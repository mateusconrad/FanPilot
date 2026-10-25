// FanManager's safety rules and persistence, on a FakeBoard with the B760M's layout (4 headers, 8-point curves).
// Default limits (AppConfig): 20% floor, fixed mode back to 100% at 85 °C, full-speed point at 90 °C at the latest.
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

public sealed class FanManagerTests : IDisposable
{
    readonly TempDir _dir = new TempDir();
    readonly AppConfig _cfg = new AppConfig();
    readonly FakeBoard _board = new FakeBoard(4);

    public void Dispose() { _dir.Dispose(); }

    FanManager Manager()
    {
        var m = new FanManager(_board, _cfg, _dir.Path);
        m.Describe(); // what Start() does first: save the original curves
        return m;
    }

    static Dictionary<string, object> Fixed(int percent)
    {
        return new Dictionary<string, object> { { "mode", "fixed" }, { "percent", percent } };
    }

    // [t, %] pairs, as the panel sends them.
    static Dictionary<string, object> Curve(params int[] tempPercent)
    {
        var points = new List<object>();
        for (int i = 0; i < tempPercent.Length; i += 2) points.Add(new object[] { tempPercent[i], tempPercent[i + 1] });
        return new Dictionary<string, object> { { "mode", "curve" }, { "points", points }, { "tempInput", 2 } };
    }

    static readonly int[] Standard = { 0, 30, 35, 30, 45, 40, 55, 55, 62, 65, 68, 75, 74, 85, 82, 100 };

    double[] Pwms(int fan) { return _board.Headers[fan].Curve.Points.Select(p => p[1]).ToArray(); }
    double[] Temps(int fan) { return _board.Headers[fan].Curve.Points.Select(p => p[0]).ToArray(); }

    [Fact]
    public void FixedModeIsFlatWithTheSafetyPoint()
    {
        Manager().Apply(1, Fixed(40));
        Assert.Equal(new[] { 0.0, 20, 30, 40, 50, 60, 70, 85 }, Temps(1));
        Assert.Equal(new[] { 102.0, 102, 102, 102, 102, 102, 102, 255 }, Pwms(1));
    }

    [Fact]
    public void FixedModeNeverGoesBelowTheFloor()
    {
        Manager().Apply(1, Fixed(5));
        Assert.All(Pwms(1).Take(7), pwm => Assert.Equal(51, pwm)); // ceil(20% of 255)
    }

    [Fact]
    public void FixedModeKeepsTheBoardsPointCount()
    {
        var six = new FanCurve { Index = 0, TempInput = 2, Mode = "SmartFanMode", DeltaT = 1 };
        foreach (var t in new[] { 0, 30, 45, 60, 75 }) six.Points.Add(new[] { (double)t, 128 });
        six.Points.Add(new[] { 85.0, 255 });
        _board.ChangeExternally(0, six);
        Manager().Apply(0, Fixed(50));
        var temps = Temps(0);
        Assert.Equal(6, temps.Length);
        Assert.True(temps.Zip(temps.Skip(1), (a, b) => b > a).All(x => x), "temperaturas crescentes");
        Assert.Equal(new[] { 85.0, 255 }, _board.Headers[0].Curve.Points.Last());
    }

    [Fact]
    public void CurveNeedsTheBoardsPointCount()
    {
        var ex = Assert.Throws<UserError>(() => Manager().Apply(1, Curve(0, 30, 40, 50, 90, 100)));
        Assert.Equal("err.curve.points", ex.Msg.Key);
        Assert.Contains("exatamente 8 pontos", ex.Msg.In(I18n.Portuguese));
        Assert.Contains("exactly 8 points", ex.Message);
    }

    [Fact]
    public void FirstPointIsAlwaysAtZeroDegrees()
    {
        var points = (int[])Standard.Clone();
        points[0] = 25;
        points[2] = 30;
        Manager().Apply(1, Curve(points));
        Assert.Equal(0, Temps(1)[0]);
    }

    [Fact]
    public void TemperaturesMustIncrease()
    {
        var points = (int[])Standard.Clone();
        points[6] = points[4]; // third point at the same temperature as the second
        Assert.Throws<UserError>(() => Manager().Apply(1, Curve(points)));
    }

