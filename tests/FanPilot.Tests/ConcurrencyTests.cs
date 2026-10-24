// Item 07: Turbo toggles and fan edits arriving at the same time (two taps, two phones) never leave a mixed state.
// The fake board takes a few milliseconds per write, so an interleaving would have room to happen.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

public sealed class ConcurrencyTests : IDisposable
{
    readonly TempDir _dir = new TempDir();
    readonly AppConfig _cfg = new AppConfig();

    public void Dispose() { _dir.Dispose(); }

    static Dictionary<string, object> Fixed(int percent)
    {
        return new Dictionary<string, object> { { "mode", "fixed" }, { "percent", percent } };
    }

    [Fact]
    public void ConcurrentTurboTogglesEndWithExactlyThePreviousSettings()
    {
        var board = new FakeBoard(4) { WriteDelayMs = 3 };
        var m = new FanManager(board, _cfg, _dir.Path);
        m.Describe();
        m.Apply(1, Fixed(40));
        string managed = board.GetCurve(1).Signature(), unmanaged = board.GetCurve(2).Signature();

        Parallel.For(0, 10, i => m.SetTurbo(i % 2 == 0));
        m.SetTurbo(false);

        Assert.False(m.TurboActive);
        Assert.Equal(managed, board.GetCurve(1).Signature());
        Assert.Equal(unmanaged, board.GetCurve(2).Signature());
        var fans = ((List<object>)m.Describe()["fans"]).Cast<Dictionary<string, object>>().ToList();
        Assert.Equal(40, Json.GetInt((Dictionary<string, object>)fans[1]["request"], "percent", 0));
        Assert.False((bool)fans[2]["managed"]);
    }

    [Fact]
    public void AnEditRacingTheTurboIsEitherBeforeItOrRefused()
    {
        var board = new FakeBoard(4) { WriteDelayMs = 3 };
        var m = new FanManager(board, _cfg, _dir.Path);
        m.Describe();
        m.Apply(1, Fixed(40));

        var turbo = Task.Run(() => m.SetTurbo(true));
        var edit = Task.Run(() =>
        {
            try { m.Apply(1, Fixed(60)); return true; }
            catch (ArgumentException) { return false; } // "Desligue o Turbo…"
        });
        Task.WaitAll(turbo, edit);
        Assert.All(board.Headers, h => Assert.All(h.Curve.Points, p => Assert.Equal(255, p[1]))); // Turbo is fully on
        m.SetTurbo(false);

        // Whatever won, what the board runs now is what FanPilot says it asked for.
        var fan = ((List<object>)m.Describe()["fans"]).Cast<Dictionary<string, object>>().ToList()[1];
        int percent = Json.GetInt((Dictionary<string, object>)fan["request"], "percent", 0);
        Assert.Equal(edit.Result ? 60 : 40, percent);
        Assert.Equal(Math.Round(percent * 2.55), board.GetCurve(1).Points[0][1]);
    }
}
