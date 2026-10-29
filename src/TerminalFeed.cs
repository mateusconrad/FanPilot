// The terminal's data (item 29): what the panel reads, from the same local API, so the console run and --tui (attached
// to the service) go the same way. Over the loopback, the API needs no pairing. Nothing here touches the console:
// TerminalScreen draws a TerminalView, TerminalApp runs the keys and the screen.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;

sealed class TerminalFeed : IDisposable
{
    // 10 minutes of the sparklines, one point a second, as the panel's charts.
    const int HistoryPoints = 600;
    static readonly TimeSpan FansEvery = TimeSpan.FromSeconds(30);

    readonly HttpClient _http;
    readonly HttpClient _stream;
    readonly object _sync = new object();
    readonly CancellationTokenSource _stop = new CancellationTokenSource();
    readonly LinkedList<double?> _cpu = new LinkedList<double?>(), _gpu = new LinkedList<double?>();
    Dictionary<string, object> _info, _fans, _snapshot;
    int _cpuLimit = 95, _gpuLimit = 87;
    DateTime _snapshotAt, _fansAt;
    Thread _thread;

    public readonly string Url;
    // Set when FanPilot doesn't answer (never did, or stopped): what the screen says instead of the numbers.
    public Msg Problem { get; private set; }

    public TerminalFeed(int port)
    {
        // localhost, as the server's own "this PC only" expects (Windows' http.sys turns any other Host away). On Linux,
        // where FanPilot listens on IPv4 alone, the client tries ::1 and then 127.0.0.1.
        Url = "http://localhost:" + port + "/";
        var api = new Uri(Url);
        _http = new HttpClient { BaseAddress = api, Timeout = TimeSpan.FromSeconds(10) };
        _stream = new HttpClient { BaseAddress = api, Timeout = System.Threading.Timeout.InfiniteTimeSpan };
    }

    // Whether FanPilot answers at all: what --tui checks before taking the screen.
    public bool Connect()
    {
        try
        {
            Load();
            return true;
        }
        catch (Exception ex) when (IsDown(ex))
        {
            Problem = new Msg("tui.notRunning", "url", Url);
            return false;
        }
    }