    [Fact]
    public void FullSpeedPointHasAMaximumTemperature()
    {
        var points = (int[])Standard.Clone();
        points[14] = 95;
        var ex = Assert.Throws<UserError>(() => Manager().Apply(1, Curve(points)));
        Assert.Contains("90", ex.Message);
    }

    [Fact]
    public void LastPointIsAlwaysFullSpeed()
    {
        var points = (int[])Standard.Clone();
        points[15] = 60;
        Manager().Apply(1, Curve(points));
        Assert.Equal(255, Pwms(1).Last());
    }

    [Fact]
    public void CurveRespectsTheFloorAndNeverGetsSlowerWhenHotter()
    {
        Manager().Apply(1, Curve(0, 10, 35, 50, 45, 30, 55, 55, 62, 65, 68, 75, 74, 85, 82, 100));
        var pwm = Pwms(1);
        double half = Math.Round(50 * 2.55); // 127: 2.55 is not exact in binary, so 50% rounds down
        Assert.Equal(51, pwm[0]);   // 10% raised to the 20% floor
        Assert.Equal(half, pwm[1]);
        Assert.Equal(half, pwm[2]); // asked 30% after 50%: held at 50%
        Assert.True(pwm.Zip(pwm.Skip(1), (a, b) => b >= a).All(x => x), "nunca desce");
    }

    [Fact]
    public void UnknownReferenceSensorIsRefused()
    {
        var request = Fixed(40);
        request["tempInput"] = 9;
        Assert.Throws<UserError>(() => Manager().Apply(1, request));
    }

    [Fact]
    public void StairStepStyleIsKeptAndUnknownStylesBecomeLinear()
    {
        var m = Manager();
        var steps = Curve(Standard);
        steps["style"] = "StairStepMode";
        m.Apply(1, steps);
        Assert.Equal("StairStepMode", _board.Headers[1].Curve.Mode);
        var odd = Curve(Standard);
        odd["style"] = "RpmFixedMode";
        m.Apply(1, odd);
        Assert.Equal("SmartFanMode", _board.Headers[1].Curve.Mode);
    }

    [Fact]
    public void AppliedCurveSurvivesARestart()
    {
        Manager().Apply(1, Fixed(40));
        var fan = (Dictionary<string, object>)((List<object>)Manager().Describe()["fans"])[1];
        Assert.True((bool)fan["managed"]);
        Assert.Equal(40, Json.GetInt((Dictionary<string, object>)fan["request"], "percent", 0));
    }

    [Fact]
    public void RestorePutsTheOriginalCurveBack()
    {
        string original = _board.GetCurve(1).Signature();
        var m = Manager();
        m.Apply(1, Fixed(40));
        m.Restore(1);
        Assert.Equal(original, _board.GetCurve(1).Signature());
        var fan = (Dictionary<string, object>)((List<object>)m.Describe()["fans"])[1];
        Assert.False((bool)fan["managed"]);
    }

    [Fact]
    public void TurboPutsEveryFanAtFullSpeedAndBlocksEditing()
    {
        var m = Manager();
        m.SetTurbo(true);
        for (int i = 0; i < 4; i++) Assert.All(Pwms(i), pwm => Assert.Equal(255, pwm));
        var ex = Assert.Throws<UserError>(() => m.Apply(1, Fixed(40)));
        Assert.Contains("Turbo", ex.Message);
    }

    [Fact]
    public void TurboOffPutsBackExactlyWhatWasThere()
    {
        string unmanaged = _board.GetCurve(2).Signature();
        var m = Manager();
        m.Apply(1, Fixed(40));
        string managed = _board.GetCurve(1).Signature();
        m.SetTurbo(true);
        m.SetTurbo(false);
        Assert.Equal(managed, _board.GetCurve(1).Signature());
        Assert.Equal(unmanaged, _board.GetCurve(2).Signature());
        var fans = (List<object>)m.Describe()["fans"];
        Assert.True((bool)((Dictionary<string, object>)fans[1])["managed"]);
        Assert.False((bool)((Dictionary<string, object>)fans[2])["managed"]);
    }

    [Fact]
    public void TurboSurvivesARestart()
    {
        var m = Manager();
        m.Apply(1, Fixed(40));
        string managed = _board.GetCurve(1).Signature();
        m.SetTurbo(true);
        var restarted = Manager();
        Assert.True(restarted.TurboActive);
        restarted.SetTurbo(false);
        Assert.Equal(managed, _board.GetCurve(1).Signature());
    }

