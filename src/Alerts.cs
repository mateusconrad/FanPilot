// Alerts (item 16): a fan that stopped, a critical temperature, fan control the board had to give up. Checked once a
// second on their own thread from the latest snapshot (never on an HTTP request), shown in the panel (the snapshot's
// "alerts" and GET /api/alerts) and, only when the user set a destination, sent out (ntfy, Telegram) at most once
// every 10 minutes per alert. Nothing leaves the PC otherwise.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

sealed class Alert
{
    public string Id, Severity; // Severity: "critical" or "warning"
    public Msg Title, Detail;
    public DateTime Since;
    public DateTime? Resolved;
    public bool Acknowledged, Notified;

    public Dictionary<string, object> ToDictionary()
    {
        return new Dictionary<string, object> {
            { "id", Id }, { "severity", Severity }, { "title", Title.ToJson() }, { "detail", Detail.ToJson() }, { "since", UnixMs(Since) },
            { "resolved", Resolved.HasValue ? (object)UnixMs(Resolved.Value) : null }, { "acknowledged", Acknowledged }
        };
    }

    static long UnixMs(DateTime t) { return (long)(t.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds; }
}

// One header as the rules see it this second.
sealed class FanReading
{
    public string Key, Name;
    public double Rpm;
    public double? Duty; // the commanded %, or null when nobody knows (left to the BIOS)
    public bool IsPump;
}

sealed class AlertInput
{
    public DateTime Now;
    public List<FanReading> Fans = new List<FanReading>();
    public double? CpuTemp, GpuTemp;
    public bool BoardOk = true;
    public List<Msg> ControlProblems = new List<Msg>();
}

// The rules, with no clock or I/O of their own: the tests feed them sequences.
sealed class AlertRules
{
    public const double SpinningRpm = 300, StoppedRpm = 100, MinDutyForStop = 30, CoolMargin = 5;
    static readonly TimeSpan SpinToCount = TimeSpan.FromSeconds(30), StoppedFor = TimeSpan.FromSeconds(5),
        HotFor = TimeSpan.FromSeconds(10), CoolFor = TimeSpan.FromSeconds(30), BoardDownFor = TimeSpan.FromSeconds(60);
    const int RecentKept = 20;

    sealed class FanState { public DateTime? SpinningSince, LowSince; public bool Spun; }
    sealed class TempState { public DateTime? HotSince, CoolSince; }

    readonly Dictionary<string, FanState> _fans = new Dictionary<string, FanState>();
    readonly TempState _cpu = new TempState(), _gpu = new TempState();
    readonly Dictionary<string, Alert> _active = new Dictionary<string, Alert>();
    readonly List<Alert> _recent = new List<Alert>();
    DateTime? _boardDownSince;

    public int CpuLimit = 95, GpuLimit = 87;

    // The alerts raised (Resolved == null) and resolved in this step, for the notifier.
    public List<Alert> Step(AlertInput input)
    {
        var changes = new List<Alert>();
        DateTime now = input.Now;
        foreach (var f in input.Fans) StepFan(f, now, changes);
        StepTemp("temp-cpu", "CPU", input.CpuTemp, CpuLimit, _cpu, now, changes);
        StepTemp("temp-gpu", "GPU", input.GpuTemp, GpuLimit, _gpu, now, changes);

        if (input.BoardOk)
        {
            _boardDownSince = null;
            Resolve("board-down", now, changes);
        }
        else
        {
            if (_boardDownSince == null) _boardDownSince = now;
            if (now - _boardDownSince >= BoardDownFor)
                Raise("board-down", "warning", new Msg("alert.boardDown.title"), new Msg("alert.boardDown.detail"), now, changes);
        }

        var current = new HashSet<string>();
        foreach (Msg problem in input.ControlProblems)
        {
            string id = "control-" + ShortHash(problem.Text);
            current.Add(id);
            Raise(id, "warning", new Msg("alert.control.title"), problem, now, changes);
        }
        foreach (string id in _active.Keys.Where(k => k.StartsWith("control-") && !current.Contains(k)).ToList()) Resolve(id, now, changes);
        return changes;
    }

