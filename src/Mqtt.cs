// Item 23, part 2: FanPilot's readings on an MQTT broker, with Home Assistant's discovery, so the entities show up on
// their own, under one device for this PC. Read only. The settings are in data\mqtt.json, the password encrypted for
// this PC. Every 5 s the state (the JSON of /api/v1/sensors) goes to fanpilot/<pc>/state, and
// fanpilot/<pc>/availability says online, or offline: FanPilot says it when it stops, and the broker says it for
// FanPilot when the connection drops (the last will).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using MQTTnet;
using MQTTnet.Adapter;
using MQTTnet.Client;
using MQTTnet.Protocol;

sealed class MqttSettings
{
    static readonly byte[] Entropy = Encoding.UTF8.GetBytes("FanPilot mqtt");
    // A name or an IP (IPv6 too), no scheme: "192.168.0.5", "homeassistant.local".
    static readonly Regex HostPattern = new Regex(@"^[A-Za-z0-9._:-]+$");
    static readonly Regex PrefixPattern = new Regex(@"^[A-Za-z0-9_-]+(/[A-Za-z0-9_-]+)*$");
    public const int DefaultPort = 1883, MaxUser = 100, MaxPassword = 200;

    public bool Enabled;
    public string Host;
    public int Port = DefaultPort;
    public bool Tls;
    public string Username, Password;
    public string DiscoveryPrefix = "homeassistant";
    // The entities' names go out in the language of the panel the settings were saved from, like the alerts (item 21).
    public string Language = I18n.SystemLanguage;

    public string Broker { get { return Host + ":" + Port; } }

    public static MqttSettings Load(string path)
    {
        var s = new MqttSettings();
        var d = DataFile.Read(path);
        if (d == null) return s;
        s.Enabled = Json.Get(d, "enabled") is bool on && on;
        s.Host = Json.GetString(d, "host", null);
        s.Port = Json.GetInt(d, "port", DefaultPort);
        s.Tls = Json.Get(d, "tls") is bool tls && tls;
        s.Username = Json.GetString(d, "username", null);
        s.DiscoveryPrefix = Json.GetString(d, "discoveryPrefix", s.DiscoveryPrefix);
        s.Language = I18n.Of(Json.GetString(d, "language", s.Language));
        try { s.Password = Secrets.Unprotect(Json.GetString(d, "password", null), Entropy); }
        catch (CryptographicException) { AppNotices.Add(new Msg("notice.mqttOtherPc")); }
        if (s.Host == null) s.Enabled = false;
        return s;
    }

    public void Save(string path)
    {
        DataFile.WriteAtomic(path, new Dictionary<string, object> {
            { "enabled", Enabled }, { "host", Host }, { "port", Port }, { "tls", Tls }, { "username", Username },
            { "password", Secrets.Protect(Password, Entropy) }, { "discoveryPrefix", DiscoveryPrefix }, { "language", Language }
        });
    }

    // What the panel sees: whether there is a password, never the password.
    public Dictionary<string, object> Describe()
    {
        return new Dictionary<string, object> {
            { "enabled", Enabled }, { "host", Host }, { "port", Port }, { "tls", Tls }, { "username", Username },
            { "hasPassword", Password != null }, { "discoveryPrefix", DiscoveryPrefix }, { "language", Language }
        };
    }

    // POST /api/mqtt. A field left out stays as it is; the password goes only when typed, and "" removes it.
    public MqttSettings With(Dictionary<string, object> body)
    {
        var s = (MqttSettings)MemberwiseClone();
        object enabled = Json.Get(body, "enabled"), tls = Json.Get(body, "tls");
        if (enabled != null) s.Enabled = Convert.ToBoolean(enabled);
        if (tls != null) s.Tls = Convert.ToBoolean(tls);
        string host = (Json.GetString(body, "host", Host) ?? "").Trim();
        s.Host = host.Length == 0 ? null : host;
        if (s.Host != null && (s.Host.Length > 253 || !HostPattern.IsMatch(s.Host))) throw new UserError("err.mqtt.host");
        if (s.Enabled && s.Host == null) throw new UserError("err.mqtt.host");
        s.Port = Json.GetInt(body, "port", Port);
        if (s.Port < 1 || s.Port > 65535) throw new UserError("err.mqtt.port", "min", 1, "max", 65535);
        string user = (Json.GetString(body, "username", Username) ?? "").Trim();
        s.Username = user.Length == 0 ? null : user;
        if (s.Username != null && (s.Username.Length > MaxUser || s.Username.Any(char.IsControl))) throw new UserError("err.mqtt.user", "max", MaxUser);
        if (Json.Get(body, "password") != null)
        {
            string password = Json.GetString(body, "password", "");
            s.Password = password.Length == 0 ? null : password;
        }
        if (s.Password != null && s.Password.Length > MaxPassword) throw new UserError("err.mqtt.password", "max", MaxPassword);
        string prefix = (Json.GetString(body, "discoveryPrefix", DiscoveryPrefix) ?? "").Trim();
        s.DiscoveryPrefix = prefix.Length == 0 ? "homeassistant" : prefix;
        if (s.DiscoveryPrefix.Length > 64 || !PrefixPattern.IsMatch(s.DiscoveryPrefix)) throw new UserError("err.mqtt.prefix");
        s.Language = I18n.Of(Json.GetString(body, "language", Language));
        return s;
    }
}

