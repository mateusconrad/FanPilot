using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

sealed class WebServer
{
    readonly AppConfig _cfg;
    readonly string _root;
    // Set once by Attach: the listener starts before the board is opened, and until then every request gets 503.
    AuthManager _auth;
    Sampler _sampler;
    FanManager _fans;
    GpuMonitor _gpu;
    AlertMonitor _alerts;
    HistoryStore _history; // null: historyDays is 0
    IntegrationTokens _tokens; // null in the tests that don't need them: no token is then valid
    IntegrationFeed _feed;
    MqttPublisher _mqtt;       // null in the tests that don't need it: /api/mqtt answers 404
    volatile bool _ready;
    readonly HostGuard _hosts;
    readonly SseHub _hub = new SseHub();
    readonly HttpListener _listener = new HttpListener();
    // "+" = every address, so phones on the LAN reach it (needs Administrator on Windows). Tests and FanPilot without
    // Administrator use "localhost": this PC only.
    internal string ListenHost = "+";
    // What /api/log sends: the last few thousand lines, not the 5 MB a full log reaches.
    const int LogTailBytes = 256 * 1024;

    static readonly Dictionary<string, string> ContentTypes = new Dictionary<string, string> {
        { ".html", "text/html; charset=utf-8" }, { ".js", "text/javascript; charset=utf-8" }, { ".mjs", "text/javascript; charset=utf-8" },
        { ".css", "text/css; charset=utf-8" }, { ".svg", "image/svg+xml" }, { ".png", "image/png" },
        { ".json", "application/json; charset=utf-8" }, { ".webmanifest", "application/manifest+json" },
        { ".mp4", "video/mp4" } // the panel mode's keep-the-screen-on video (item 22)
    };

    public WebServer(AppConfig cfg, string root)
    {
        _cfg = cfg; _root = root;
        _hosts = new HostGuard(cfg.Port, cfg.PublicHost);
    }

    // alerts: null where nobody watches (the tests that do not need them); the alert routes then answer 404.
    public void Attach(AuthManager auth, Sampler sampler, FanManager fans, GpuMonitor gpu, AlertMonitor alerts = null, HistoryStore history = null,
                       IntegrationTokens tokens = null, IntegrationFeed feed = null, MqttPublisher mqtt = null)
    {
        _auth = auth; _sampler = sampler; _fans = fans; _gpu = gpu; _alerts = alerts; _history = history;
        _tokens = tokens; _mqtt = mqtt;
        _feed = feed ?? new IntegrationFeed(sampler, new FanListCache(fans), () => fans.BackendName);
        // The sampler only hands the snapshot over: the hub writes to each client on its own.
        sampler.Subscribe(json => _hub.Publish(Frame(json)));
        _ready = true; // volatile: the fields above are visible to whoever sees this
    }

    // Start() fails like this when the port is taken; FanPilot shows it before touching any fan.
    public static Msg PortProblem(int port, HttpListenerException ex)
    {
        return new Msg("console.portBusy", "port", port, "detail", ex.Message);
    }

    public void Start()
    {
        _listener.Prefixes.Add("http://" + (ListenHost == "localhost" ? Platform.LocalhostPrefixHost : ListenHost) + ":" + _cfg.Port + "/");
        _listener.Start();
        new Thread(AcceptLoop) { IsBackground = true, Name = "http" }.Start();
    }

    public void Stop()
    {
        _hub.CloseAll();
        try { _listener.Stop(); } catch { }
    }

