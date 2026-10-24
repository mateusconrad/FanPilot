// Item 04: saved state follows each header's stable key. A reordered header list keeps every curve on its own fan;
// a header that disappeared leaves an orphan that is never applied; version 1 files (keyed by position) migrate.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

public sealed class HeaderKeyTests : IDisposable
{
    readonly TempDir _dir = new TempDir();
    readonly AppConfig _cfg = new AppConfig();

    public void Dispose() { _dir.Dispose(); }

    static Dictionary<string, object> Fixed(int percent)
    {
        return new Dictionary<string, object> { { "mode", "fixed" }, { "percent", percent } };
    }

    FanManager Manager(FakeBoard board)
    {
        var m = new FanManager(board, _cfg, _dir.Path);
        m.Describe();
        return m;
    }

    static List<Dictionary<string, object>> Fans(FanManager m) { return ((List<object>)m.Describe()["fans"]).Cast<Dictionary<string, object>>().ToList(); }
    static List<Dictionary<string, object>> Orphans(FanManager m) { return ((List<object>)m.Describe()["orphans"]).Cast<Dictionary<string, object>>().ToList(); }

    static double FlatPwm(FakeBoard board, string key) { return board.Headers.Single(h => h.Key == key).Curve.Points[0][1]; }

    // LibreHardwareMonitor boards start on the BIOS and FanPilot puts the curves back: each must land on its own header.
    [Fact]
    public void ReorderedHeadersKeepTheirOwnCurves()
    {
        var board = new FakeBoard(4, hardwareCurves: false);
        var m = Manager(board);
        m.Apply(1, Fixed(40));
        m.Apply(2, Fixed(60));
        board.Shutdown();
        var moved = board.Headers[1];        // an update lists "System 1" and "System 2" the other way round
        board.Headers[1] = board.Headers[2];
        board.Headers[2] = moved;

        new FanManager(board, _cfg, _dir.Path).ReapplyDesired();
        Assert.Equal(102, FlatPwm(board, "fake/1"));             // 40%
        Assert.Equal(Math.Round(60 * 2.55), FlatPwm(board, "fake/2"));
        var fans = Fans(new FanManager(board, _cfg, _dir.Path));
        Assert.Equal("fake/2", fans[1]["key"]);
        Assert.Equal(60, Json.GetInt((Dictionary<string, object>)fans[1]["request"], "percent", 0));
    }

    [Fact]
    public void ACurveForAHeaderThatDisappearedIsNeverAppliedElsewhere()
    {
        var board = new FakeBoard(4, hardwareCurves: false);
        var m = Manager(board);
        m.Apply(2, Fixed(40));
        board.Shutdown();
        board.Headers.RemoveAt(2); // the chip no longer reports "System 2"

        var restarted = new FanManager(board, _cfg, _dir.Path);
        int writes = board.Writes;
        restarted.ReapplyDesired();
        restarted.Watchdog();
        Assert.Equal(writes, board.Writes);
        Assert.All(board.Headers, h => Assert.True(h.Curve.IsBios));
        var orphan = Orphans(restarted).Single();
        Assert.Equal("fake/2", orphan["key"]);
        Assert.Equal("System 2", orphan["title"]);
    }

    [Fact]
    public void OrphansCanBeDiscardedButExistingFansCannot()
    {
        var board = new FakeBoard(4);
        Manager(board).Apply(2, Fixed(40));
        board.Headers.RemoveAt(2);
        var m = Manager(board);
        Assert.Throws<ArgumentException>(() => m.DiscardOrphan("fake/1"));
        m.DiscardOrphan("fake/2");
        Assert.Empty(Orphans(m));
        Assert.Empty(Orphans(Manager(board))); // and it stays discarded
    }

    [Fact]
    public void TurboRemembersEachFanByKey()
    {
        var board = new FakeBoard(4, hardwareCurves: false);
        var m = Manager(board);
        m.Apply(1, Fixed(40));
        m.SetTurbo(true);
        board.Shutdown();
        var moved = board.Headers[1];
        board.Headers[1] = board.Headers[2];
        board.Headers[2] = moved;

        var restarted = Manager(board);
        restarted.SetTurbo(false);
        Assert.Equal(102, FlatPwm(board, "fake/1"));
        Assert.True(board.Headers.Single(h => h.Key == "fake/2").Curve.IsBios);
    }

    // Shaped like the files FanPilot wrote before the keys (version 1, keyed by position), from the B760M.
    static string V1Desired(string titleOfFan1)
    {
        return "{\"1\":{\"request\":{\"mode\":\"fixed\",\"percent\":40},\"curve\":{\"index\":1,\"title\":\"" + titleOfFan1 + "\",\"tempInput\":0," +
               "\"mode\":\"SmartFanMode\",\"deltaT\":1,\"points\":[[0,102],[20,102],[30,102],[40,102],[50,102],[60,102],[70,102],[85,255]]}," +
               "\"signature\":\"0|0,102;20,102;30,102;40,102;50,102;60,102;70,102;85,255\"}}";
    }

    const string V1Backup = "{\"1\":{\"index\":1,\"title\":\"System 1\",\"tempInput\":0,\"mode\":\"StairStepMode\",\"deltaT\":1," +
                            "\"points\":[[0,255],[20,255],[30,255],[40,255],[45,255],[50,255],[55,255],[60,255]]}}";

    [Fact]
    public void VersionOneFilesMigrateWhenTheTitlesMatch()
    {
        File.WriteAllText(_dir.File("desired-curves.json"), V1Desired("System 1"));
        File.WriteAllText(_dir.File("backup-curves.json"), V1Backup);
        File.WriteAllText(_dir.File("turbo.json"), "{\"active\":false,\"prev\":{}}");
        var m = Manager(new FakeBoard(4));

        var fan = Fans(m)[1];
        Assert.True((bool)fan["managed"]);
        Assert.True((bool)fan["hasBackup"]);
        Assert.Empty(Orphans(m));
        Assert.Equal(2, Json.GetInt(Json.ParseObject(File.ReadAllText(_dir.File("desired-curves.json"))), "version", 0));
        Assert.Equal(V1Desired("System 1"), File.ReadAllText(_dir.File("desired-curves.json.v1")));
        Assert.True(File.Exists(_dir.File("backup-curves.json.v1")));
        Assert.True(File.Exists(_dir.File("turbo.json.v1")));
    }

    [Fact]
    public void VersionOneEntryForAnotherHeaderBecomesAnOrphan()
    {
        File.WriteAllText(_dir.File("desired-curves.json"), V1Desired("SYS_FAN5_PUMP")); // position 1 is not that fan now
        var board = new FakeBoard(4);
        var m = Manager(board);
        Assert.False((bool)Fans(m)[1]["managed"]);
        Assert.Equal("SYS_FAN5_PUMP", Orphans(m).Single()["title"]);
        m.Watchdog();
        Assert.Equal(0, board.Writes);
    }

    [Fact]
    public void ApiListsTheKeyOfEveryFan()
    {
        var fans = Fans(Manager(new FakeBoard(4)));
        Assert.Equal(new[] { "fake/0", "fake/1", "fake/2", "fake/3" }, fans.Select(f => (string)f["key"]));
    }
}