    // Only a header that really spun (300 RPM for 30 s) and is told to: one without a tachometer never alerts.
    void StepFan(FanReading f, DateTime now, List<Alert> changes)
    {
        FanState s;
        if (!_fans.TryGetValue(f.Key, out s)) _fans[f.Key] = s = new FanState();
        string id = "fan-" + ShortHash(f.Key);
        if (f.Rpm >= SpinningRpm)
        {
            if (s.SpinningSince == null) s.SpinningSince = now;
            if (now - s.SpinningSince >= SpinToCount) s.Spun = true;
            s.LowSince = null;
            Resolve(id, now, changes);
            return;
        }
        s.SpinningSince = null;
        if (!(f.Rpm < StoppedRpm && f.Duty.HasValue && f.Duty.Value >= MinDutyForStop)) { s.LowSince = null; return; }
        if (s.LowSince == null) s.LowSince = now;
        if (s.Spun && now - s.LowSince >= StoppedFor)
            Raise(id, f.IsPump ? "critical" : "warning", new Msg(f.IsPump ? "alert.pumpStopped.title" : "alert.fanStopped.title", "name", f.Name),
                  new Msg("alert.stopped.detail", "rpm", (int)Math.Round(f.Rpm), "duty", (int)Math.Round(f.Duty.Value)), now, changes);
    }

    // Raised after 10 s at or above the limit; over only after 30 s at least 5 °C below it.
    void StepTemp(string id, string what, double? t, int limit, TempState s, DateTime now, List<Alert> changes)
    {
        if (t == null) { s.HotSince = null; s.CoolSince = null; return; }
        if (t.Value >= limit)
        {
            s.CoolSince = null;
            if (s.HotSince == null) s.HotSince = now;
            if (now - s.HotSince >= HotFor)
                Raise(id, "critical", new Msg("alert.temp.title", "what", what, "temp", (int)Math.Round(t.Value)), new Msg("alert.temp.detail", "limit", limit), now, changes);
            return;
        }
        s.HotSince = null;
        if (t.Value > limit - CoolMargin) { s.CoolSince = null; return; }
        if (s.CoolSince == null) s.CoolSince = now;
        if (now - s.CoolSince >= CoolFor) Resolve(id, now, changes);
    }

    void Raise(string id, string severity, Msg title, Msg detail, DateTime now, List<Alert> changes)
    {
        if (_active.ContainsKey(id)) return;
        var a = new Alert { Id = id, Severity = severity, Title = title, Detail = detail, Since = now };
        _active[id] = a;
        changes.Add(a);
    }

    void Resolve(string id, DateTime now, List<Alert> changes)
    {
        Alert a;
        if (!_active.TryGetValue(id, out a)) return;
        _active.Remove(id);
        a.Resolved = now;
        _recent.Insert(0, a);
        if (_recent.Count > RecentKept) _recent.RemoveAt(_recent.Count - 1);
        changes.Add(a);
    }

    public List<Alert> Active { get { return _active.Values.OrderBy(a => a.Severity == "critical" ? 0 : 1).ThenBy(a => a.Since).ToList(); } }
    public List<Alert> Recent { get { return _recent.ToList(); } }

    public bool Acknowledge(string id)
    {
        Alert a;
        if (!_active.TryGetValue(id, out a)) return false;
        a.Acknowledged = true;
        return true;
    }

    // Short, stable and safe in a URL or an element id, whatever the header key or the message says.
    internal static string ShortHash(string s)
    {
        using (var sha = SHA1.Create())
            return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(s)).Take(4).Select(b => b.ToString("x2")));
    }
}

// Limits and destinations, in alerts.json. Secrets (the ntfy topic URL, the Telegram token) are encrypted with DPAPI
// for this machine: copied to another PC, the file is useless, and the panel never gets them back.
sealed class AlertSettings
{
    static readonly byte[] Entropy = Encoding.UTF8.GetBytes("FanPilot alertas");

    public int CpuTemp = 95, GpuTemp = 87;
    public string PcName = Environment.MachineName;
    // What the messages that leave the PC are written in: the panel's language when the settings were saved, else
    // Windows' (item 21).
    public string Language = I18n.SystemLanguage;
    public string NtfyUrl, TelegramToken, TelegramChatId;

    public bool HasDestination { get { return NtfyUrl != null || (TelegramToken != null && TelegramChatId != null); } }