sealed class MqttPublisher
{
    public const int StateSeconds = 5;
    static readonly int[] RetrySeconds = { 5, 10, 30, 60 };

    readonly string _path;
    readonly Func<Dictionary<string, object>> _readings;
    readonly string _pc, _node, _stateTopic, _availabilityTopic;
    readonly object _sync = new object();
    readonly CancellationTokenSource _stop = new CancellationTokenSource();
    MqttSettings _settings;
    int _generation;           // one more at every save: the connection starts over with the new settings
    bool _stopped;
    Thread _thread;
    string _state = "off";     // off, connecting, connected, error
    Msg _problem;
    int _entities;
    string _logged;            // the last problem written to the log: a line per new problem, not one per retry

    // readings: IntegrationFeed.SensorsV1. pc: the device's name in Home Assistant, and the topics' (sanitized).
    public MqttPublisher(string dataDir, Func<Dictionary<string, object>> readings, string pc)
    {
        _path = System.IO.Path.Combine(dataDir, "mqtt.json");
        _readings = readings;
        _pc = pc;
        _node = Node(pc);
        _stateTopic = "fanpilot/" + _node + "/state";
        _availabilityTopic = "fanpilot/" + _node + "/availability";
        _settings = MqttSettings.Load(_path);
    }

    // "PC-CONRAD" → "pc-conrad": topics and Home Assistant's ids take letters, digits, - and _.
    internal static string Node(string pc)
    {
        var sb = new StringBuilder();
        foreach (char c in (pc ?? "").ToLowerInvariant()) sb.Append((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_' ? c : '_');
        return sb.Length == 0 ? "pc" : sb.ToString();
    }

    public void Start()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "mqtt" };
        _thread.Start();
    }

    // "offline" and a clean disconnect, in at most a second or so; past that, the broker's last will says it.
    public void Stop()
    {
        lock (_sync)
        {
            if (_stopped) return;
            _stopped = true;
            Monitor.PulseAll(_sync);
        }
        _stop.Cancel();
        if (_thread != null) _thread.Join(1500);
    }

    public Dictionary<string, object> Describe()
    {
        lock (_sync)
        {
            var d = _settings.Describe();
            d["status"] = new Dictionary<string, object> {
                { "state", _settings.Enabled ? _state : "off" }, { "error", _problem == null ? null : _problem.ToJson() },
                { "entities", _entities }, { "topic", "fanpilot/" + _node }
            };
            return d;
        }
    }

    public Dictionary<string, object> Update(Dictionary<string, object> body)
    {
        lock (_sync)
        {
            var updated = _settings.With(body);
            updated.Save(_path);
            _settings = updated;
            _generation++;
            _state = updated.Enabled ? "connecting" : "off";
            _problem = null;
            _logged = null;
            Monitor.PulseAll(_sync);
            Log.Info("MQTT: " + (updated.Enabled ? "on, broker " + updated.Broker + (updated.Tls ? " (TLS)" : "") + (updated.Username != null ? ", with a user" : "") : "off") + ".");
        }
        return Describe();
    }

    // Up to ms, or until the settings change or FanPilot stops.
    void Wait(int generation, int ms)
    {
        lock (_sync)
        {
            if (_stopped || _generation != generation) return;
            Monitor.Wait(_sync, ms);
        }
    }

    bool Current(int generation) { lock (_sync) return !_stopped && _generation == generation; }

    void SetState(int generation, string state, Msg problem, int entities)
    {
        bool log;
        lock (_sync)
        {
            if (_generation != generation) return; // an older connection's news
            _state = state; _problem = problem; _entities = entities;
            if (state == "connected") _logged = null;
            log = problem != null && problem.Text != _logged;
            if (log) _logged = problem.Text;
        }
        if (log) Log.Warn(new Msg("mqtt.log.problem", "problem", problem));
    }

