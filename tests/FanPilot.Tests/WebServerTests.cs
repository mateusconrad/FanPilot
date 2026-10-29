// The real web server, auth, sampler and fan manager on a FakeBoard, on a free port. Without Administrator it
// listens on "localhost" only, so the tests that need other Host headers (and FanPilot's "+" prefix) are skipped
// here and run where the tests are elevated (CI).
using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Xunit;

public sealed class WebServerTests : IDisposable
{
    readonly TempDir _dir = new TempDir();
    readonly FakeBoard _board = new FakeBoard(4);
    readonly int _port;
    readonly Sampler _sampler;
    readonly WebServer _server;
    readonly IntegrationTokens _tokens;

    // In App.Run's order: the port first, then the board's side attached.
    public WebServerTests()
    {
        var cfg = new AppConfig();
        _server = TestEnv.StartServer(port =>
        {
            cfg.Port = port;
            return new WebServer(cfg, System.IO.Path.Combine(TestEnv.OutputDir, "wwwroot"))
            {
                ListenHost = TestEnv.IsAdministrator ? "+" : "localhost"
            };
        });
        _port = cfg.Port;
        var gpu = new FakeGpu(cfg.GpuSafetyTemp);
        var fans = new FanManager(_board, cfg, _dir.Path);
        fans.Describe();
        _sampler = new Sampler(_board, gpu);
        _tokens = new IntegrationTokens(_dir.Path);
        _server.Attach(new AuthManager(_dir.Path, cfg), _sampler, fans, gpu, tokens: _tokens);
        _sampler.Start();
    }

    public void Dispose()
    {
        _server.Stop();
        _sampler.Stop();
        _dir.Dispose();
    }

    [Fact]
    public void ThePcOpensThePanelWithoutPairing()
    {
        var r = RawHttp.Send(_port, "GET", "/");
        Assert.Equal(200, r.Status);
        Assert.Contains("<title>FanPilot</title>", r.Body);
    }

    [Fact]
    public void ThePcReadsTheApi()
    {
        var r = RawHttp.Send(_port, "GET", "/api/fans");
        Assert.Equal(200, r.Status);
        Assert.Contains("\"fans\"", r.Body);
        Assert.Contains("nosniff", r.Head);
    }

    // Item 11: the panel's footer says which FanPilot answers, the first thing a bug report needs.
    [Fact]
    public void InfoTellsTheVersionAndTheBackend()
    {
        var r = RawHttp.Send(_port, "GET", "/api/info");
        Assert.Equal(200, r.Status);
        Assert.Contains("\"version\":\"" + AppVersion.Text + "\"", r.Body);
        Assert.Contains("\"backend\":\"Test board\"", r.Body);
    }

    // Item 19: the CPU chart's series, whichever reading is "the CPU" on this board.
    [Fact]
    public void HistoryHasTheCpuSeries()
    {
        Assert.True(SpinWait(() => RawHttp.Send(_port, "GET", "/api/history").Body.Contains("\"cpu-main\"")), "sem a série cpu-main");
    }

    static bool SpinWait(Func<bool> condition)
    {
        for (int i = 0; i < 50; i++) { if (condition()) return true; System.Threading.Thread.Sleep(100); }
        return condition();
    }

    [Fact]
    public void PanelModuleIsServedAsJavaScript()
    {
        var r = RawHttp.Send(_port, "GET", "/js/curve.mjs");
        Assert.Equal(200, r.Status);
        Assert.Contains("Content-Type: text/javascript", r.Head);
    }

    [Fact]
    public void PostWithoutTheFanPilotHeaderIsRefused()
    {
        var r = RawHttp.Send(_port, "POST", "/api/fans/1", json: "{\"mode\":\"fixed\",\"percent\":40}");
        Assert.Equal(403, r.Status);
        Assert.Equal(0, _board.Writes);
    }