    public static AlertSettings Load(string path)
    {
        var s = new AlertSettings();
        var d = DataFile.Read(path);
        if (d == null) return s;
        s.CpuTemp = Json.GetInt(d, "cpuTemp", s.CpuTemp);
        s.GpuTemp = Json.GetInt(d, "gpuTemp", s.GpuTemp);
        s.PcName = Json.GetString(d, "pcName", s.PcName);
        s.Language = I18n.Of(Json.GetString(d, "language", s.Language));
        s.TelegramChatId = Json.GetString(d, "telegramChatId", null);
        try
        {
            s.NtfyUrl = Secrets.Unprotect(Json.GetString(d, "ntfyUrl", null), Entropy);
            s.TelegramToken = Secrets.Unprotect(Json.GetString(d, "telegramToken", null), Entropy);
        }
        catch (CryptographicException)
        {
            s.NtfyUrl = s.TelegramToken = null;
            AppNotices.Add(new Msg("notice.alertsOtherPc"));
        }
        return s;
    }

    public void Save(string path)
    {
        DataFile.WriteAtomic(path, new Dictionary<string, object> {
            { "cpuTemp", CpuTemp }, { "gpuTemp", GpuTemp }, { "pcName", PcName }, { "language", Language },
            { "ntfyUrl", Secrets.Protect(NtfyUrl, Entropy) }, { "telegramToken", Secrets.Protect(TelegramToken, Entropy) }, { "telegramChatId", TelegramChatId }
        });
    }

    // What the panel sees: the secrets only as "configured", with their last characters to tell them apart.
    public Dictionary<string, object> Describe()
    {
        return new Dictionary<string, object> {
            { "cpuTemp", CpuTemp }, { "gpuTemp", GpuTemp }, { "pcName", PcName }, { "language", Language },
            { "ntfy", NtfyUrl == null ? null : Masked(NtfyUrl) },
            { "telegram", TelegramToken == null ? null : Masked(TelegramToken) }, { "telegramChatId", TelegramChatId }
        };
    }

    // POST /api/alerts/settings. A field left out stays as it is; "" removes a destination. ArgumentException says
    // what is wrong, and nothing changes then.
    public AlertSettings With(Dictionary<string, object> body)
    {
        var s = (AlertSettings)MemberwiseClone();
        s.CpuTemp = Json.GetInt(body, "cpuTemp", CpuTemp);
        s.GpuTemp = Json.GetInt(body, "gpuTemp", GpuTemp);
        if (s.CpuTemp < 60 || s.CpuTemp > 105) throw new UserError("err.alerts.cpu", "min", 60, "max", 105);
        if (s.GpuTemp < 60 || s.GpuTemp > 100) throw new UserError("err.alerts.gpu", "min", 60, "max", 100);
        s.Language = I18n.Of(Json.GetString(body, "language", Language));
        string name = Json.GetString(body, "pcName", PcName);
        if (name != null) name = name.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 40 || name.Any(char.IsControl)) throw new UserError("err.alerts.pcName", "max", 40);
        s.PcName = name;
        s.NtfyUrl = Secret(body, "ntfyUrl", NtfyUrl);
        if (s.NtfyUrl != null)
        {
            Uri u;
            if (!Uri.TryCreate(s.NtfyUrl, UriKind.Absolute, out u) || (u.Scheme != "https" && u.Scheme != "http") || u.AbsolutePath.Trim('/').Length == 0)
                throw new UserError("err.alerts.ntfy");
        }
        s.TelegramToken = Secret(body, "telegramToken", TelegramToken);
        s.TelegramChatId = Secret(body, "telegramChatId", TelegramChatId);
        if ((s.TelegramToken == null) != (s.TelegramChatId == null)) throw new UserError("err.alerts.telegram");
        return s;
    }

    static string Secret(Dictionary<string, object> body, string key, string current)
    {
        if (Json.Get(body, key) == null) return current;
        string v = Json.GetString(body, key, "").Trim();
        return v.Length == 0 ? null : v;
    }

    static string Masked(string secret) { return "…" + secret.Substring(Math.Max(0, secret.Length - 4)); }
}

// What leaves the PC: the alert and the name the user gave the PC. No IPs, no serial numbers.
static class AlertSender
{
    // The tests point it at a local server.
    internal static string TelegramApi = "https://api.telegram.org";

