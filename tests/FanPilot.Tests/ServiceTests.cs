// Item 14a: the command line, the data folder, importing a portable install, and the host the console and the
// Windows service share. The service itself (FanPilot.exe --service under the Service Control Manager) is tested end
// to end in tests/e2e/service.spec.mjs, where the CI can install one.
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Xunit;

public class AppOptionsTests
{
    [Fact]
    public void ServiceModeTakesADataFolderAndNeverOpensTheBrowser()
    {
        var o = AppOptions.Parse(new[] { "--service", "--data", @"C:\ProgramData\FanPilot" }, false);
        Assert.True(o.Service);
        Assert.Equal(@"C:\ProgramData\FanPilot", o.DataDir);
        Assert.False(o.Interactive);
    }

    [Fact]
    public void OptionsCanBeWrittenWithAnEqualsSign()
    {
        var o = AppOptions.Parse(new[] { @"--data=D:\fp", @"--import-data=E:\old\data" }, false);
        Assert.Equal(@"D:\fp", o.DataDir);
        Assert.Equal(@"E:\old\data", o.ImportFrom);
    }

    [Theory]
    [InlineData("--data")]
    [InlineData("--import-data")]
    public void AFolderOptionWithoutAFolderIsAnError(string option)
    {
        Assert.Throws<UserError>(() => AppOptions.Parse(new[] { option }, false));
        Assert.Throws<UserError>(() => AppOptions.Parse(new[] { option, "--service" }, false));
        Assert.Throws<UserError>(() => AppOptions.Parse(new[] { option + "=" }, false));
    }

    [Fact]
    public void TheFakeBoardExistsOnlyWhereAllowed()
    {
        Assert.Equal("worst", AppOptions.Parse(new[] { "--fake-board=worst" }, true).FakeScenario);
        Assert.Equal("demo", AppOptions.Parse(new[] { "--fake-board" }, true).FakeScenario);
        Assert.Null(AppOptions.Parse(new[] { "--fake-board=worst" }, false).FakeScenario);
    }

    [Fact]
    public void OptionsForOthersAreLeftAlone()
    {
        var o = AppOptions.Parse(new[] { "--hardware-test", "--fan", "2", "--no-browser" }, false);
        Assert.True(o.HardwareTest);
        Assert.False(o.Interactive);
        Assert.Equal(new[] { "--hardware-test", "--fan", "2", "--no-browser" }, o.Raw);
    }
}

public sealed class DataImportTests : IDisposable
{
    readonly TempDir _old = new TempDir(), _new = new TempDir();

    public void Dispose() { _old.Dispose(); _new.Dispose(); }

    [Fact]
    public void TheStateIsCopiedAndTheLogsStay()
    {
        foreach (string name in DataImport.FileNames.Where(n => n != "machine.json")) File.WriteAllText(_old.File(name), "{}");
        File.WriteAllText(_old.File("fanpilot.log"), "linhas antigas");
        Assert.Equal(0, DataImport.Run(_old.Path, _new.Path, false));
        foreach (string name in DataImport.FileNames) Assert.True(File.Exists(_new.File(name)), name);
        Assert.False(File.Exists(_new.File("fanpilot.log")));
    }

    [Fact]
    public void NothingIsImportedWhileFanPilotRuns()
    {
        File.WriteAllText(_old.File("desired-curves.json"), "{}");
        Assert.Equal(4, DataImport.Run(_old.Path, _new.Path, true));
        Assert.False(File.Exists(_new.File("desired-curves.json")));
    }

    [Fact]
    public void AnotherPCsStateIsSetAsideAsOnAnyStart()
    {
        File.WriteAllText(_old.File("machine.json"), "{\"machineId\": \"outro-pc\", \"board\": \"Outra placa\"}");
        File.WriteAllText(_old.File("desired-curves.json"), "{\"version\": 2, \"fans\": {}}");
        Assert.Equal(0, DataImport.Run(_old.Path, _new.Path, false));
        Assert.False(File.Exists(_new.File("desired-curves.json")));
        Assert.Single(Directory.GetDirectories(_new.Path, "outra-maquina-*"));
    }