    [Fact]
    public void TurboNeedsFans()
    {
        var m = new FanManager(new FakeBoard(0), _cfg, _dir.Path);
        Assert.Throws<UserError>(() => m.SetTurbo(true));
    }

    [Fact]
    public void WatchdogReappliesACurveChangedBehindItsBack()
    {
        var m = Manager();
        m.Apply(1, Fixed(40));
        string desired = _board.GetCurve(1).Signature();
        _board.ChangeExternally(1, FakeBoard.DefaultCurve(1, 0));
        int writes = _board.Writes;
        m.Watchdog();
        Assert.Equal(writes + 1, _board.Writes);
        Assert.Equal(desired, _board.GetCurve(1).Signature());
    }

    [Fact]
    public void WatchdogDoesNotLoopOnALossyReadback()
    {
        // The board stores every PWM rounded to a multiple of 5: what it reports never equals what was sent.
        _board.Readback = c =>
        {
            c.Points = c.Points.Select(p => new[] { p[0], Math.Min(255, Math.Round(p[1] / 5) * 5) }).ToList();
            return c;
        };
        var m = Manager();
        m.Apply(1, Fixed(40));
        int writes = _board.Writes;
        m.Watchdog();
        m.Watchdog();
        Assert.Equal(writes, _board.Writes);
    }

    // LibreHardwareMonitor boards start with every header on the BIOS: the saved curves must be put back.
    [Fact]
    public void SoftwareCurveBoardGetsItsCurvesBackAfterARestart()
    {
        var board = new FakeBoard(4, hardwareCurves: false);
        var m = new FanManager(board, _cfg, _dir.Path);
        m.Describe();
        m.Apply(1, Fixed(40));
        string desired = board.GetCurve(1).Signature();
        board.Shutdown();
        Assert.True(board.GetCurve(1).IsBios);
        new FanManager(board, _cfg, _dir.Path).ReapplyDesired();
        Assert.Equal(desired, board.GetCurve(1).Signature());
    }

    // Item 03: a damaged state file must not keep FanPilot from starting.
    [Fact]
    public void DamagedDesiredCurvesStartWithThePreviousVersion()
    {
        var m = Manager();
        m.Apply(1, Fixed(40));
        m.Apply(2, Fixed(50));                                   // desired-curves.json.bak now has only fan 1
        System.IO.File.WriteAllText(_dir.File("desired-curves.json"), "{\"1\": {\"request\"");
        var fans = (List<object>)Manager().Describe()["fans"];
        Assert.True((bool)((Dictionary<string, object>)fans[1])["managed"]);
        Assert.False((bool)((Dictionary<string, object>)fans[2])["managed"]);
    }

    [Fact]
    public void DamagedTurboFileMeansTurboOff()
    {
        System.IO.File.WriteAllText(_dir.File("turbo.json"), "not json");
        Assert.False(Manager().TurboActive);
    }

    // Item 03: with the backup lost while FanPilot manages a fan, its curve in the chip is FanPilot's own; saving that
    // as "original" would make Restore a silent no-op forever.
    [Fact]
    public void LostBackupIsNotRecapturedFromAFanFanPilotManages()
    {
        Manager().Apply(1, Fixed(40));
        System.IO.File.Delete(_dir.File("backup-curves.json"));
        System.IO.File.Delete(_dir.File("backup-curves.json.bak"));
        var m = Manager();
        var fans = (List<object>)m.Describe()["fans"];
        Assert.False((bool)((Dictionary<string, object>)fans[1])["hasBackup"]);
        Assert.True((bool)((Dictionary<string, object>)fans[2])["hasBackup"]);
        Assert.Throws<UserError>(() => m.Restore(1));
    }

    [Fact]
    public void LostBackupOnASoftwareCurveBoardMeansTheBios()
    {
        var board = new FakeBoard(4, hardwareCurves: false);
        var m = new FanManager(board, _cfg, _dir.Path);
        m.Describe();
        m.Apply(1, Fixed(40));
        System.IO.File.Delete(_dir.File("backup-curves.json"));
        var restarted = new FanManager(board, _cfg, _dir.Path);
        restarted.Describe();
        restarted.Restore(1);
        Assert.True(board.GetCurve(1).IsBios);
    }