    // Each configured destination, one after the other; returns what failed ("ntfy: …"), empty when all arrived.
    public static List<string> Send(AlertSettings s, Msg title, Msg detail, bool critical)
    {
        var errors = new List<string>();
        string heading = s.PcName + ": " + title.In(s.Language), text = detail.In(s.Language);
        if (s.NtfyUrl != null)
        {
            string url = s.NtfyUrl + (s.NtfyUrl.Contains("?") ? "&" : "?") + "title=" + Uri.EscapeDataString(heading) +
                         "&priority=" + (critical ? "high" : "default") + "&tags=" + (critical ? "rotating_light" : "warning");
            Try(errors, "ntfy", () => Post(url, "text/plain; charset=utf-8", text));
        }
        if (s.TelegramToken != null && s.TelegramChatId != null)
        {
            string json = Json.Serialize(new Dictionary<string, object> { { "chat_id", s.TelegramChatId }, { "text", heading + "\n" + text } });
            Try(errors, "Telegram", () => Post(TelegramApi + "/bot" + s.TelegramToken + "/sendMessage", "application/json; charset=utf-8", json));
        }
        return errors;
    }

    static void Try(List<string> errors, string name, Action send)
    {
        try { send(); }
        catch (WebException ex) { errors.Add(name + ": " + ex.Message); }
        catch (IOException ex) { errors.Add(name + ": " + ex.Message); }
    }

    static void Post(string url, string contentType, string body)
    {
        var req = (HttpWebRequest)WebRequest.Create(url);
        req.Method = "POST";
        req.ContentType = contentType;
        req.Timeout = req.ReadWriteTimeout = 10000;
        req.UserAgent = "FanPilot/" + AppVersion.Text;
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        req.ContentLength = bytes.Length;
        using (var s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
        using (req.GetResponse()) { }
    }
}

sealed class AlertMonitor
{
    static readonly TimeSpan NotifyEvery = TimeSpan.FromMinutes(10);
    readonly Sampler _sampler;
    readonly IBoard _board;
    readonly string _settingsPath;
    readonly Func<DateTime> _now;
    readonly Action<AlertSettings, Msg, Msg, bool> _send;
    readonly AlertRules _rules = new AlertRules();
    readonly Dictionary<string, DateTime> _lastSent = new Dictionary<string, DateTime>();
    readonly object _sync = new object();
    AlertSettings _settings;
    readonly FanListCache _fanList; // what each fan is told to do, for the "stopped" rule
    volatile bool _stopped;

    public AlertMonitor(Sampler sampler, FanManager fans, IBoard board, string dataDir)
        : this(sampler, fans, board, dataDir, () => DateTime.Now, SendInBackground) { }

    // now and send: the tests move the clock and see what would have left the PC.
    internal AlertMonitor(Sampler sampler, FanManager fans, IBoard board, string dataDir, Func<DateTime> now, Action<AlertSettings, Msg, Msg, bool> send)
    {
        _sampler = sampler; _board = board; _now = now; _send = send;
        _fanList = new FanListCache(fans, now);
        _settingsPath = Path.Combine(dataDir, "alerts.json");
        _settings = AlertSettings.Load(_settingsPath);
    }

    public void Start()
    {
        new Thread(() =>
        {
            while (!_stopped)
            {
                try { Tick(); } catch (Exception ex) { Log.Warn("Alerts: " + ex.Message); }
                Thread.Sleep(1000);
            }
        }) { IsBackground = true, Name = "alerts" }.Start();
    }

    public void Stop() { _stopped = true; }

    internal void Tick() { Tick(Input()); }

    // The tests feed the input themselves.
    internal void Tick(AlertInput input)
    {
        List<Alert> changes;
        lock (_sync)
        {
            _rules.CpuLimit = _settings.CpuTemp;
            _rules.GpuLimit = _settings.GpuTemp;
            changes = _rules.Step(input);
            Notify(changes, input.Now);
        }
        foreach (var a in changes)
        {
            if (a.Resolved != null) Log.Info("Alert resolved: " + a.Title.Text);
            else if (a.Severity == "critical") Log.Error("Alert: " + a.Title.Text + ". " + a.Detail.Text);
            else Log.Warn("Alert: " + a.Title.Text + ". " + a.Detail.Text);
        }
    }

    // At most one message per alert every 10 minutes, and one more when it ends if its start went out.
    void Notify(List<Alert> changes, DateTime now)
    {
        var s = _settings;
        if (!s.HasDestination) return;
        foreach (var a in changes)
        {
            if (a.Resolved == null)
            {
                DateTime last;
                if (_lastSent.TryGetValue(a.Id, out last) && now - last < NotifyEvery) continue;
                _lastSent[a.Id] = now;
                a.Notified = true;
                _send(s, a.Title, a.Detail, a.Severity == "critical");
            }
            else if (a.Notified) _send(s, new Msg("alert.resolved.title", "title", a.Title), new Msg("alert.resolved.detail"), false);
        }
    }