    void Loop()
    {
        var factory = new MqttFactory();
        int failures = 0;
        while (true)
        {
            MqttSettings s;
            int generation;
            lock (_sync)
            {
                if (_stopped) return;
                s = _settings; generation = _generation;
                if (!s.Enabled) { _state = "off"; Monitor.Wait(_sync); continue; }
            }
            using (var client = factory.CreateMqttClient())
            {
                try
                {
                    SetState(generation, "connecting", null, 0);
                    using (var cts = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
                    {
                        cts.CancelAfter(TimeSpan.FromSeconds(15));
                        client.ConnectAsync(Options(s), cts.Token).GetAwaiter().GetResult();
                    }
                    failures = 0;
                    Log.Info("MQTT: connected to " + s.Broker + ".");
                    Publish(client, _availabilityTopic, "online", true);
                    Serve(client, s, generation);
                }
                catch (Exception ex)
                {
                    // Stopping, or new settings while connecting: nothing to tell.
                    if (Current(generation))
                    {
                        failures++;
                        SetState(generation, "error", Problem(ex, s), 0);
                    }
                }
                if (client.IsConnected) Goodbye(client);
            }
            // Again in 5 s, then 10, 30 and every minute.
            if (Current(generation)) Wait(generation, 1000 * RetrySeconds[Math.Min(Math.Max(failures, 1), RetrySeconds.Length) - 1]);
        }
    }

    // Connected: the discovery once (and again when an entity is added or renamed), then the state every 5 s, until the
    // settings change, FanPilot stops or the connection drops.
    void Serve(IMqttClient client, MqttSettings s, int generation)
    {
        var published = new Dictionary<string, string>(); // config topic → payload sent
        var known = new Dictionary<string, Dictionary<string, object>>();  // config topic → config, every entity seen since connecting
        while (Current(generation))
        {
            if (!client.IsConnected) throw new MqttCommunicationLostException();
            var v1 = _readings();
            // An entity once seen stays (a GPU whose driver restarts doesn't come and go in Home Assistant).
            foreach (var e in Entities(v1, s)) known[e.Key] = e.Value;
            foreach (var e in known)
            {
                string payload = Json.Serialize(e.Value);
                string seen;
                if (published.TryGetValue(e.Key, out seen) && seen == payload) continue;
                Publish(client, e.Key, payload, true);
                published[e.Key] = payload;
            }
            Publish(client, _stateTopic, Json.Serialize(v1), false);
            SetState(generation, "connected", null, known.Count);
            Wait(generation, StateSeconds * 1000);
        }
    }

    // The connection dropped under Serve.
    sealed class MqttCommunicationLostException : Exception { }

    void Goodbye(IMqttClient client)
    {
        try
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1)))
            {
                client.PublishAsync(Message(_availabilityTopic, "offline", true), cts.Token).GetAwaiter().GetResult();
                client.DisconnectAsync(new MqttClientDisconnectOptions(), cts.Token).GetAwaiter().GetResult();
            }
        }
        catch (Exception) { } // the last will says it
    }

    MqttClientOptions Options(MqttSettings s)
    {
        var b = new MqttClientOptionsBuilder()
            .WithTcpServer(s.Host, s.Port)
            .WithClientId("fanpilot_" + _node)
            .WithCleanSession(true)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
            .WithTimeout(TimeSpan.FromSeconds(10))
            .WithWillTopic(_availabilityTopic).WithWillPayload("offline").WithWillRetain(true)
            .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce);
        if (s.Username != null) b.WithCredentials(s.Username, s.Password ?? "");
        if (s.Tls) b.WithTlsOptions(o => o.UseTls());
        return b.Build();
    }

    static MqttApplicationMessage Message(string topic, string payload, bool retain)
    {
        // The discovery and the availability are kept by the broker (retain) and sent at least once; the state, every
        // 5 s, at most once.
        return new MqttApplicationMessageBuilder().WithTopic(topic).WithPayload(payload).WithRetainFlag(retain)
            .WithQualityOfServiceLevel(retain ? MqttQualityOfServiceLevel.AtLeastOnce : MqttQualityOfServiceLevel.AtMostOnce).Build();
    }

    void Publish(IMqttClient client, string topic, string payload, bool retain)
    {
        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
        {
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            client.PublishAsync(Message(topic, payload, retain), cts.Token).GetAwaiter().GetResult();
        }
    }

    static Msg Problem(Exception ex, MqttSettings s)
    {
        if (ex is MqttCommunicationLostException || ex is MQTTnet.Exceptions.MqttClientNotConnectedException || ex is MqttClientDisconnectedException)
            return new Msg("mqtt.error.lost", "broker", s.Broker);
        var failed = ex as MqttConnectingFailedException ?? ex.InnerException as MqttConnectingFailedException;
        if (failed != null && (failed.ResultCode == MqttClientConnectResultCode.BadUserNameOrPassword || failed.ResultCode == MqttClientConnectResultCode.NotAuthorized))
            return new Msg("mqtt.error.auth", "broker", s.Broker);
        if (ex is OperationCanceledException) return new Msg("mqtt.error.timeout", "broker", s.Broker);
        var inner = ex;
        while (inner.InnerException != null) inner = inner.InnerException; // the socket's or the TLS' own words
        return new Msg("mqtt.error.connect", "broker", s.Broker, "detail", inner.Message);
    }

    // Home Assistant's discovery: config topic → config, one entity per reading, under one device for this PC. The
    // values come out of the state's JSON (value_template); "None" (no reading) shows as unknown.
    internal IEnumerable<KeyValuePair<string, Dictionary<string, object>>> Entities(Dictionary<string, object> v1, MqttSettings s)
    {
        var board = (Dictionary<string, object>)v1["board"];
        var device = new Dictionary<string, object> {
            { "identifiers", new[] { "fanpilot_" + _node } }, { "name", _pc }, { "manufacturer", "FanPilot" },
            { "model", board["name"] }, { "sw_version", AppVersion.Text }
        };
        Func<string, string, string, string, string, string, string, KeyValuePair<string, Dictionary<string, object>>> sensor =
            (key, name, template, unit, deviceClass, icon, component) =>
            {
                var c = new Dictionary<string, object> {
                    { "name", name }, { "unique_id", "fanpilot_" + _node + "_" + key }, { "state_topic", _stateTopic },
                    { "value_template", template }, { "availability_topic", _availabilityTopic }, { "device", device }
                };
                if (unit != null) c["unit_of_measurement"] = unit;
                if (deviceClass != null) c["device_class"] = deviceClass;
                if (component == "sensor") c["state_class"] = "measurement";
                if (icon != null) c["icon"] = icon;
                return new KeyValuePair<string, Dictionary<string, object>>(s.DiscoveryPrefix + "/" + component + "/fanpilot_" + _node + "_" + key + "/config", c);
            };
        Func<string, object[], string> name = (key, args) => new Msg(key, args).In(s.Language);
        var list = new List<KeyValuePair<string, Dictionary<string, object>>> {
            sensor("cpu_temperature", name("mqtt.entity.cpuTemp", new object[0]), "{{ value_json.cpu.temperature }}", "°C", "temperature", null, "sensor"),
            sensor("cpu_power", name("mqtt.entity.cpuPower", new object[0]), "{{ value_json.cpu.power }}", "W", "power", null, "sensor"),
            sensor("alerts", name("mqtt.entity.alerts", new object[0]), "{{ value_json.alerts | length }}", null, null, "mdi:alert", "sensor"),
            sensor("board_problem", name("mqtt.entity.board", new object[0]), "{{ 'OFF' if value_json.board.ok else 'ON' }}", null, "problem", null, "binary_sensor"),
        };
        if (v1["gpu"] != null)
        {
            list.Add(sensor("gpu_temperature", name("mqtt.entity.gpuTemp", new object[0]), "{{ value_json.gpu.temperature if value_json.gpu else none }}", "°C", "temperature", null, "sensor"));
            list.Add(sensor("gpu_power", name("mqtt.entity.gpuPower", new object[0]), "{{ value_json.gpu.power if value_json.gpu else none }}", "W", "power", null, "sensor"));
            list.Add(sensor("gpu_utilization", name("mqtt.entity.gpuUtil", new object[0]), "{{ value_json.gpu.utilization if value_json.gpu else none }}", "%", null, "mdi:expansion-card", "sensor"));
            list.Add(sensor("gpu_fan", name("mqtt.entity.gpuFan", new object[0]), "{{ value_json.gpu.fan if value_json.gpu else none }}", "%", null, "mdi:fan", "sensor"));
        }
        // By position, like the panel's charts and the history; the name is the user's (item 17) when there is one.
        var fans = ((List<object>)v1["fans"]).Cast<Dictionary<string, object>>().ToList();
        for (int i = 0; i < fans.Count; i++)
        {
            string fan = (string)(fans[i]["label"] ?? fans[i]["name"]);
            list.Add(sensor("fan" + i + "_rpm", name("mqtt.entity.fanRpm", new object[] { "fan", fan }), "{{ value_json.fans[" + i + "].rpm }}", "RPM", null, "mdi:fan", "sensor"));
            list.Add(sensor("fan" + i + "_duty", name("mqtt.entity.fanDuty", new object[] { "fan", fan }), "{{ value_json.fans[" + i + "].duty }}", "%", null, "mdi:speedometer", "sensor"));
        }
        return list;
    }
}