    [Fact]
    public void RestoreOnASoftwareCurveBoardHandsTheFanBackToTheBios()
    {
        var board = new FakeBoard(4, hardwareCurves: false);
        var m = new FanManager(board, _cfg, _dir.Path);
        m.Describe();
        m.Apply(1, Fixed(40));
        m.Restore(1);
        Assert.True(board.GetCurve(1).IsBios);
    }

    // Item 15: how a software curve follows the temperature travels with it to the board and back to the panel.
    [Fact]
    public void TheResponseGoesWithTheCurve()
    {
        var board = new FakeBoard(4, hardwareCurves: false);
        var m = new FanManager(board, _cfg, _dir.Path);
        m.Describe();
        var smooth = Curve(Standard);
        smooth["response"] = "smooth";
        m.Apply(1, smooth);
        Assert.Equal("smooth", board.GetCurve(1).Response);
        var fan = (Dictionary<string, object>)((List<object>)m.Describe()["fans"])[1];
        Assert.Equal("smooth", Json.GetString((Dictionary<string, object>)fan["request"], "response", null));

        m.Apply(2, Curve(Standard));
        Assert.Equal("balanced", board.GetCurve(2).Response);
        m.Apply(3, Fixed(40));
        Assert.Null(board.GetCurve(3).Response); // a fixed speed is never smoothed
    }

    [Fact]
    public void AnUnknownResponseIsRefused()
    {
        var bad = Curve(Standard);
        bad["response"] = "turbo";
        var ex = Assert.Throws<UserError>(() => Manager().Apply(1, bad));
        Assert.Equal("err.response", ex.Msg.Key);
    }

    [Fact]
    public void CurvesSavedBeforeResponsesExistedGetTheBalancedOne()
    {
        var board = new FakeBoard(4, hardwareCurves: false);
        var m = new FanManager(board, _cfg, _dir.Path);
        m.Describe();
        m.Apply(1, Curve(Standard));
        m.Apply(2, Fixed(40));
        // As an older FanPilot wrote it: no "response" anywhere.
        var saved = Json.ParseObject(System.IO.File.ReadAllText(_dir.File("desired-curves.json")));
        foreach (var entry in ((Dictionary<string, object>)saved["fans"]).Values.Cast<Dictionary<string, object>>())
            ((Dictionary<string, object>)entry["curve"]).Remove("response");
        System.IO.File.WriteAllText(_dir.File("desired-curves.json"), Json.Serialize(saved));

        var fresh = new FakeBoard(4, hardwareCurves: false);
        new FanManager(fresh, _cfg, _dir.Path).ReapplyDesired();
        Assert.Equal("balanced", fresh.GetCurve(1).Response);
        Assert.Null(fresh.GetCurve(2).Response);
    }

    // Item 25: the GPU as the reference, where FanPilot runs the curve; a profile keeps the source the fan had.
    [Fact]
    public void TheSourceGoesWithTheCurveWhereFanPilotRunsIt()
    {
        var board = new FakeBoard(4, hardwareCurves: false);
        var m = new FanManager(board, _cfg, _dir.Path);
        m.Describe();
        var max = Curve(Standard);
        max["source"] = "max";
        m.Apply(1, max);
        Assert.Equal("max", board.GetCurve(1).Source);
        m.SetMode("profile:quiet");
        Assert.Equal("max", board.GetCurve(1).Source);
        m.SetMode("custom");
        m.Apply(2, Curve(Standard));
        Assert.Null(board.GetCurve(2).Source); // the sensor, as every curve before item 25

        var bad = Curve(Standard);
        bad["source"] = "ram";
        Assert.Equal("err.source", Assert.Throws<UserError>(() => m.Apply(1, bad)).Msg.Key);
    }

    [Fact]
    public void TheChipOnlyFollowsItsOwnSensors()
    {
        var gpu = Curve(Standard);
        gpu["source"] = "gpu";
        Assert.Equal("err.source.chip", Assert.Throws<UserError>(() => Manager().Apply(1, gpu)).Msg.Key);
        Assert.Null(_board.GetCurve(1).Source);
    }
}
