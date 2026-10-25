// Item 18: global profiles (quiet, standard, performance) over the user's own settings, Turbo as a layer on top,
// pumps left alone, and the mode surviving a restart on both kinds of board.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

public class FanPresetsTests
{
    [Fact]
    public void EightPointsAreThePresetItself()
    {
        Assert.Equal(new[] { 0, 25, 40, 25, 50, 30, 60, 40, 65, 50, 70, 65, 75, 80, 85, 100 },
                     FanPresets.Points("quiet", 8, 90, 20).SelectMany(p => p).ToArray());
    }

    [Fact]
    public void OtherPointCountsAreResampledAsThePanelDoes()
    {
        // curve.mjs resample("Padrão", 5): temperatures spread over 0..82, values from the curve, .5 rounded up.
        Assert.Equal(new[] { new[] { 0, 30 }, new[] { 21, 30 }, new[] { 41, 36 }, new[] { 62, 65 }, new[] { 82, 100 } },
                     FanPresets.Points("standard", 5, 90, 20));
    }

    [Fact]
    public void ALowFullSpeedLimitStillGivesIncreasingTemperatures()
    {
        var pts = FanPresets.Points("quiet", 8, 70, 20);
        Assert.Equal(70, pts.Last()[0]);
        Assert.True(pts.Zip(pts.Skip(1), (a, b) => b[0] > a[0]).All(x => x), string.Join(" ", pts.Select(p => p[0])));
        Assert.Equal(100, pts.Last()[1]);
    }

    [Fact]
    public void TheFloorAppliesToProfilesToo()
    {
        Assert.All(FanPresets.Points("quiet", 8, 90, 30), p => Assert.True(p[1] >= 30));
    }
}

public sealed class ProfileTests : IDisposable
{
    readonly TempDir _dir = new TempDir();
    readonly AppConfig _cfg = new AppConfig();

    public void Dispose() { _dir.Dispose(); }

    FanManager Manager(IBoard board)
    {
        var m = new FanManager(board, _cfg, _dir.Path);
        m.Describe();
        return m;
    }

    static Dictionary<string, object> Fixed(int percent)
    {
        return new Dictionary<string, object> { { "mode", "fixed" }, { "percent", percent } };
    }

    static string[] Signatures(IBoard board) { return board.GetCurves().Select(c => c.Signature()).ToArray(); }

    static Dictionary<string, object> Fan(FanManager m, int i)
    {
        return (Dictionary<string, object>)((List<object>)m.Describe()["fans"])[i];
    }

    // A board where the user set fan 1 to 40% and left the others as they were.
    (FakeBoard, FanManager, string[]) Personal()
    {
        var board = new FakeBoard(4);
        var m = Manager(board);
        m.Apply(1, Fixed(40));
        return (board, m, Signatures(board));
    }

