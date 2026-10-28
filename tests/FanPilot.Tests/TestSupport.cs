// Shared by the tests: a scratch data folder per test, paths of the test output, a free TCP port.
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;

// FanManager and AuthManager keep their state in files: each test gets a fresh folder, deleted afterwards.
sealed class TempDir : IDisposable
{
    public readonly string Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fanpilot-tests", Guid.NewGuid().ToString("N"));

    public TempDir() { Directory.CreateDirectory(Path); }

    public string File(string name) { return System.IO.Path.Combine(Path, name); }

    public void Dispose()
    {
        try { Directory.Delete(Path, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

static class TestEnv
{
    // xunit.runner.json turns shadow copying off, so this is the build output: FanPilot.exe, wwwroot\, fixtures\.
    public static readonly string OutputDir = Path.GetDirectoryName(typeof(TestEnv).Assembly.Location);

    // Listening on every address ("+", as FanPilot does) needs Administrator on Windows; CI runners have it, this PC
    // may not. On Linux: root, which a container has and the CI's runner doesn't.
    public static readonly bool IsAdministrator = Platform.IsElevated;

    public static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    // Starts the server create(port) returns on a free port. A port that FreePort found free can be taken before
    // the server binds it: it comes from the ephemeral range, which the tests' own client connections use too
    // (the CI saw it once, elevated, listening on every address). So a failed start tries another port.
    public static WebServer StartServer(Func<int, WebServer> create)
    {
        for (int attempt = 1; ; attempt++)
        {
            var server = create(FreePort());
            try
            {
                server.Start();
                return server;
            }
            catch (HttpListenerException) when (attempt < 5) { server.Stop(); }
        }
    }
}
