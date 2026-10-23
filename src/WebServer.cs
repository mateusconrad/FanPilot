using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

sealed class WebServer
{
    readonly AppConfig _cfg;
    readonly string _root;
    readonly AuthManager _auth;
    readonly Sampler _sampler;
    readonly FanManager _fans;
    readonly GpuMonitor _gpu;
    readonly HttpListener _listener = new HttpListener();

    static readonly Dictionary<string, string> ContentTypes = new Dictionary<string, string> {
        { ".html", "text/html; charset=utf-8" }, { ".js", "text/javascript; charset=utf-8" },
        { ".css", "text/css; charset=utf-8" }, { ".svg", "image/svg+xml" }, { ".png", "image/png" },
        { ".json", "application/json; charset=utf-8" }, { ".webmanifest", "application/manifest+json" }
    };

    public WebServer(AppConfig cfg, string root, AuthManager auth, Sampler sampler, FanManager fans, GpuMonitor gpu)
    {
        _cfg = cfg; _root = root; _auth = auth; _sampler = sampler; _fans = fans; _gpu = gpu;
    }

    public void Start()
    {
        _listener.Prefixes.Add("http://+:" + _cfg.Port + "/");
        _listener.Start();
        new Thread(AcceptLoop) { IsBackground = true, Name = "http" }.Start();
    }