    [Fact]
    public void AProfileGoesOnEveryFanAndPersonalBringsBackExactlyWhatWasThere()
    {
        var (board, m, mine) = Personal();
        m.SetMode("profile:quiet");
        Assert.Equal("profile:quiet", m.Mode);
        var quiet = FanPresets.Points("quiet", 8, 90, 20);
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(quiet.Select(p => (double)p[0]), board.GetCurve(i).Points.Select(p => p[0]));
            Assert.Equal("quiet", Json.GetString((Dictionary<string, object>)Fan(m, i)["request"], "profile", null));
        }
        m.SetMode("custom");
        Assert.Equal("custom", m.Mode);
        Assert.Equal(mine, Signatures(board));
        Assert.False((bool)Fan(m, 2)["managed"]); // was the original curve, and is again
    }

    [Fact]
    public void GoingFromProfileToProfileStillReturnsToThePersonalSettings()
    {
        var (board, m, mine) = Personal();
        m.SetMode("profile:quiet");
        m.SetMode("profile:performance");
        Assert.Equal(FanPresets.Points("performance", 8, 90, 20).Select(p => (double)p[0]), board.GetCurve(3).Points.Select(p => p[0]));
        m.SetMode("custom");
        Assert.Equal(mine, Signatures(board));
    }

    [Fact]
    public void TurboOverAProfileGoesBackToTheProfile()
    {
        var (board, m, mine) = Personal();
        m.SetMode("profile:quiet");
        var quiet = Signatures(board);
        m.SetTurbo(true);
        Assert.All(board.GetCurves(), c => Assert.All(c.Points, p => Assert.Equal(255, p[1])));
        m.SetTurbo(false);
        Assert.Equal("profile:quiet", m.Mode);
        Assert.Equal(quiet, Signatures(board));
        m.SetMode("custom");
        Assert.Equal(mine, Signatures(board));
    }

    [Fact]
    public void AProfileChosenDuringTurboWaitsForTurboToEnd()
    {
        var (board, m, mine) = Personal();
        m.SetTurbo(true);
        m.SetMode("profile:standard");
        Assert.All(board.GetCurves(), c => Assert.All(c.Points, p => Assert.Equal(255, p[1])));
        m.SetTurbo(false);
        Assert.Equal(FanPresets.Points("standard", 8, 90, 20).Select(p => (double)p[0]), board.GetCurve(0).Points.Select(p => p[0]));
        m.SetMode("custom");
        Assert.Equal(mine, Signatures(board));
    }

    [Fact]
    public void EditingAFanLeavesTheProfileAndTheOthersKeepItsCurve()
    {
        var (board, m, _) = Personal();
        m.SetMode("profile:quiet");
        var quietFan2 = board.GetCurve(2).Signature();
        m.Apply(0, Fixed(60));
        Assert.Equal("custom", m.Mode);
        Assert.Equal(quietFan2, board.GetCurve(2).Signature());
        Assert.True((bool)Fan(m, 2)["managed"]);
        m.SetMode("profile:performance");
        m.SetMode("custom");
        Assert.Equal(quietFan2, board.GetCurve(2).Signature()); // the profile's curve is now fan 2's own setting
    }

    [Fact]
    public void PumpsDoNotChangeWithProfiles()
    {
        var board = FakeBoard.Scenario("worst");
        board.Animated = false;
        var m = Manager(board);
        string pump = board.GetCurve(6).Signature();
        m.SetMode("profile:quiet");
        Assert.Equal(pump, board.GetCurve(6).Signature());
        Assert.NotEqual(pump, board.GetCurve(2).Signature());
        m.SetTurbo(true);
        m.SetTurbo(false);
        Assert.Equal(pump, board.GetCurve(6).Signature());
    }

    [Theory]
    [InlineData(true)]   // Gigabyte: curves in the chip
    [InlineData(false)]  // LibreHardwareMonitor: FanPilot runs them, every header back on the BIOS at start
    public void TheModeSurvivesARestart(bool hardwareCurves)
    {
        var board = new FakeBoard(4, hardwareCurves);
        var m = Manager(board);
        m.Apply(1, Fixed(40));
        m.SetMode("profile:performance");
        var performance = Signatures(board);

        // A new boot: the chip keeps its curves; on LHM every header starts on the BIOS and FanPilot puts them back.
        var fresh = hardwareCurves ? board : new FakeBoard(4, false);
        var restarted = new FanManager(fresh, _cfg, _dir.Path);
        restarted.Start();
        Assert.Equal("profile:performance", restarted.Mode);
        Assert.Equal(performance, Signatures(fresh));
        restarted.SetMode("custom");
        Assert.Equal(Math.Round(40 * 2.55), fresh.GetCurve(1).Points[0][1]);
    }

    [Fact]
    public void AnOldTurboFileBecomesTheGlobalMode()
    {
        var board = new FakeBoard(4);
        Manager(board);
        File.WriteAllText(_dir.File("turbo.json"),
            "{\"version\": 2, \"active\": true, \"prev\": {\"fake/1\": {\"mode\": \"fixed\", \"percent\": 40}, \"fake/2\": null}}");
        var m = Manager(board);
        Assert.True(m.TurboActive);
        Assert.Equal("custom", m.Mode);
        Assert.False(File.Exists(_dir.File("turbo.json")));
        Assert.True(File.Exists(_dir.File("global-mode.json")));
        m.SetTurbo(false);
        Assert.Equal(Math.Round(40 * 2.55), board.GetCurve(1).Points[0][1]);
    }

    [Fact]
    public void AnUnknownModeIsRefused()
    {
        var (_, m, _) = Personal();
        Assert.Throws<UserError>(() => m.SetMode("profile:max"));
        Assert.Throws<UserError>(() => m.SetMode("silencioso"));
    }
}
