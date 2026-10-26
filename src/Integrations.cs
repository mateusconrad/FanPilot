// Item 23: FanPilot's readings for Prometheus/Grafana and Home Assistant, read only. An integration gets in with a
// token made on the PC (the /pair page): "Authorization: Bearer fp_…". Only its SHA-256 is kept, in
// data\integrations.json, and it opens /metrics and /api/v1/sensors: nothing that changes a fan.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

sealed class IntegrationToken
{
    public string Id, Name, Hash;
    public DateTime CreatedUtc;
    public DateTime? LastUsedUtc;

    // What the /pair page lists: never the hash.
    public Dictionary<string, object> Describe()
    {
        return new Dictionary<string, object> {
            { "id", Id }, { "name", Name }, { "created", CreatedUtc.ToString("o") },
            { "lastUsed", LastUsedUtc.HasValue ? LastUsedUtc.Value.ToString("o") : null }
        };
    }
}

sealed class IntegrationTokens
{
    public const string Prefix = "fp_";
    public const int MaxTokens = 20, MaxName = 40;

    readonly object _sync = new object();
    readonly string _path;
    readonly Func<DateTime> _utcNow;
    readonly List<IntegrationToken> _tokens = new List<IntegrationToken>();
    DateTime _lastSave = DateTime.MinValue;

    public IntegrationTokens(string dataDir) : this(dataDir, () => DateTime.UtcNow) { }

    // utcNow: the tests move the clock.
    internal IntegrationTokens(string dataDir, Func<DateTime> utcNow)
    {
        _path = Path.Combine(dataDir, "integrations.json");
        _utcNow = utcNow;
        Load();
    }

    // "Bearer fp_…" → "fp_…"; null without that header (or with another scheme).
    public static string FromHeader(string authorization)
    {
        if (string.IsNullOrEmpty(authorization) || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
        string token = authorization.Substring(7).Trim();
        return token.Length == 0 ? null : token;
    }

    // A new token, in the answer this once: { id, name, created, lastUsed, token }.
    public Dictionary<string, object> Create(string name)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0 || name.Length > MaxName || name.Any(char.IsControl)) throw new UserError("err.token.name", "max", MaxName);
        lock (_sync)
        {
            if (_tokens.Count >= MaxTokens) throw new UserError("err.token.tooMany", "max", MaxTokens);
            string token = Prefix + AuthManager.RandomToken(32);
            var t = new IntegrationToken { Id = AuthManager.RandomToken(6), Name = name, Hash = AuthManager.Hash(token), CreatedUtc = _utcNow() };
            _tokens.Add(t);
            Save();
            Log.Info("Integration token created: " + name + " (" + t.Id + ").");
            var d = t.Describe();
            d["token"] = token;
            return d;
        }
    }

    // The token's entry, or null: unknown or revoked.
    public IntegrationToken Validate(string token)
    {
        if (string.IsNullOrEmpty(token) || !token.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        string hash = AuthManager.Hash(token);
        lock (_sync)
        {
            var t = _tokens.FirstOrDefault(x => x.Hash == hash);
            if (t == null) return null;
            t.LastUsedUtc = _utcNow();
            // A scrape every 15 s: the last use goes to disk every few minutes, not every time.
            if (_utcNow() - _lastSave > TimeSpan.FromMinutes(5)) Save();
            return t;
        }
    }

    public bool Revoke(string id)
    {
        lock (_sync)
        {
            var t = _tokens.FirstOrDefault(x => x.Id == id);
            if (t == null) return false;
            _tokens.Remove(t);
            Save();
            Log.Info("Integration token revoked: " + t.Name + " (" + t.Id + ").");
            return true;
        }
    }

    public List<Dictionary<string, object>> List()
    {
        lock (_sync) return _tokens.OrderByDescending(t => t.CreatedUtc).Select(t => t.Describe()).ToList();
    }

    void Load()
    {
        var saved = DataFile.Read(_path);
        var tokens = Json.Get(saved, "tokens") as System.Collections.IEnumerable;
        if (tokens == null) return;
        foreach (object o in tokens)
        {
            var d = o as Dictionary<string, object>;
            if (d == null) continue;
            try
            {
                string used = Json.GetString(d, "lastUsed", null);
                var t = new IntegrationToken
                {
                    Id = Json.GetString(d, "id", ""),
                    Name = Json.GetString(d, "name", ""),
                    Hash = Json.GetString(d, "hash", ""),
                    CreatedUtc = DateTime.Parse(Json.GetString(d, "created", ""), null, DateTimeStyles.RoundtripKind),
                    LastUsedUtc = used == null ? (DateTime?)null : DateTime.Parse(used, null, DateTimeStyles.RoundtripKind)
                };
                if (t.Hash.Length == 64 && t.Id.Length > 0) _tokens.Add(t);
            }
            catch (FormatException) { } // one damaged entry does not cost the others (that integration gets a new token)
        }
    }

    void Save()
    {
        _lastSave = _utcNow();
        DataFile.WriteAtomic(_path, new Dictionary<string, object> {
            { "tokens", _tokens.Select(t => new Dictionary<string, object> {
                { "id", t.Id }, { "name", t.Name }, { "hash", t.Hash }, { "created", t.CreatedUtc.ToString("o") },
                { "lastUsed", t.LastUsedUtc.HasValue ? t.LastUsedUtc.Value.ToString("o") : null } }).ToList() }
        });
    }
}