    public void Stop() { try { _listener.Stop(); } catch { } }

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
        public bool Authenticated { get { return Loopback || Session != null; } }
    }

    void Handle(HttpListenerContext ctx)
    {
        var r = new Req
        {
            Ctx = ctx,
            Path = ctx.Request.Url.AbsolutePath,
            Method = ctx.Request.HttpMethod,
            Ip = ctx.Request.RemoteEndPoint.Address.ToString(),
            Loopback = IPAddress.IsLoopback(ctx.Request.RemoteEndPoint.Address)
        };
        var cookie = ctx.Request.Cookies[AuthManager.CookieName];
        if (cookie != null) r.Session = _auth.Validate(cookie.Value);

        var h = ctx.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        h["X-Frame-Options"] = "DENY";
        h["Referrer-Policy"] = "no-referrer";
        h["Content-Security-Policy"] = "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self'; connect-src 'self'; frame-ancestors 'none'";
        try
        {
            if (!Route(r)) return; // response kept open (SSE)
        }
        catch (ArgumentException ex) { SafeJson(ctx, 400, Error(ex.Message)); }
        catch (FormatException ex) { SafeJson(ctx, 400, Error("Requisição inválida: " + ex.Message)); }
        catch (InvalidOperationException ex) { SafeJson(ctx, 503, Error(ex.Message)); }
        catch (Exception ex) { Log.Warn(r.Method + " " + r.Path + ": " + ex); SafeJson(ctx, 500, Error("Erro interno.")); }
        try { ctx.Response.Close(); } catch { }
    }

    static Dictionary<string, object> Error(string msg) { return new Dictionary<string, object> { { "error", msg } }; }

    // Returns false when the handler keeps the response open.
    bool Route(Req r)
    {
        string p = r.Path;
        if (r.Method == "GET")
        {
            if (p == "/auth") { Redeem(r); return true; }
            if (p == "/pair" || p == "/pair.html")
            {
                if (!r.Loopback) { Text(r.Ctx, 403, "O pareamento só pode ser aberto no próprio PC."); return true; }
                return ServeFile(r.Ctx, "pair.html");
            }
            if (p == "/" || p == "/index.html") return ServeFile(r.Ctx, r.Authenticated ? "index.html" : "locked.html");
            if (!p.StartsWith("/api/")) return ServeFile(r.Ctx, p.TrimStart('/'));
        }

        if (!p.StartsWith("/api/")) { Text(r.Ctx, 404, "Not found"); return true; }
        if (p.StartsWith("/api/pair/"))
        {
            if (!r.Loopback) { WriteJson(r.Ctx, 403, Error("Somente no PC.")); return true; }
            return PairApi(r);
        }
        if (!r.Authenticated) { WriteJson(r.Ctx, 401, Error("Não pareado.")); return true; }
        if (r.Method == "POST" && !SameOriginPost(r)) { WriteJson(r.Ctx, 403, Error("Origem inválida.")); return true; }

        if (r.Method == "GET" && p == "/api/state") { RawJson(r.Ctx, 200, _sampler.LatestJson); return true; }
        if (r.Method == "GET" && p == "/api/stream") { Stream(r); return false; }
        if (r.Method == "GET" && p == "/api/history") { WriteJson(r.Ctx, 200, CompactHistory()); return true; }
        if (r.Method == "GET" && p == "/api/fans") { WriteJson(r.Ctx, 200, _fans.Describe()); return true; }
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
        if (r.Method == "POST" && p.StartsWith("/api/fans/"))
        {
            var parts = p.Substring("/api/fans/".Length).Split('/');
            int index;
            if (!int.TryParse(parts[0], out index)) throw new ArgumentException("Fan inválido.");
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
        WriteJson(r.Ctx, 404, Error("Rota inexistente."));
        return true;
    }

    // Custom header forces a CORS preflight (which we never grant), and Origin must match when present.
    static bool SameOriginPost(Req r)
    {
        if (r.Ctx.Request.Headers["X-FanPilot"] != "1") return false;
        string origin = r.Ctx.Request.Headers["Origin"];
        if (string.IsNullOrEmpty(origin)) return true;
        Uri o;
        return Uri.TryCreate(origin, UriKind.Absolute, out o) && string.Equals(o.Authority, r.Ctx.Request.Url.Authority, StringComparison.OrdinalIgnoreCase);
    }

    void Redeem(Req r)
    {
        if (_auth.IsBlocked(r.Ip)) { Text(r.Ctx, 429, "Muitas tentativas. Aguarde um minuto."); return; }
        string token = _auth.Redeem(r.Ctx.Request.QueryString["c"], r.Ip, r.Ctx.Request.UserAgent);
        if (token == null)
        {
            Html(r.Ctx, 403, "Código inválido ou expirado. Escaneie o QR code novamente.");
            return;
        }
        int maxAge = _cfg.SessionDays * 86400;
        r.Ctx.Response.Headers.Add("Set-Cookie", AuthManager.CookieName + "=" + token + "; Path=/; Max-Age=" + maxAge + "; HttpOnly; SameSite=Strict");
        // Not a 302: after a cross-site navigation (camera app) browsers would omit the Strict cookie on the
        // redirected request. A same-origin refresh is a same-site navigation, so the cookie is sent.
        Html(r.Ctx, 200, "Pareado! Abrindo o painel…", "<meta http-equiv=refresh content='1;url=/'>");
    }

    bool PairApi(Req r)
    {
        if (r.Method == "GET" && r.Path == "/api/pair/info")
        {
            string code;
            DateTime expires;
            _auth.GetPairCode(out code, out expires);
            var urls = NetworkInfo.CandidateHosts(_cfg).Select(h => "http://" + h + ":" + _cfg.Port + "/auth?c=" + code).ToList();
            WriteJson(r.Ctx, 200, new Dictionary<string, object> {
                { "urls", urls }, { "expiresIn", Math.Max(0, (int)(expires - DateTime.UtcNow).TotalSeconds) },
                { "sessions", _auth.List() }, { "port", _cfg.Port }
            });
            return true;
        }
        if (r.Method == "POST" && r.Path == "/api/pair/revoke")
        {
            if (!SameOriginPost(r)) { WriteJson(r.Ctx, 403, Error("Origem inválida.")); return true; }
            var body = ReadBody(r);
            string id = Json.GetString(body, "id", null);
            if (id == "*") _auth.RevokeAll(); else _auth.RevokeById(id);
            WriteJson(r.Ctx, 200, new Dictionary<string, object> { { "ok", true } });
            return true;
        }
        WriteJson(r.Ctx, 404, Error("Rota inexistente."));
        return true;
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
                    if ((string)d["type"] == "Temperature" || (string)d["type"] == "Power") Put(series, (string)d["id"], i, d["value"]);
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

    void Stream(Req r)
    {
        var resp = r.Ctx.Response;
        resp.StatusCode = 200;
        resp.ContentType = "text/event-stream; charset=utf-8";
        resp.Headers["Cache-Control"] = "no-store";
        resp.SendChunked = true;
        var output = resp.OutputStream;
        Action<string> sub = null;
        sub = json =>
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes("data: " + json + "\n\n");
                output.Write(bytes, 0, bytes.Length);
                output.Flush();
            }
            catch
            {
                _sampler.Unsubscribe(sub);
                try { resp.Abort(); } catch { }
            }
        };
        sub(_sampler.LatestJson);
        _sampler.Subscribe(sub);
    }

    Dictionary<string, object> ReadBody(Req r)
    {
        if (r.Ctx.Request.ContentLength64 > 64 * 1024) throw new ArgumentException("Corpo grande demais.");
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

    static void Html(HttpListenerContext ctx, int status, string message, string head = "")
    {
        string html = "<!doctype html><meta charset=utf-8><meta name=viewport content='width=device-width,initial-scale=1'>" + head +
            "<link rel=stylesheet href=/style.css><main class=locked><h1>FanPilot</h1><p>" + WebUtility.HtmlEncode(message) + "</p></main>";
        var bytes = Encoding.UTF8.GetBytes(html);
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
    }
}