    void AcceptLoop()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = _listener.GetContext(); } catch { break; }
            ThreadPool.QueueUserWorkItem(_ => Handle(ctx));
        }
    }

    sealed class Req
    {
        public HttpListenerContext Ctx;
        public string Path;
        public string Method;
        public bool Loopback;
        public Session Session;
        public string Ip;
        // An integration's (item 23): it reads /metrics and /api/v1/sensors, nothing else. BadToken: one was sent, but
        // it is unknown or revoked.
        public IntegrationToken Token;
        public bool BadToken;
        public bool Authenticated { get { return Loopback || Session != null; } }
    }

    void Handle(HttpListenerContext ctx)
    {
        var h = ctx.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        h["X-Frame-Options"] = "DENY";
        h["Referrer-Policy"] = "no-referrer";
        h["Content-Security-Policy"] = "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self'; connect-src 'self'; frame-ancestors 'none'";

        // "localhost": this PC only, also where the listener had to take every address (Platform.LocalhostPrefixHost).
        // A plain 403: .NET's own HttpListener (Linux) can't drop a connection without answering it.
        if (ListenHost == "localhost" && !IPAddress.IsLoopback(ctx.Request.RemoteEndPoint.Address))
        {
            try
            {
                ctx.Response.StatusCode = 403;
                ctx.Response.Close();
            }
            catch { }
            return;
        }

        // Before anything else, static files included: a request for some other host is never served.
        var url = ctx.Request.Url;
        if (!_hosts.IsAllowed(url))
        {
            _hosts.LogRejected(url.Authority);
            try
            {
                ctx.Response.StatusDescription = "Misdirected Request"; // .NET Framework has no reason phrase for 421
                Text(ctx, 421, "Misdirected Request");
                ctx.Response.Close();
            }
            catch { }
            return;
        }
        if (!_ready)
        {
            try
            {
                ctx.Response.Headers["Retry-After"] = "2";
                if (url.AbsolutePath.StartsWith("/api/")) WriteJson(ctx, 503, Error("server.starting"));
                else Text(ctx, 503, I18n.T(LanguageOf(ctx), "server.starting"));
                ctx.Response.Close();
            }
            catch { }
            return;
        }

        var r = new Req
        {
            Ctx = ctx,
            Path = url.AbsolutePath,
            Method = ctx.Request.HttpMethod,
            Ip = ctx.Request.RemoteEndPoint.Address.ToString(),
            Loopback = IPAddress.IsLoopback(ctx.Request.RemoteEndPoint.Address) && HostGuard.IsLoopbackName(url)
        };
        var cookie = ctx.Request.Cookies[AuthManager.CookieName];
        if (cookie != null) r.Session = _auth.Validate(cookie.Value);
        string bearer = IntegrationTokens.FromHeader(ctx.Request.Headers["Authorization"]);
        if (bearer != null)
        {
            r.Token = _tokens == null ? null : _tokens.Validate(bearer);
            r.BadToken = r.Token == null;
        }

        try
        {
            if (!Route(r)) return; // response kept open (SSE)
        }
        catch (ArgumentException ex) { SafeJson(ctx, 400, Error(ex)); }
        catch (FormatException ex) { SafeJson(ctx, 400, Error("err.badRequest", "detail", ex.Message)); }
        catch (InvalidOperationException ex) { SafeJson(ctx, 503, Error(ex)); }
        catch (Exception ex) { Log.Warn(r.Method + " " + r.Path + ": " + ex); SafeJson(ctx, 500, Error("err.internal")); }
        try { ctx.Response.Close(); } catch { }
    }

    // {"error": English text, "code": key, "params": {…}}: the panel translates by the code (item 21).
    static Dictionary<string, object> Error(Msg m)
    {
        var d = new Dictionary<string, object> { { "error", m.Text }, { "code", m.Key } };
        object args;
        if (m.ToJson().TryGetValue("params", out args)) d["params"] = args;
        return d;
    }

    static Dictionary<string, object> Error(string key, params object[] nameValuePairs) { return Error(new Msg(key, nameValuePairs)); }

    // Ours carry their message; anything else (.NET's own) only has its text.
    static Dictionary<string, object> Error(Exception ex)
    {
        var m = Msg.Of(ex);
        return m != null ? Error(m) : new Dictionary<string, object> { { "error", ex.Message } };
    }

    // The pages the server writes itself (text, not the panel) follow the browser's language.
    static string LanguageOf(HttpListenerContext ctx) { return I18n.FromAcceptLanguage(ctx.Request.Headers["Accept-Language"]); }

    // Returns false when the handler keeps the response open.
    bool Route(Req r)
    {
        string p = r.Path;
        if (r.Method == "GET")
        {
            if (p == "/auth") { Redeem(r); return true; }
            if (p == "/pair" || p == "/pair.html")
            {
                if (!r.Loopback) { Text(r.Ctx, 403, I18n.T(LanguageOf(r.Ctx), "server.pairLocalOnly")); return true; }
                return ServeFile(r.Ctx, "pair.html");
            }
            if (p == "/" || p == "/index.html") return ServeFile(r.Ctx, r.Authenticated ? "index.html" : "locked.html");
            // Panel mode (item 22): read only, behind the same pairing as the panel.
            if (p == "/panel" || p == "/painel" || p == "/panel.html") return ServeFile(r.Ctx, r.Authenticated ? "panel.html" : "locked.html");
            if (p == "/metrics" || p == "/api/v1/sensors") { Integration(r); return true; }
            if (!p.StartsWith("/api/")) return ServeFile(r.Ctx, p.TrimStart('/'));
        }

        if (!p.StartsWith("/api/")) { Text(r.Ctx, 404, "Not found"); return true; }
        if (p.StartsWith("/api/pair/"))
        {
            if (!r.Loopback) { WriteJson(r.Ctx, 403, Error("err.localOnly")); return true; }
            return PairApi(r);
        }
        if (!r.Authenticated && r.Token != null) { WriteJson(r.Ctx, 403, Error("err.token.scope")); return true; }
        if (!r.Authenticated) { WriteJson(r.Ctx, 401, Error("err.unpaired")); return true; }
        if (r.Method == "POST" && !SameOriginPost(r)) { WriteJson(r.Ctx, 403, Error("err.origin")); return true; }

        if (r.Method == "GET" && p == "/api/state") { RawJson(r.Ctx, 200, _sampler.LatestJson); return true; }
        if (r.Method == "GET" && p == "/api/stream") return Stream(r);
        if (r.Method == "GET" && p == "/api/history") { WriteJson(r.Ctx, 200, History(r.Ctx.Request.QueryString["range"])); return true; }
        if (r.Method == "GET" && p == "/api/fans") { WriteJson(r.Ctx, 200, _fans.Describe()); return true; }
        if (r.Method == "GET" && p == "/api/info")
        {
            WriteJson(r.Ctx, 200, new Dictionary<string, object> {
                { "version", AppVersion.Text }, { "backend", _fans.BackendName }, { "board", MachineData.Board },
                // false with "historyDays": 0: the panel then offers only its 10 minutes.
                { "history", _history != null } });
            return true;
        }
        // The end of fanpilot.log, for the tray's "Ver log" (item 14b). This PC only, like the pairing: the log names
        // the devices and addresses that reached the server.
        if (r.Method == "GET" && p == "/api/log")
        {
            if (!r.Loopback) { WriteJson(r.Ctx, 403, Error("err.localOnly")); return true; }
            r.Ctx.Response.Headers["Cache-Control"] = "no-store";
            Text(r.Ctx, 200, Log.Tail(LogTailBytes));
            return true;
        }
        if (p.StartsWith("/api/alerts") && _alerts != null)
        {
            if (r.Method == "GET" && p == "/api/alerts") { WriteJson(r.Ctx, 200, _alerts.Describe()); return true; }
            if (r.Method == "GET" && p == "/api/alerts/settings") { WriteJson(r.Ctx, 200, _alerts.Settings()); return true; }
            if (r.Method == "POST" && p == "/api/alerts/settings") { WriteJson(r.Ctx, 200, _alerts.UpdateSettings(ReadBody(r))); return true; }
            if (r.Method == "POST" && p == "/api/alerts/test") { WriteJson(r.Ctx, 200, _alerts.SendTest()); return true; }
            if (r.Method == "POST" && p == "/api/alerts/ack")
            {
                bool found = _alerts.Acknowledge(Json.GetString(ReadBody(r), "id", ""));
                WriteJson(r.Ctx, found ? 200 : 404, found ? new Dictionary<string, object> { { "ok", true } } : Error("err.alertNotFound"));
                return true;
            }
        }
        // Home Assistant over MQTT (item 23): the settings, and how the connection is going.
        if (p == "/api/mqtt" && _mqtt != null)
        {
            if (r.Method == "GET") { WriteJson(r.Ctx, 200, _mqtt.Describe()); return true; }
            if (r.Method == "POST") { WriteJson(r.Ctx, 200, _mqtt.Update(ReadBody(r))); return true; }
        }
        if (r.Method == "POST" && p == "/api/logout")
        {
            var c = r.Ctx.Request.Cookies[AuthManager.CookieName];
            if (c != null) _auth.Revoke(c.Value);
            r.Ctx.Response.Headers.Add("Set-Cookie", AuthManager.CookieName + "=; Path=/; Max-Age=0; HttpOnly; SameSite=Strict");
            WriteJson(r.Ctx, 200, new Dictionary<string, object> { { "ok", true } });
            return true;
        }
        if (r.Method == "POST" && p == "/api/fans/turbo")
        {
            var body = ReadBody(r);
            _fans.SetTurbo(Convert.ToBoolean(Json.Get(body, "on")));
            WriteJson(r.Ctx, 200, new Dictionary<string, object> { { "turbo", _fans.TurboActive } });
            return true;
        }
        if (r.Method == "POST" && p == "/api/fans/mode")
        {
            _fans.SetMode(Json.GetString(ReadBody(r), "mode", ""));
            WriteJson(r.Ctx, 200, new Dictionary<string, object> { { "mode", _fans.Mode }, { "turbo", _fans.TurboActive } });
            return true;
        }
        if (r.Method == "POST" && p == "/api/fans/orphans")
        {
            _fans.DiscardOrphan(Json.GetString(ReadBody(r), "key", ""));
            WriteJson(r.Ctx, 200, new Dictionary<string, object> { { "ok", true } });
            return true;
        }
        if (r.Method == "POST" && p.StartsWith("/api/fans/"))
        {
            var parts = p.Substring("/api/fans/".Length).Split('/');
            int index;
            if (!int.TryParse(parts[0], out index)) throw new UserError("err.fanIndex");
            if (parts.Length > 1 && parts[1] == "meta") { WriteJson(r.Ctx, 200, _fans.SetMeta(index, ReadBody(r))); return true; }
            FanCurve result = parts.Length > 1 && parts[1] == "restore" ? _fans.Restore(index) : _fans.Apply(index, ReadBody(r));
            WriteJson(r.Ctx, 200, result.ToDictionary());
            return true;
        }
        if (r.Method == "POST" && p == "/api/gpu/fan")
        {
            var body = ReadBody(r);
            if (Json.GetString(body, "mode", "auto") == "fixed") _gpu.SetFixed(Json.GetInt(body, "percent", 50));
            else _gpu.SetAuto();
            Log.Info("GPU fan <- " + Json.Serialize(body));
            WriteJson(r.Ctx, 200, _gpu.Read());
            return true;
        }
        WriteJson(r.Ctx, 404, Error("err.route"));
        return true;
    }

    // Custom header forces a CORS preflight (which we never grant). Origin, when present, must be this same server
    // under one of the PC's own names, not merely equal to the Host header.
    bool SameOriginPost(Req r)
    {
        if (r.Ctx.Request.Headers["X-FanPilot"] != "1") return false;
        string origin = r.Ctx.Request.Headers["Origin"];
        if (string.IsNullOrEmpty(origin)) return true;
        Uri o;
        return Uri.TryCreate(origin, UriKind.Absolute, out o) && _hosts.IsAllowed(o) &&
               string.Equals(o.Authority, r.Ctx.Request.Url.Authority, StringComparison.OrdinalIgnoreCase);
    }

    void Redeem(Req r)
    {
        string lang = LanguageOf(r.Ctx);
        if (_auth.IsBlocked(r.Ip)) { Text(r.Ctx, 429, I18n.T(lang, "server.tooManyAttempts")); return; }
        string token = _auth.Redeem(r.Ctx.Request.QueryString["c"], r.Ip, r.Ctx.Request.UserAgent);
        if (token == null)
        {
            Html(r.Ctx, 403, lang, I18n.T(lang, "server.codeInvalid"));
            return;
        }
        int maxAge = _cfg.SessionDays * 86400;
        r.Ctx.Response.Headers.Add("Set-Cookie", AuthManager.CookieName + "=" + token + "; Path=/; Max-Age=" + maxAge + "; HttpOnly; SameSite=Strict");
        // Not a 302: after a cross-site navigation (camera app) browsers would omit the Strict cookie on the
        // redirected request. A same-origin refresh is a same-site navigation, so the cookie is sent.
        Html(r.Ctx, 200, lang, I18n.T(lang, "server.paired"), "<meta http-equiv=refresh content='1;url=/'>");
    }

    // Prometheus' /metrics and the stable JSON (item 23): for the PC, a paired device or an integration token. A token
    // that was sent and doesn't hold gets 401 even here, so a revoked one shows up as such in the integration.
    void Integration(Req r)
    {
        if (r.BadToken || (!r.Authenticated && r.Token == null))
        {
            // A restricted header for Headers[…]: AddHeader sets it all the same.
            r.Ctx.Response.AddHeader("WWW-Authenticate", "Bearer realm=\"FanPilot\"");
            WriteJson(r.Ctx, 401, Error(r.BadToken ? "err.token.invalid" : "err.unpaired"));
            return;
        }
        if (r.Path == "/api/v1/sensors") { WriteJson(r.Ctx, 200, _feed.SensorsV1()); return; }
        var bytes = Encoding.UTF8.GetBytes(_feed.Metrics());
        r.Ctx.Response.StatusCode = 200;
        r.Ctx.Response.ContentType = "text/plain; version=0.0.4; charset=utf-8";
        r.Ctx.Response.Headers["Cache-Control"] = "no-store";
        r.Ctx.Response.ContentLength64 = bytes.Length;
        r.Ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
    }

    bool PairApi(Req r)
    {
        if (r.Method == "POST" && !SameOriginPost(r)) { WriteJson(r.Ctx, 403, Error("err.origin")); return true; }
        if (r.Method == "GET" && r.Path == "/api/pair/info")
        {
            string code;
            DateTime expires;
            _auth.GetPairCode(out code, out expires);
            var urls = NetworkInfo.CandidateHosts(_cfg).Select(h => "http://" + h + ":" + _cfg.Port + "/auth?c=" + code).ToList();
            WriteJson(r.Ctx, 200, new Dictionary<string, object> {
                { "urls", urls }, { "expiresIn", Math.Max(0, (int)(expires - DateTime.UtcNow).TotalSeconds) },
                { "sessions", _auth.List() }, { "port", _cfg.Port },
                { "tokens", _tokens == null ? new List<Dictionary<string, object>>() : _tokens.List() }
            });
            return true;
        }
        if (r.Method == "POST" && r.Path == "/api/pair/revoke")
        {
            var body = ReadBody(r);
            string id = Json.GetString(body, "id", null);
            if (id == "*") _auth.RevokeAll(); else _auth.RevokeById(id);
            WriteJson(r.Ctx, 200, new Dictionary<string, object> { { "ok", true } });
            return true;
        }
        // Integration tokens (item 23): made and revoked on the PC only, like the pairing.
        if (r.Method == "POST" && r.Path == "/api/pair/tokens" && _tokens != null)
        {
            WriteJson(r.Ctx, 200, _tokens.Create(Json.GetString(ReadBody(r), "name", "")));
            return true;
        }
        if (r.Method == "POST" && r.Path == "/api/pair/tokens/revoke" && _tokens != null)
        {
            bool found = _tokens.Revoke(Json.GetString(ReadBody(r), "id", ""));
            WriteJson(r.Ctx, found ? 200 : 404, found ? new Dictionary<string, object> { { "ok", true } } : Error("err.token.notFound"));
            return true;
        }
        WriteJson(r.Ctx, 404, Error("err.route"));
        return true;
    }

    // ?range=10m (the default: every second, from memory), 1h, 24h or 7d (the rows on disk, item 24).
    Dictionary<string, object> History(string range)
    {
        TimeSpan span;
        if (string.IsNullOrEmpty(range) || range == "10m") return CompactHistory();
        if (!HistoryStore.Ranges.TryGetValue(range, out span)) throw new UserError("err.historyRange");
        if (_history == null) throw new Unavailable("err.historyOff");
        return _history.Read(span);
    }

    Dictionary<string, object> CompactHistory()
    {
        var snaps = _sampler.History();
        var t = new List<long>();
        var series = new Dictionary<string, List<object>>();
        int i = 0;
        foreach (var s in snaps)
        {
            t.Add(Convert.ToInt64(s["t"]));
            object sensors;
            if (s.TryGetValue("sensors", out sensors))
                foreach (var d in (List<Dictionary<string, object>>)sensors)
                {
                    if ((string)d["type"] == "Temperature" || (string)d["type"] == "Power") Put(series, (string)d["id"], i, d["value"]);
                    // The CPU chart's series, whichever reading it is on this board (item 19).
                    if (Json.GetString(d, "role", null) == CpuRoles.Main) Put(series, CpuRoles.Main, i, d["value"]);
                }
            var gpu = (Dictionary<string, object>)s["gpu"];
            object v;
            if (gpu.TryGetValue("temp", out v)) Put(series, "gpu-temp", i, v);
            if (gpu.TryGetValue("power", out v)) Put(series, "gpu-power", i, v);
            object rpm;
            if (s.TryGetValue("fanRpm", out rpm))
            {
                var list = (List<int>)rpm;
                for (int k = 0; k < list.Count; k++) Put(series, "fan-" + k, i, list[k]);
            }
            i++;
        }
        foreach (var kv in series) while (kv.Value.Count < i) kv.Value.Add(null);
        return new Dictionary<string, object> { { "t", t }, { "series", series } };
    }

    static void Put(Dictionary<string, List<object>> series, string key, int index, object value)
    {
        List<object> list;
        if (!series.TryGetValue(key, out list)) series[key] = list = new List<object>();
        while (list.Count < index) list.Add(null);
        list.Add(value);
    }

    // Returns false (response kept open) once the client joined the stream; true after a 503 for a full hub.
    bool Stream(Req r)
    {
        var resp = r.Ctx.Response;
        resp.StatusCode = 200;
        resp.ContentType = "text/event-stream; charset=utf-8";
        resp.Headers["Cache-Control"] = "no-store";
        resp.SendChunked = true;
        var output = resp.OutputStream;
        if (_hub.Add(bytes => output.WriteAsync(bytes, 0, bytes.Length), () => resp.Abort(), Frame(_sampler.LatestJson))) return false;
        // Nothing has been written yet, so the status can still change.
        resp.SendChunked = false;
        resp.Headers["Retry-After"] = "10";
        WriteJson(r.Ctx, 503, Error("err.tooManyStreams"));
        return true;
    }

    static byte[] Frame(string json) { return Encoding.UTF8.GetBytes("data: " + json + "\n\n"); }

    Dictionary<string, object> ReadBody(Req r)
    {
        if (r.Ctx.Request.ContentLength64 > 64 * 1024) throw new UserError("err.bodyTooLarge");
        using (var reader = new StreamReader(r.Ctx.Request.InputStream, Encoding.UTF8))
            return Json.ParseObject(reader.ReadToEnd());
    }

    bool ServeFile(HttpListenerContext ctx, string relative)
    {
        string full = Path.GetFullPath(Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(Path.GetFullPath(_root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
        {
            Text(ctx, 404, "Not found");
            return true;
        }
        string type;
        if (!ContentTypes.TryGetValue(Path.GetExtension(full).ToLowerInvariant(), out type)) type = "application/octet-stream";
        var bytes = File.ReadAllBytes(full);
        ctx.Response.ContentType = type;
        ctx.Response.Headers["Cache-Control"] = "no-cache";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        return true;
    }

    static void WriteJson(HttpListenerContext ctx, int status, object o) { RawJson(ctx, status, Json.Serialize(o)); }

    static void RawJson(HttpListenerContext ctx, int status, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        ctx.Response.Headers["Cache-Control"] = "no-store";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
    }

    static void SafeJson(HttpListenerContext ctx, int status, object o) { try { WriteJson(ctx, status, o); } catch { } }

    static void Text(HttpListenerContext ctx, int status, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "text/plain; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
    }

    static void Html(HttpListenerContext ctx, int status, string lang, string message, string head = "")
    {
        string html = "<!doctype html><html lang=" + lang + "><meta charset=utf-8><meta name=viewport content='width=device-width,initial-scale=1'>" + head +
            "<link rel=stylesheet href=/style.css><main class=locked><h1>FanPilot</h1><p>" + WebUtility.HtmlEncode(message) + "</p></main>";
        var bytes = Encoding.UTF8.GetBytes(html);
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
    }
}
