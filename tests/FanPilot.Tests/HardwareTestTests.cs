// Item 27: the --hardware-test sequence and its safety rules, on the fake board with no real waiting.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

public sealed class HardwareTestTests : IDisposable
{
    readonly TempDir _dir = new TempDir();
    readonly AppConfig _cfg = new AppConfig();

    public void Dispose() { _dir.Dispose(); }

    HardwareTest Test(FakeBoard board, FakeGpu gpu, Action<HardwareTest> onSleep = null)
    {
        HardwareTest test = null;
        test = new HardwareTest(board, gpu, _cfg, _dir.Path, () => board, _ => { if (onSleep != null) onSleep(test); }, TimeSpan.Zero);
        return test;
    }

    static Dictionary<string, object> Fixed(int percent)
    {
        return new Dictionary<string, object> { { "mode", "fixed" }, { "percent", percent } };
    }

    static Dictionary<string, object> RequestOf(FakeBoard board, AppConfig cfg, string dir, int fan)
    {
        var fans = (List<object>)new FanManager(board, cfg, dir).Describe()["fans"];
        return ((Dictionary<string, object>)fans[fan])["request"] as Dictionary<string, object>;
    }

    [Fact]
    public void FullRunPassesWritesTheReportAndPutsEverythingBack()
    {
        var board = new FakeBoard(4);
        var gpu = new FakeGpu(83);
        string before = board.GetCurve(1).Signature();
        string report;
        int code = Test(board, gpu).Execute(1, out report);

        Assert.Equal(HardwareTest.Passed, code);
        string text = File.ReadAllText(report);
        Assert.Contains("**passou**", text);
        Assert.Contains("| Fixo 40% e 100% | ok |", text);
        Assert.Contains("| Devolver tudo como estava | ok |", text);
        Assert.Equal(before, board.GetCurve(1).Signature());
        Assert.Null(RequestOf(board, _cfg, _dir.Path, 1));      // unmanaged again, as it was
        Assert.Equal("auto", gpu.Read()["fanMode"]);
    }

    // On the B760M every fan has the user's own curve: the test must give that back, not the original.
    [Fact]
    public void TheUsersOwnSettingComesBackNotTheOriginal()
    {
        var board = new FakeBoard(4);
        new FanManager(board, _cfg, _dir.Path).Describe();
        new FanManager(board, _cfg, _dir.Path).Apply(1, Fixed(55));
        string mine = board.GetCurve(1).Signature();
        string report;
        Assert.Equal(HardwareTest.Passed, Test(board, new FakeGpu(83)).Execute(1, out report));
        Assert.Equal(mine, board.GetCurve(1).Signature());
        Assert.Equal(55, Json.GetInt(RequestOf(board, _cfg, _dir.Path, 1), "percent", 0));
    }

    [Fact]
    public void AGpuLeftInManualIsLeftInManual()
    {
        var gpu = new FakeGpu(83);
        gpu.SetFixed(70);
        string report;
        Test(new FakeBoard(4), gpu).Execute(1, out report);
        Assert.Equal(70, gpu.Manual);
    }

    [Theory]
    [InlineData("CPU")]
    [InlineData("SYS_FAN5_PUMP")]
    [InlineData("AIO Pump")]
    public void RefusesTheCpuHeaderAndPumpsWithoutTouchingAnything(string title)
    {
        var board = new FakeBoard(4);
        board.Headers[1].Title = title;
        string report;
        Assert.Equal(HardwareTest.Refused, Test(board, new FakeGpu(83)).Execute(1, out report));
        Assert.Null(report);
        Assert.Equal(0, board.Writes);
    }

    [Fact]
    public void PicksACaseFanByDefault()
    {
        var board = new FakeBoard(4);
        string report;
        Assert.Equal(HardwareTest.Passed, Test(board, new FakeGpu(83)).Execute(null, out report));
        Assert.Contains("[1] System 1", File.ReadAllText(report)); // fan 0 is the CPU's
    }

