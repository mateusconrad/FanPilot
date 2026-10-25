// Item 26: the fake boards "--fake-board=<name>" runs FanPilot on, which the end-to-end tests (tests/e2e) rely on.
using System;
using System.Linq;
using Xunit;

public class FakeScenarioTests
{
    [Fact]
    public void EveryScenarioOpensExceptTheSlowOneQuickly()
    {
        // "slow" is the demo board after 4 s, on purpose; the rest open at once.
        foreach (string name in FakeBoard.Scenarios.Where(n => n != "slow"))
        {
            Assert.NotNull(FakeBoard.Scenario(name));
            Assert.NotNull(FakeGpu.Scenario(name, 83).Latest);
        }
    }

    [Fact]
    public void UnknownScenarioIsRefused()
    {
        Assert.Throws<ArgumentException>(() => FakeBoard.Scenario("nenhum"));
    }

    [Fact]
    public void WorstCaseHasEightHeadersAndADisconnectedSensor()
    {
        var b = FakeBoard.Scenario("worst");
        Assert.Equal(8, b.GetCurves().Count);
        Assert.Equal(9, b.TempInputTitles.Length);
        var temps = b.ReadSensors().Where(s => s.Type == "Temperature").Select(s => s.Value).ToList();
        // An open input keeps its impossible reading even on the animated board.
        Assert.Contains(127.0, temps);
        Assert.Contains(-128.0, temps);
        Assert.Contains("bomba", b.GetCurve(7).Title);
    }

    [Fact]
    public void UnsupportedBoardOnlyMonitors()
    {
        var b = FakeBoard.Scenario("unsupported");
        Assert.False(b.FanControlSupported);
        Assert.NotEmpty(b.ReadSensors());
        var gpu = FakeGpu.Scenario("unsupported", 83);
        Assert.Equal(false, gpu.Read()["ok"]);
        Assert.Throws<InvalidOperationException>(() => gpu.SetFixed(50));
    }

    [Fact]
    public void DownBoardFailsEveryCallLikeTheStoppedService()
    {
        var b = FakeBoard.Scenario("down");
        var ex = Assert.Throws<InvalidOperationException>(() => b.ReadSensors());
        Assert.StartsWith("EasyTuneEngineService indisponível", ex.Message);
        // Not a one-off failure: the next call fails the same way.
        Assert.Throws<InvalidOperationException>(() => b.GetCurves());
    }

    [Fact]
    public void LhmBoardRunsTheCurvesItselfAndStartsOnTheBios()
    {
        var b = FakeBoard.Scenario("lhm");
        Assert.False(b.HardwareCurves);
        Assert.All(b.GetCurves(), c => Assert.True(c.IsBios));
    }
}