    // The background thread: the stream of snapshots, reconnecting when FanPilot goes away and comes back.
    public void Start()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "terminal-feed" };
        _thread.Start();
    }

    void Run()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                if (_info == null) Load();
                ReadStream();
            }
            catch (Exception ex) when (IsDown(ex) || ex is IOException || ex is FormatException)
            {
                lock (_sync) Problem = new Msg("tui.lost", "url", Url);
            }
            _stop.Token.WaitHandle.WaitOne(2000);
        }
    }

    void Load()
    {
        var info = Get("api/info");
        var fans = Get("api/fans");
        Dictionary<string, object> alerts = null;
        try { alerts = Get("api/alerts/settings"); } catch (HttpRequestException) { } // the tests' servers have no alerts
        Dictionary<string, object> history = null;
        try { history = Get("api/history"); } catch (HttpRequestException) { }
        lock (_sync)
        {
            _info = info;
            _fans = fans;
            _fansAt = DateTime.UtcNow;
            if (alerts != null)
            {
                _cpuLimit = Json.GetInt(alerts, "cpuTemp", _cpuLimit);
                _gpuLimit = Json.GetInt(alerts, "gpuTemp", _gpuLimit);
            }
            if (history != null && _cpu.Count == 0) FillHistory(history);
        }
    }

    void FillHistory(Dictionary<string, object> history)
    {
        var series = Json.Get(history, "series") as Dictionary<string, object>;
        if (series == null) return;
        foreach (var pair in new[] { new { Key = CpuRoles.Main, List = _cpu }, new { Key = "gpu-temp", List = _gpu } })
        {
            object values;
            if (!series.TryGetValue(pair.Key, out values)) continue;
            foreach (object v in Json.ToList(values)) Add(pair.List, v == null ? (double?)null : Json.ToDouble(v));
        }
    }

    static void Add(LinkedList<double?> list, double? v)
    {
        list.AddLast(v);
        while (list.Count > HistoryPoints) list.RemoveFirst();
    }

    void ReadStream()
    {
        using (var response = _stream.GetAsync("api/stream", HttpCompletionOption.ResponseHeadersRead, _stop.Token).GetAwaiter().GetResult())
        {
            response.EnsureSuccessStatusCode();
            using (var reader = new StreamReader(response.Content.ReadAsStreamAsync().GetAwaiter().GetResult(), Encoding.UTF8))
            {
                string line;
                while (!_stop.IsCancellationRequested && (line = reader.ReadLine()) != null)
                {
                    if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;
                    Take(Json.ParseObject(line.Substring("data: ".Length)));
                    if (DateTime.UtcNow - _fansAt > FansEvery) RefreshFans();
                }
            }
        }
        throw new IOException("The stream ended.");
    }

    // A snapshot from the stream (the tests feed them directly).
    internal void Take(Dictionary<string, object> snapshot)
    {
        lock (_sync)
        {
            _snapshot = snapshot;
            _snapshotAt = DateTime.UtcNow;
            Problem = null;
            Add(_cpu, CpuTemp(snapshot));
            Add(_gpu, GpuTemp(snapshot));
        }
    }

    // Names, modes and Turbo change from the panel or the phone: read again every 30 s, and right after the Turbo key.
    void RefreshFans()
    {
        _fansAt = DateTime.UtcNow;
        try
        {
            var fans = Get("api/fans");
            lock (_sync) _fans = fans;
        }
        catch (Exception ex) when (IsDown(ex)) { }
    }

    public void ToggleTurbo()
    {
        bool on;
        lock (_sync) on = _fans != null && Json.Get(_fans, "turbo") is bool b && b;
        Post("api/fans/turbo", new Dictionary<string, object> { { "on", !on } });
        RefreshFans();
    }

    Dictionary<string, object> Get(string path)
    {
        using (var response = _http.GetAsync(path, _stop.Token).GetAwaiter().GetResult())
        {
            response.EnsureSuccessStatusCode();
            return Json.ParseObject(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        }
    }

    void Post(string path, Dictionary<string, object> body)
    {
        using (var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(Json.Serialize(body), Encoding.UTF8, "application/json") })
        {
            request.Headers.Add("X-FanPilot", "1"); // what the server asks of every POST (item 01)
            using (var response = _http.SendAsync(request, _stop.Token).GetAwaiter().GetResult()) response.EnsureSuccessStatusCode();
        }
    }

    // FanPilot not answering: refused, reset, timed out (a TaskCanceledException from HttpClient).
    static bool IsDown(Exception ex)
    {
        return ex is HttpRequestException || ex is System.Threading.Tasks.TaskCanceledException || ex is System.Net.Sockets.SocketException || ex is IOException;
    }

    static double? CpuTemp(Dictionary<string, object> snapshot)
    {
        var sensors = Json.Get(snapshot, "sensors");
        if (sensors == null) return null;
        foreach (object o in Json.ToList(sensors))
        {
            var d = o as Dictionary<string, object>;
            if (d != null && Json.GetString(d, "role", null) == CpuRoles.Main) return Json.ToDouble(Json.Get(d, "value"));
        }
        return null;
    }

    static double? GpuTemp(Dictionary<string, object> snapshot)
    {
        var gpu = Json.Get(snapshot, "gpu") as Dictionary<string, object>;
        return gpu != null && Json.Get(gpu, "ok") is bool ok && ok && Json.Get(gpu, "temp") != null ? Json.ToDouble(Json.Get(gpu, "temp")) : (double?)null;
    }

    // What the screen shows now, in lang.
    public TerminalView View(string lang)
    {
        lock (_sync)
        {
            var v = new TerminalView { Url = Url, Problem = Problem };
            if (_info == null) return v;
            v.Version = Json.GetString(_info, "version", "");
            v.Backend = Json.GetString(_info, "backend", "");
            if (_fans != null)
            {
                string mode = Json.GetString(_fans, "mode", "custom");
                v.Profile = I18n.T(lang, mode == "custom" ? "profiles.custom" : "preset." + mode);
                v.Turbo = Json.Get(_fans, "turbo") is bool turbo && turbo;
            }
            if (_snapshot == null) return v;
            v.Age = DateTime.UtcNow - _snapshotAt;
            var alerts = Json.Get(_snapshot, "alerts");
            v.Alerts = alerts == null ? new List<string>() : Json.ToList(alerts).OfType<Dictionary<string, object>>()
                .Select(a => Text(Json.Get(a, "title") as Dictionary<string, object>, lang) ?? Json.GetString(a, "id", "")).ToList();

            v.Temps.Add(new TerminalView.Temp { Title = CpuTitle(lang), Value = CpuTemp(_snapshot), Limit = _cpuLimit, History = _cpu.ToList() });
            var gpu = Json.Get(_snapshot, "gpu") as Dictionary<string, object>;
            bool gpuOk = gpu != null && Json.Get(gpu, "ok") is bool ok && ok;
            if (gpuOk) v.Temps.Add(new TerminalView.Temp { Title = "GPU", Value = GpuTemp(_snapshot), Limit = _gpuLimit, History = _gpu.ToList() });
            AddBoardTemps(v);
            AddFans(v, lang);
            if (gpuOk && Json.Get(gpu, "fans") != null)
            {
                var percents = Json.ToList(Json.Get(gpu, "fans"));
                if (percents.Count > 0)
                    v.Fans.Add(new TerminalView.Fan {
                        Title = "GPU", Duty = Json.ToDouble(percents[0]),
                        Mode = I18n.T(lang, Json.GetString(gpu, "fanMode", "auto") == "fixed" ? "tui.mode.fixed" : "tui.mode.auto") });
            }
            return v;
        }
    }

    string CpuTitle(string lang)
    {
        foreach (object o in Json.ToList(Json.Get(_snapshot, "sensors") ?? new object[0]))
        {
            var d = o as Dictionary<string, object>;
            if (d == null || Json.GetString(d, "role", null) != CpuRoles.Main) continue;
            string key = Json.GetString(d, "titleKey", null);
            return key != null ? I18n.T(lang, key) : Json.GetString(d, "title", "CPU");
        }
        return "CPU";
    }

    // The board's own sensors that some curve follows, besides the CPU (already on top).
    void AddBoardTemps(TerminalView v)
    {
        if (_fans == null) return;
        var titles = Json.Get(_fans, "tempInputs") == null ? new List<object>() : Json.ToList(Json.Get(_fans, "tempInputs"));
        var values = Json.Get(_snapshot, "tempInputs") == null ? new List<object>() : Json.ToList(Json.Get(_snapshot, "tempInputs"));
        var followed = Json.ToList(Json.Get(_fans, "fans") ?? new object[0]).OfType<Dictionary<string, object>>()
            .Where(f => Json.GetString(f, "mode", "") != FanCurve.BiosMode && Json.GetString(f, "source", null) != "gpu")
            .Select(f => Json.GetInt(f, "tempInput", -1)).Distinct().OrderBy(i => i);
        foreach (int i in followed)
        {
            if (i < 0 || i >= titles.Count) continue;
            string title = Convert.ToString(titles[i]);
            if (title == "CPU") continue;
            double? value = i < values.Count && values[i] != null ? Json.ToDouble(values[i]) : (double?)null;
            v.Temps.Add(new TerminalView.Temp { Title = title, Value = value, Limit = _cpuLimit });
        }
    }

    void AddFans(TerminalView v, string lang)
    {
        if (_fans == null) return;
        var rpms = Json.Get(_snapshot, "fanRpm") == null ? new List<object>() : Json.ToList(Json.Get(_snapshot, "fanRpm"));
        var duties = Json.Get(_snapshot, "fanDuty") == null ? new List<object>() : Json.ToList(Json.Get(_snapshot, "fanDuty"));
        bool estimated = Json.Get(_snapshot, "fanDutyEstimated") is bool e && e;
        var fans = Json.ToList(Json.Get(_fans, "fans") ?? new object[0]).OfType<Dictionary<string, object>>().ToList();
        for (int i = 0; i < fans.Count; i++)
        {
            var f = fans[i];
            string label = Json.GetString(f, "label", null);
            v.Fans.Add(new TerminalView.Fan {
                Title = string.IsNullOrEmpty(label) ? Json.GetString(f, "title", "Fan " + (i + 1)) : label,
                Rpm = i < rpms.Count && rpms[i] != null ? (int?)Json.GetInt(new Dictionary<string, object> { { "v", rpms[i] } }, "v", 0) : null,
                Duty = i < duties.Count && duties[i] != null ? Json.ToDouble(duties[i]) : (double?)null,
                Estimated = estimated,
                Mode = I18n.T(lang, ModeKey(f, v.Turbo)) });
        }
    }

    static string ModeKey(Dictionary<string, object> fan, bool turbo)
    {
        if (Json.GetString(fan, "role", null) == "pump") return "tui.mode.pump";
        if (turbo) return "tui.mode.turbo";
        var request = Json.Get(fan, "request") as Dictionary<string, object>;
        if (request != null) return Json.GetString(request, "mode", "") == "fixed" ? "tui.mode.fixed" : "tui.mode.curve";
        return Json.GetString(fan, "mode", "") == FanCurve.BiosMode ? "tui.mode.bios" : "tui.mode.chip";
    }

    static string Text(Dictionary<string, object> msg, string lang)
    {
        if (msg == null) return null;
        string code = Json.GetString(msg, "code", null);
        return code != null && I18n.Has(code) ? I18n.T(lang, code, Json.Get(msg, "params") as Dictionary<string, object>) : Json.GetString(msg, "text", null);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _http.Dispose();
        _stream.Dispose();
    }
}

// One screen of the terminal: what TerminalScreen draws.
sealed class TerminalView
{
    public sealed class Temp
    {
        public string Title;
        public double? Value;
        public int Limit;
        public List<double?> History = new List<double?>();
    }

    public sealed class Fan
    {
        public string Title;
        public int? Rpm;
        public double? Duty;
        public bool Estimated;
        public string Mode;
    }

    public string Url, Version = "", Backend = "", Profile = "";
    public bool Turbo;
    public Msg Problem;
    public TimeSpan? Age;
    public List<string> Alerts = new List<string>();
    public List<Temp> Temps = new List<Temp>();
    public List<Fan> Fans = new List<Fan>();
    // Set by TerminalApp: q closes this FanPilot (it runs in this window), and l shows its log.
    public bool OwnsFanPilot, HasLog;
}