    [Fact]
    public void PostFromAnotherOriginIsRefused()
    {
        var r = RawHttp.Send(_port, "POST", "/api/fans/1", json: "{\"mode\":\"fixed\",\"percent\":40}",
                             headers: new[] { "X-FanPilot: 1", "Origin: http://exemplo.invalid:" + _port });
        Assert.Equal(403, r.Status);
        Assert.Equal(0, _board.Writes);
    }

    [Fact]
    public void PostAppliesTheFanSetting()
    {
        var r = RawHttp.Send(_port, "POST", "/api/fans/1", json: "{\"mode\":\"fixed\",\"percent\":40}", headers: new[] { "X-FanPilot: 1" });
        Assert.Equal(200, r.Status);
        Assert.Equal(1, _board.Writes);
        Assert.Equal(102, _board.Headers[1].Curve.Points[0][1]);
    }

    [Fact]
    public void InvalidRequestGets400WithTheReason()
    {
        var r = RawHttp.Send(_port, "POST", "/api/fans/1", json: "{\"mode\":\"turbo\"}", headers: new[] { "X-FanPilot: 1" });
        Assert.Equal(400, r.Status);
        // With the code the panel translates (item 21), and the English text.
        Assert.Contains("\"code\":\"err.mode\"", r.Body);
        Assert.Contains("\"error\":\"Invalid mode.\"", r.Body);
    }

    [Fact]
    public void DiscardingAnUnknownOrphanIsABadRequest()
    {
        var r = RawHttp.Send(_port, "POST", "/api/fans/orphans", json: "{\"key\":\"fake/1\"}", headers: new[] { "X-FanPilot: 1" });
        Assert.Equal(400, r.Status);
    }

    [Fact]
    public void StreamSendsSnapshots()
    {
        string snapshot = RawHttp.FirstEvent(_port, "/api/stream", json => json.Contains("\"t\":"), TimeSpan.FromSeconds(10));
        Assert.NotNull(snapshot);
        Assert.Contains("\"fanRpm\"", snapshot);
    }

    // Item 02: a 17th panel open at the same time is turned away instead of piling up writers.
    [Fact]
    public void StreamIsLimitedToSixteenClients()
    {
        var open = new System.Collections.Generic.List<System.Net.Sockets.TcpClient>();
        try
        {
            for (int i = 0; i < SseHub.MaxClients; i++) open.Add(RawHttp.OpenStream(_port, "/api/stream"));
            var refused = RawHttp.Send(_port, "GET", "/api/stream");
            Assert.Equal(503, refused.Status);
            Assert.Contains("Retry-After", refused.Head);
            open[0].Close();
            open.RemoveAt(0);
            string snapshot = null;
            for (int attempt = 0; attempt < 20 && snapshot == null; attempt++)
            {
                snapshot = RawHttp.FirstEvent(_port, "/api/stream", json => true, TimeSpan.FromSeconds(2));
                if (snapshot == null) System.Threading.Thread.Sleep(100); // the closed one may take a write to be noticed
            }
            Assert.NotNull(snapshot);
        }
        finally { foreach (var c in open) c.Close(); }
    }

    // Item 05: the port is taken before the board is opened; until everything is attached, requests wait.
    [Fact]
    public void ServerThatIsStillStartingAnswers503()
    {
        var cfg = new AppConfig();
        var starting = TestEnv.StartServer(p =>
        {
            cfg.Port = p;
            return new WebServer(cfg, System.IO.Path.Combine(TestEnv.OutputDir, "wwwroot")) { ListenHost = "localhost" };
        });
        int port = cfg.Port;
        try
        {
            var api = RawHttp.Send(port, "GET", "/api/fans");
            Assert.Equal(503, api.Status);
            Assert.Contains("\"code\":\"server.starting\"", api.Body);
            Assert.Contains("Retry-After: 2", api.Head);
            Assert.Equal(503, RawHttp.Send(port, "GET", "/").Status);
            // A page the server writes itself follows the browser's language.
            Assert.Contains("is starting", RawHttp.Send(port, "GET", "/").Body);
            Assert.Contains("está iniciando", RawHttp.Send(port, "GET", "/", headers: new[] { "Accept-Language: pt-BR,pt;q=0.9,en;q=0.8" }).Body);
        }
        finally { starting.Stop(); }
    }