    [Fact]
    public void AnOverheatingGpuAbortsBeforeTouchingAnything()
    {
        var board = new FakeBoard(4);
        string report;
        Assert.Equal(HardwareTest.Aborted, Test(board, new FakeGpu(83) { Temp = 85 }).Execute(1, out report));
        Assert.Contains("GPU a 85", File.ReadAllText(report));
        Assert.Equal(0, board.Writes);
    }

    [Fact]
    public void CtrlCInTheMiddleStopsAndPutsTheFanBack()
    {
        var board = new FakeBoard(4);
        var gpu = new FakeGpu(83);
        string before = board.GetCurve(1).Signature();
        int sleeps = 0;
        string report;
        int code = Test(board, gpu, t => { if (++sleeps == 3) t.Cancel(); }).Execute(1, out report);
        Assert.Equal(HardwareTest.Aborted, code);
        Assert.Contains("Ctrl+C", File.ReadAllText(report));
        Assert.Equal(before, board.GetCurve(1).Signature());
        Assert.False(new FanManager(board, _cfg, _dir.Path).TurboActive);
    }

    [Fact]
    public void ATemperatureRiseDuringAStepAbortsAndRestores()
    {
        var board = new FakeBoard(4);
        string before = board.GetCurve(1).Signature();
        string report;
        var gpu = new FakeGpu(83);
        // The GPU step waits a few seconds: the CPU heats up meanwhile (settle is zero here, so earlier steps never sleep).
        int code = Test(board, gpu, t => { if (t.CurrentStep.StartsWith("GPU")) board.Temps[2] = 92; }).Execute(1, out report);
        Assert.Equal(HardwareTest.Aborted, code);
        Assert.Contains("CPU a 92", File.ReadAllText(report));
        Assert.Equal(before, board.GetCurve(1).Signature());
        Assert.Equal("auto", gpu.Read()["fanMode"]); // the GPU, set to 50% by the step, is back on automatic
    }

    [Fact]
    public void SomethingOverwritingTheCurveFailsTheWatchdogStep()
    {
        var board = new FakeBoard(4);
        bool overwritten = false;
        string report;
        int code = Test(board, new FakeGpu(83), t =>
        {
            if (!overwritten && t.CurrentStep.StartsWith("Curva")) { overwritten = true; board.ChangeExternally(1, FakeBoard.DefaultCurve(1, 0)); }
        }).Execute(1, out report);
        Assert.Equal(HardwareTest.Failed, code);
        Assert.Contains("| Curva por 60 s sem o watchdog reaplicar | falhou |", File.ReadAllText(report));
    }

    [Fact]
    public void AFanThatIgnoresItsSpeedFailsTheSpeedStepAndIsPutBack()
    {
        var board = new FakeBoard(4);
        board.Headers[1].StuckRpm = 1200;
        string before = board.GetCurve(1).Signature();
        string report;
        Assert.Equal(HardwareTest.Failed, Test(board, new FakeGpu(83)).Execute(1, out report));
        Assert.Contains("o RPM quase não mudou", File.ReadAllText(report));
        Assert.Equal(before, board.GetCurve(1).Signature());
    }

    [Fact]
    public void AFanWithoutATachometerIsCheckedByWhatTheBoardRuns()
    {
        var board = new FakeBoard(4);
        board.Headers[1].HasTach = false; // reads 0 RPM, like CPU and System 3 on the B760M
        string report;
        Assert.Equal(HardwareTest.Passed, Test(board, new FakeGpu(83)).Execute(1, out report));
        Assert.Contains("sem tacômetro", File.ReadAllText(report));
    }

    [Fact]
    public void OnASoftwareCurveBoardExitHandsTheFanToTheBios()
    {
        var board = new FakeBoard(4, hardwareCurves: false);
        string report;
        Assert.Equal(HardwareTest.Passed, Test(board, new FakeGpu(83)).Execute(1, out report));
        Assert.Contains("voltou à BIOS", File.ReadAllText(report));
        Assert.True(board.GetCurve(1).IsBios); // put back: it was on the BIOS before the test
    }
}
