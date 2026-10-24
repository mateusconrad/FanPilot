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
    readonly int _port = TestEnv.FreePort();
    readonly Sampler _sampler;
    readonly WebServer _server;

    public WebServerTests()
    {
        var cfg = new AppConfig { Port = _port };
        var gpu = new FakeGpu(cfg.GpuSafetyTemp);
        var fans = new FanManager(_board, cfg, _dir.Path);
        fans.Describe();
        _sampler = new Sampler(_board, gpu);
        _server = new WebServer(cfg, System.IO.Path.Combine(TestEnv.OutputDir, "wwwroot"), new AuthManager(_dir.Path, cfg), _sampler, fans, gpu)
        {
            ListenHost = TestEnv.IsAdministrator ? "+" : "localhost"
        };
        _server.Start();
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
        Assert.Contains("Modo", r.Body);
    }

    [Fact]
    public void StreamSendsSnapshots()
    {
        string snapshot = RawHttp.FirstEvent(_port, "/api/stream", json => json.Contains("\"t\":"), TimeSpan.FromSeconds(10));
        Assert.NotNull(snapshot);
        Assert.Contains("\"fanRpm\"", snapshot);
    }

    // Item 05: the port is taken before the board is opened; until everything is attached, requests wait.
    [Fact]
    public void ServerThatIsStillStartingAnswers503()
    {
        int port = TestEnv.FreePort();
        var starting = new WebServer(new AppConfig { Port = port }, System.IO.Path.Combine(TestEnv.OutputDir, "wwwroot")) { ListenHost = "localhost" };
        starting.Start();
        try
        {
            var api = RawHttp.Send(port, "GET", "/api/fans");
            Assert.Equal(503, api.Status);
            Assert.Contains("iniciando", api.Body);
            Assert.Contains("Retry-After: 2", api.Head);
            Assert.Equal(503, RawHttp.Send(port, "GET", "/").Status);
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
        string message = WebServer.PortProblem(_port, ex);
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

    // Item 01: the PC itself is trusted only under a loopback name; by its LAN address it needs pairing like a phone.
    [SkippableFact]
    public void LanAddressNeedsPairing()
    {
        Skip.IfNot(TestEnv.IsAdministrator, "precisa de Administrador para escutar em todos os endereços, como o FanPilot");
        string lan = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address))
            .Select(a => a.Address.ToString()).FirstOrDefault();
        Skip.If(lan == null, "nenhum endereço IPv4 de rede neste PC");
        Assert.Equal(401, RawHttp.Send(_port, "GET", "/api/fans", host: lan + ":" + _port).Status);
        Assert.Contains("bloqueado", RawHttp.Send(_port, "GET", "/", host: lan + ":" + _port).Body);
    }
}