    // Sending never holds up the rules: one thread per message, each with its own 10 s timeouts.
    static void SendInBackground(AlertSettings s, Msg title, Msg detail, bool critical)
    {
        new Thread(() =>
        {
            var errors = AlertSender.Send(s, title, detail, critical);
            if (errors.Count > 0) Log.Warn("Alerts: could not send (" + string.Join("; ", errors) + ").");
        }) { IsBackground = true, Name = "alert-send" }.Start();
    }

    AlertInput Input()
    {
        var input = new AlertInput { Now = _now() };
        var snap = _sampler.Latest;
        if (snap == null) return input;
        var service = Json.Get(snap, "service") as Dictionary<string, object>;
        input.BoardOk = service == null || !(Json.Get(service, "ok") is bool ok) || ok;
        var sensors = Json.Get(snap, "sensors") as IEnumerable<Dictionary<string, object>>;
        var cpu = sensors == null ? null : sensors.FirstOrDefault(s => Json.GetString(s, "role", "") == CpuRoles.Main)
                  ?? sensors.FirstOrDefault(s => Json.GetString(s, "type", "") == "Temperature" && Json.GetString(s, "title", "") == "CPU");
        if (cpu != null) input.CpuTemp = Json.ToDouble(Json.Get(cpu, "value"));
        var gpu = Json.Get(snap, "gpu") as Dictionary<string, object>;
        if (gpu != null && Json.Get(gpu, "ok") is bool gpuOk && gpuOk && Json.Get(gpu, "temp") != null) input.GpuTemp = Json.ToDouble(Json.Get(gpu, "temp"));
        input.ControlProblems = _board.ControlProblems();

        var rpms = Json.Get(snap, "fanRpm") as List<int>;
        var temps = Json.Get(snap, "tempInputs") as List<double>;
        var fans = _fanList.Get();
        if (rpms == null || fans == null) return input;
        for (int i = 0; i < fans.Count && i < rpms.Count; i++)
        {
            var f = fans[i];
            double? duty = FanListCache.DutyNow(f, temps, input.GpuTemp);
            input.Fans.Add(new FanReading {
                Key = Json.GetString(f, "key", "fan-" + i), Name = Json.GetString(f, "label", null) ?? Json.GetString(f, "title", "?"),
                Rpm = rpms[i], Duty = duty, IsPump = Json.GetString(f, "role", "fan") == "pump"
            });
        }
        return input;
    }

    public List<Dictionary<string, object>> ActiveForSnapshot()
    {
        lock (_sync) return _rules.Active.Select(a => a.ToDictionary()).ToList();
    }

    public Dictionary<string, object> Describe()
    {
        lock (_sync)
            return new Dictionary<string, object> {
                { "active", _rules.Active.Select(a => a.ToDictionary()).ToList() },
                { "recent", _rules.Recent.Select(a => a.ToDictionary()).ToList() }
            };
    }

    public bool Acknowledge(string id) { lock (_sync) return _rules.Acknowledge(id); }

    public Dictionary<string, object> Settings() { lock (_sync) return _settings.Describe(); }

    public Dictionary<string, object> UpdateSettings(Dictionary<string, object> body)
    {
        lock (_sync)
        {
            var updated = _settings.With(body);
            updated.Save(_settingsPath);
            _settings = updated;
            Log.Info("Alerts: limits CPU " + updated.CpuTemp + " °C, GPU " + updated.GpuTemp + " °C; messages in " + updated.Language + "; destinations: " +
                     (updated.NtfyUrl != null ? "ntfy " : "") + (updated.TelegramToken != null ? "Telegram" : "") + (updated.HasDestination ? "" : "none"));
            return updated.Describe();
        }
    }

    // "Enviar teste": right now, and waiting for the answer, so the panel can say what failed.
    public Dictionary<string, object> SendTest()
    {
        AlertSettings s;
        lock (_sync) s = _settings;
        if (!s.HasDestination) throw new UserError("err.alerts.noDestination");
        var errors = AlertSender.Send(s, new Msg("alert.test.title"), new Msg("alert.test.detail"), false);
        return new Dictionary<string, object> { { "ok", errors.Count == 0 }, { "errors", errors } };
    }
}