    // Item 05: a taken port fails at Start, before FanPilot opens the board (App.Run starts the server first).
    [Fact]
    public void TakenPortFailsToStartWithAClearMessage()
    {
        var second = new WebServer(new AppConfig { Port = _port }, System.IO.Path.Combine(TestEnv.OutputDir, "wwwroot"))
        {
            ListenHost = TestEnv.IsAdministrator ? "+" : "localhost"
        };
        var ex = Assert.Throws<HttpListenerException>(() => second.Start());
        string message = WebServer.PortProblem(_port, ex).Text;
        Assert.Contains(_port.ToString(), message);
        Assert.Contains("config.json", message);
    }

    // Item 01: a page that resolved its own domain to 127.0.0.1 (DNS rebinding) gets nothing, not even files.
    [SkippableFact]
    public void OtherHostsGet421()
    {
        Skip.IfNot(TestEnv.IsAdministrator, "precisa de Administrador para escutar em todos os endereços, como o FanPilot");
        Assert.Equal(421, RawHttp.Send(_port, "GET", "/api/fans", host: "exemplo.invalid:" + _port).Status);
        Assert.Equal(421, RawHttp.Send(_port, "GET", "/", host: "exemplo.invalid:" + _port).Status);
    }

    // An IPv4 address of this PC on the network, or null.
    static string LanAddress()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address))
            .Select(a => a.Address.ToString()).FirstOrDefault();
    }

    // Item 01: the PC itself is trusted only under a loopback name; by its LAN address it needs pairing like a phone.
    [SkippableFact]
    public void LanAddressNeedsPairing()
    {
        Skip.IfNot(TestEnv.IsAdministrator, "precisa de Administrador para escutar em todos os endereços, como o FanPilot");
        string lan = LanAddress();
        Skip.If(lan == null, "nenhum endereço IPv4 de rede neste PC");
        Assert.Equal(401, RawHttp.Send(_port, "GET", "/api/fans", host: lan + ":" + _port).Status);
        // The locked page (locked.html), not the panel: its words come from the dictionaries, so look for its script.
        Assert.Contains("/locked.js", RawHttp.Send(_port, "GET", "/", host: lan + ":" + _port).Body);
        // Panel mode too (item 22): read only, but the readings are still behind the pairing.
        Assert.Contains("/locked.js", RawHttp.Send(_port, "GET", "/panel", host: lan + ":" + _port).Body);
    }

    // Item 14b: the log is the PC's alone, as the pairing page is; not even a paired phone reads it.
    [SkippableFact]
    public void TheLogIsForThePcOnly()
    {
        var local = RawHttp.Send(_port, "GET", "/api/log");
        Assert.Equal(200, local.Status);
        Assert.Contains("Content-Type: text/plain", local.Head);
        Skip.IfNot(TestEnv.IsAdministrator, "precisa de Administrador para escutar em todos os endereços, como o FanPilot");
        string lan = LanAddress();
        Skip.If(lan == null, "nenhum endereço IPv4 de rede neste PC");
        Assert.NotEqual(200, RawHttp.Send(_port, "GET", "/api/log", host: lan + ":" + _port).Status);
    }

    // Item 23: Prometheus and the stable JSON, for the PC and for a token. A token that doesn't hold gets 401 even
    // from the PC, so the integration shows a revoked token as such.
    [Fact]
    public void TheIntegrationRoutesAnswerThePcAndAToken()
    {
        Assert.True(SpinWait(() => RawHttp.Send(_port, "GET", "/metrics").Body.Contains("fanpilot_up 1")), "sem fanpilot_up 1");
        var metrics = RawHttp.Send(_port, "GET", "/metrics");
        Assert.Contains("Content-Type: text/plain; version=0.0.4", metrics.Head);
        Assert.Contains("fanpilot_fan_rpm{fan=", metrics.Body);
        var made = _tokens.Create("Home Assistant");
        var auth = new[] { "Authorization: Bearer " + made["token"] };
        var json = RawHttp.Send(_port, "GET", "/api/v1/sensors", headers: auth);
        Assert.Equal(200, json.Status);
        Assert.Contains("\"version\":1", json.Body);
        _tokens.Revoke((string)made["id"]);
        var revoked = RawHttp.Send(_port, "GET", "/api/v1/sensors", headers: auth);
        Assert.Equal(401, revoked.Status);
        Assert.Contains("WWW-Authenticate: Bearer", revoked.Head);
        Assert.Contains("err.token.invalid", revoked.Body);
    }

    // Item 23: tokens are made and revoked on the PC, by the /pair page (same origin).
    [Fact]
    public void ThePairPageMakesAndRevokesTokens()
    {
        var made = RawHttp.Send(_port, "POST", "/api/pair/tokens", json: "{\"name\":\"Grafana\"}", headers: new[] { "X-FanPilot: 1" });
        Assert.Equal(200, made.Status);
        var d = Json.ParseObject(made.Body);
        Assert.NotNull(_tokens.Validate((string)d["token"]));
        Assert.Contains("\"name\":\"Grafana\"", RawHttp.Send(_port, "GET", "/api/pair/info").Body);
        Assert.DoesNotContain((string)d["token"], RawHttp.Send(_port, "GET", "/api/pair/info").Body);
        // Without the panel's header: another page trying to make one.
        Assert.Equal(403, RawHttp.Send(_port, "POST", "/api/pair/tokens", json: "{\"name\":\"x\"}").Status);
        string revoke = "{\"id\":\"" + d["id"] + "\"}";
        Assert.Equal(200, RawHttp.Send(_port, "POST", "/api/pair/tokens/revoke", json: revoke, headers: new[] { "X-FanPilot: 1" }).Status);
        Assert.Null(_tokens.Validate((string)d["token"]));
        Assert.Equal(404, RawHttp.Send(_port, "POST", "/api/pair/tokens/revoke", json: revoke, headers: new[] { "X-FanPilot: 1" }).Status);
    }

    // Item 23, from the network: no token, 401; a token reads the integration routes and nothing else, and changes
    // nothing.
    [SkippableFact]
    public void ATokenOnlyReadsTheIntegrationRoutes()
    {
        Skip.IfNot(TestEnv.IsAdministrator, "precisa de Administrador para escutar em todos os endereços, como o FanPilot");
        string lan = LanAddress();
        Skip.If(lan == null, "nenhum endereço IPv4 de rede neste PC");
        string host = lan + ":" + _port;
        var made = _tokens.Create("Prometheus");
        var auth = new[] { "Authorization: Bearer " + made["token"], "X-FanPilot: 1" };
        Assert.Equal(401, RawHttp.Send(_port, "GET", "/metrics", host: host).Status);
        Assert.Equal(401, RawHttp.Send(_port, "GET", "/api/v1/sensors", host: host).Status);
        Assert.Equal(200, RawHttp.Send(_port, "GET", "/metrics", host: host, headers: auth).Status);
        Assert.Equal(200, RawHttp.Send(_port, "GET", "/api/v1/sensors", host: host, headers: auth).Status);
        var fans = RawHttp.Send(_port, "GET", "/api/fans", host: host, headers: auth);
        Assert.Equal(403, fans.Status);
        Assert.Contains("err.token.scope", fans.Body);
        Assert.Equal(403, RawHttp.Send(_port, "POST", "/api/fans/turbo", host: host, json: "{\"on\":true}", headers: auth).Status);
        Assert.Equal(403, RawHttp.Send(_port, "POST", "/api/pair/tokens", host: host, json: "{\"name\":\"x\"}", headers: auth).Status);
        Assert.Contains("\"turbo\":false", RawHttp.Send(_port, "GET", "/api/fans").Body);
        _tokens.Revoke((string)made["id"]);
        Assert.Equal(401, RawHttp.Send(_port, "GET", "/metrics", host: host, headers: auth).Status);
    }
}