    [Fact]
    public void ImportingAFolderIntoItselfIsRefused()
    {
        Assert.Equal(1, DataImport.Run(_old.Path, _old.Path, false));
    }
}

public sealed class FanPilotHostTests : IDisposable
{
    readonly TempDir _dir = new TempDir();
    readonly FakeBoard _board = new FakeBoard(4);
    FanPilotHost _host;
    int _port;

    public void Dispose()
    {
        if (_host != null) _host.Stop();
        _dir.Dispose();
    }

    // Started on a free port; a port taken in between (5) is retried, as TestEnv.StartServer does.
    FanPilotHost Started(Func<IBoard> board = null)
    {
        for (int attempt = 1; ; attempt++)
        {
            var cfg = new AppConfig { Port = TestEnv.FreePort() };
            var host = new FanPilotHost(cfg, Path.Combine(TestEnv.OutputDir, "wwwroot"), _dir.Path,
                board ?? (() => _board), () => new FakeGpu(cfg.GpuSafetyTemp), false, TestEnv.IsAdministrator);
            int code = host.Start();
            if (code == 5 && attempt < 5) continue;
            Assert.Equal(0, code);
            _port = cfg.Port;
            return _host = host;
        }
    }

    static bool Answers(int port)
    {
        try { return RawHttp.Send(port, "GET", "/api/info").Status == 200; }
        catch (SocketException) { return false; }
        catch (IOException) { return false; }
    }

    [Fact]
    public void StartsAndAnswersThePanel()
    {
        Started();
        Assert.True(Answers(_port));
    }

    [Fact]
    public void StopClosesThePortAndHandsTheBoardBack()
    {
        var host = Started();
        host.Stop();
        Assert.True(_board.ShutDown);
        Assert.False(Answers(_port));
        host.Stop(); // a second way out (the service's stop, then the process exit) does nothing
    }

    [Fact]
    public void ATakenPortFailsBeforeTheBoardIsOpened()
    {
        int port = TestEnv.FreePort();
#if NETFRAMEWORK
        var other = new HttpListener();
        other.Prefixes.Add("http://localhost:" + port + "/");
#else
        // Linux: .NET's HttpListener takes "localhost" on ::1 alone, which doesn't stop FanPilot's 127.0.0.1 (item 28).
        var other = new TcpListener(IPAddress.Any, port);
#endif
        other.Start();
        try
        {
            bool opened = false;
            var cfg = new AppConfig { Port = port };
            var host = new FanPilotHost(cfg, Path.Combine(TestEnv.OutputDir, "wwwroot"), _dir.Path,
                () => { opened = true; return _board; }, () => new FakeGpu(83), false, false);
            Assert.Equal(5, host.Start());
            Assert.False(opened);
        }
        finally { other.Stop(); }
    }

    [Fact]
    public void ABoardThatCannotOpenIsExitCode3()
    {
        var cfg = new AppConfig();
        FanPilotHost host = null;
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            cfg.Port = TestEnv.FreePort();
            host = new FanPilotHost(cfg, Path.Combine(TestEnv.OutputDir, "wwwroot"), _dir.Path,
                () => { throw new InvalidOperationException("O Gigabyte Control Center não foi encontrado."); }, () => new FakeGpu(83), false, TestEnv.IsAdministrator);
            int code = host.Start();
            if (code == 5) continue;
            Assert.Equal(3, code);
            break;
        }
        host.Stop();
    }

    [Fact]
    public void BackFromSleepTheCurvesArePutBackAtOnce()
    {
        var host = Started();
        host.Fans.Apply(1, new System.Collections.Generic.Dictionary<string, object> { { "mode", "fixed" }, { "percent", 40 } });
        string mine = _board.GetCurve(1).Signature();
        _board.ChangeExternally(1, FakeBoard.DefaultCurve(1, 0)); // what the BIOS can do on resume
        host.Resumed();
        Assert.Equal(mine, _board.GetCurve(1).Signature());
    }
}