// What the integrations read: the latest snapshot as the stable JSON of /api/v1/sensors (version 1: fields are only
// ever added, while the panel's snapshot changes with the panel), and the same readings in Prometheus' text format.
sealed class IntegrationFeed
{
    public const int Version = 1;

    readonly Sampler _sampler;
    readonly FanListCache _fans;
    readonly Func<string> _backend;

    public IntegrationFeed(Sampler sampler, FanListCache fans, Func<string> backend)
    {
        _sampler = sampler; _fans = fans; _backend = backend;
    }

    // Before the first snapshot: board.ok false and nothing else read.
    public Dictionary<string, object> SensorsV1()
    {
        return Build(_sampler.Latest, _fans.Get(), _backend(), MachineData.Board, Environment.MachineName);
    }

    public string Metrics() { return Prometheus(SensorsV1()); }

    // snap: Sampler.Sample's; fans: FanManager.Describe's, index-aligned with snap.fanRpm.
    internal static Dictionary<string, object> Build(Dictionary<string, object> snap, List<Dictionary<string, object>> fans, string backend, string board, string pc)
    {
        var service = Json.Get(snap, "service") as Dictionary<string, object>;
        bool up = snap != null && !(Json.Get(service, "ok") is bool ok && !ok);
        var sensors = (Json.Get(snap, "sensors") as IEnumerable<Dictionary<string, object>>) ?? new List<Dictionary<string, object>>();
        var cpu = sensors.FirstOrDefault(s => Json.GetString(s, "role", null) == CpuRoles.Main);
        var cpuPower = sensors.FirstOrDefault(s => Json.GetString(s, "type", "") == "Power" && Json.GetString(s, "title", "") == "CPU Package");
        Func<string, List<object>> ofType = type => sensors.Where(s => Json.GetString(s, "type", "") == type && Json.Get(s, "value") != null)
            .Select(s => (object)new Dictionary<string, object> {
                { "id", Json.GetString(s, "id", "") }, { "name", Json.GetString(s, "title", "") }, { "value", Round(Json.Get(s, "value")) } })
            .ToList();

        var gpuSnap = Json.Get(snap, "gpu") as Dictionary<string, object>;
        Dictionary<string, object> gpu = null;
        double? gpuTemp = null;
        if (gpuSnap != null && Json.Get(gpuSnap, "ok") is bool gpuOk && gpuOk)
        {
            gpuTemp = Number(Json.Get(gpuSnap, "temp"));
            var gpuFans = Json.Get(gpuSnap, "fans") as IEnumerable<int>;
            gpu = new Dictionary<string, object> {
                { "name", Json.GetString(gpuSnap, "name", "") }, { "temperature", gpuTemp }, { "power", Number(Json.Get(gpuSnap, "power")) },
                { "utilization", Number(Json.Get(gpuSnap, "util")) },
                { "fan", gpuFans != null && gpuFans.Any() ? (object)gpuFans.First() : null }
            };
        }

        var rpm = Json.Get(snap, "fanRpm") as IList<int>;
        var temps = Json.Get(snap, "tempInputs") as IList<double>;
        var fanList = new List<object>();
        if (fans != null)
            for (int i = 0; i < fans.Count; i++)
            {
                var f = fans[i];
                double? duty = FanListCache.DutyNow(f, temps, gpuTemp);
                fanList.Add(new Dictionary<string, object> {
                    { "id", Json.GetString(f, "key", "fan-" + i) }, { "name", Json.GetString(f, "title", "") },
                    { "label", Json.GetString(f, "label", null) }, { "pump", Json.GetString(f, "role", "fan") == "pump" },
                    { "rpm", rpm != null && i < rpm.Count ? (object)rpm[i] : null },
                    { "duty", duty.HasValue ? (object)Math.Round(duty.Value) : null }
                });
            }

        var alerts = (Json.Get(snap, "alerts") as IEnumerable<Dictionary<string, object>>) ?? new List<Dictionary<string, object>>();
        return new Dictionary<string, object> {
            { "version", Version },
            { "time", snap == null ? null : DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(snap["t"], CultureInfo.InvariantCulture)).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) },
            { "fanpilot", AppVersion.Text },
            { "pc", pc },
            { "board", new Dictionary<string, object> { { "name", board }, { "backend", backend }, { "ok", up } } },
            { "cpu", new Dictionary<string, object> {
                { "temperature", cpu == null ? null : Round(Json.Get(cpu, "value")) },
                { "power", cpuPower == null ? null : Round(Json.Get(cpuPower, "value")) } } },
            { "gpu", gpu },
            { "fans", fanList },
            { "temperatures", ofType("Temperature") },
            { "power", ofType("Power") },
            // The English text: integrations have no language of their own.
            { "alerts", alerts.Select(a => (object)new Dictionary<string, object> {
                { "id", Json.GetString(a, "id", "") }, { "severity", Json.GetString(a, "severity", "") }, { "title", Json.GetString(Json.Get(a, "title") as Dictionary<string, object>, "text", "") } }).ToList() },
        };
    }

    static object Round(object v)
    {
        double? d = Number(v);
        return d.HasValue ? (object)Math.Round(d.Value, 1) : null;
    }

    static double? Number(object v)
    {
        if (v == null) return null;
        double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
        return double.IsNaN(d) || double.IsInfinity(d) ? (double?)null : d;
    }

    // The Prometheus text format (version 0.0.4): one HELP and TYPE per family, a family only with samples.
    internal static string Prometheus(Dictionary<string, object> v1)
    {
        var sb = new StringBuilder();
        var board = (Dictionary<string, object>)v1["board"];
        var cpu = (Dictionary<string, object>)v1["cpu"];
        var gpu = v1["gpu"] as Dictionary<string, object>;
        var fans = ((List<object>)v1["fans"]).Cast<Dictionary<string, object>>().ToList();
        Func<Dictionary<string, object>, string> fanLabels = f => Labels("fan", f["name"], "name", f["label"] ?? f["name"]);
        Func<Dictionary<string, object>, string> sensorLabels = s => Labels("sensor", s["id"], "name", s["name"]);
        string gpuLabels = gpu == null ? "" : Labels("gpu", gpu["name"]);

        Family(sb, "fanpilot_up", "1 while FanPilot reads the motherboard's sensors.", new[] { Sample("", (bool)board["ok"] ? 1 : 0) });
        Family(sb, "fanpilot_info", "FanPilot's version, the motherboard and the way FanPilot reaches it.",
            new[] { Sample(Labels("version", v1["fanpilot"], "board", board["name"], "backend", board["backend"], "pc", v1["pc"]), 1) });
        Family(sb, "fanpilot_cpu_temperature_celsius", "The CPU temperature the panel shows (the cores when there is a reading of them).", new[] { Sample("", cpu["temperature"]) });
        Family(sb, "fanpilot_cpu_power_watts", "The CPU package power.", new[] { Sample("", cpu["power"]) });
        Family(sb, "fanpilot_temperature_celsius", "Every temperature sensor FanPilot reads.",
            ((List<object>)v1["temperatures"]).Cast<Dictionary<string, object>>().Select(s => Sample(sensorLabels(s), s["value"])));
        Family(sb, "fanpilot_power_watts", "Every power reading FanPilot has.",
            ((List<object>)v1["power"]).Cast<Dictionary<string, object>>().Select(s => Sample(sensorLabels(s), s["value"])));
        Family(sb, "fanpilot_fan_rpm", "Motherboard fan speed, from its tachometer.", fans.Select(f => Sample(fanLabels(f), f["rpm"])));
        Family(sb, "fanpilot_fan_duty_percent", "The speed the fan's curve asks for at the current temperature; absent while the BIOS runs the fan.",
            fans.Select(f => Sample(fanLabels(f), f["duty"])));
        if (gpu != null)
        {
            Family(sb, "fanpilot_gpu_temperature_celsius", "GPU temperature.", new[] { Sample(gpuLabels, gpu["temperature"]) });
            Family(sb, "fanpilot_gpu_power_watts", "GPU power.", new[] { Sample(gpuLabels, gpu["power"]) });
            Family(sb, "fanpilot_gpu_utilization_percent", "GPU utilization.", new[] { Sample(gpuLabels, gpu["utilization"]) });
            Family(sb, "fanpilot_gpu_fan_percent", "GPU fan speed.", new[] { Sample(gpuLabels, gpu["fan"]) });
        }
        var alerts = ((List<object>)v1["alerts"]).Cast<Dictionary<string, object>>().ToList();
        Family(sb, "fanpilot_alerts_active", "Alerts active now, by severity.", new[] { "critical", "warning" }
            .Select(sev => Sample(Labels("severity", sev), alerts.Count(a => (string)a["severity"] == sev))));
        return sb.ToString();
    }

    static KeyValuePair<string, object> Sample(string labels, object value) { return new KeyValuePair<string, object>(labels, value); }

    static void Family(StringBuilder sb, string name, string help, IEnumerable<KeyValuePair<string, object>> samples)
    {
        var lines = samples.Where(s => s.Value != null).ToList();
        if (lines.Count == 0) return;
        sb.Append("# HELP ").Append(name).Append(' ').Append(help).Append('\n');
        sb.Append("# TYPE ").Append(name).Append(" gauge\n");
        foreach (var s in lines)
            sb.Append(name).Append(s.Key).Append(' ').Append(Convert.ToDouble(s.Value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture)).Append('\n');
    }

    // {name="value",…} with the format's escapes (backslash, double quote, line break).
    static string Labels(params object[] nameValue)
    {
        var parts = new List<string>();
        for (int i = 0; i + 1 < nameValue.Length; i += 2)
        {
            string v = Convert.ToString(nameValue[i + 1], CultureInfo.InvariantCulture) ?? "";
            parts.Add(nameValue[i] + "=\"" + v.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"");
        }
        return "{" + string.Join(",", parts) + "}";
    }
}
