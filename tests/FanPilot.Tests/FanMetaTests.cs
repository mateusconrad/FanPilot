// Item 17: a name for each fan, and pumps protected in every write FanManager makes.
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

public sealed class FanMetaTests : IDisposable
{
    readonly TempDir _dir = new TempDir();
    readonly AppConfig _cfg = new AppConfig();

    public void Dispose() { _dir.Dispose(); }

    // The worst-case board: SYS_FAN5_PUMP and SYS_FAN6_PUMP are headers 6 and 7.
    static FakeBoard Worst() { var b = FakeBoard.Scenario("worst"); b.Animated = false; return b; }

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

    static Dictionary<string, object> Meta(string label = null, string role = null, string policy = null, int? min = null)
    {
        var d = new Dictionary<string, object>();
        if (label != null) d["label"] = label;
        if (role != null) d["role"] = role;
        if (policy != null) d["pumpPolicy"] = policy;
        if (min != null) d["pumpMinPercent"] = min.Value;
        return d;
    }

    static Dictionary<string, object> Fan(FanManager m, int i)
    {
        return (Dictionary<string, object>)((List<object>)m.Describe()["fans"])[i];
    }

    static double[] Pwms(IBoard board, int i) { return board.GetCurve(i).Points.Select(p => p[1]).ToArray(); }

    [Theory]
    [InlineData("SYS_FAN5_PUMP", true)]
    [InlineData("W_PUMP+", true)]
    [InlineData("AIO Pump", true)]
    [InlineData("AIO_PUMP", true)]
    [InlineData("Bomba do watercooler", true)]
    [InlineData("System 1", false)]
    [InlineData("CPU_OPT", false)]
    [InlineData("Chassis Fan #2", false)]
    public void APumpIsRecognizedByItsName(string title, bool pump)
    {
        Assert.Equal(pump, FanMeta.LooksLikePump(title));
    }

    [Fact]
    public void ThePumpHeaderShowsAsAPumpWithoutAnySetup()
    {
        var fan = Fan(Manager(Worst()), 6);
        Assert.Equal("SYS_FAN5_PUMP", fan["title"]);
        Assert.Equal("pump", fan["role"]);
        Assert.Equal("full", fan["pumpPolicy"]);
        Assert.Null(fan["label"]);
        Assert.Equal("fan", Fan(Manager(Worst()), 2)["role"]);
    }

    [Fact]
    public void AFullPumpRunsAt100WhateverIsAsked()
    {
        var board = Worst();
        var m = Manager(board);
        m.Apply(6, Fixed(30));
        Assert.All(Pwms(board, 6), pwm => Assert.Equal(255, pwm));
        m.Apply(6, new Dictionary<string, object> {
            { "mode", "curve" }, { "tempInput", 2 },
            { "points", new List<object> { new object[] { 0, 25 }, new object[] { 40, 25 }, new object[] { 50, 30 }, new object[] { 60, 40 }, new object[] { 65, 50 }, new object[] { 70, 65 }, new object[] { 75, 80 }, new object[] { 85, 100 } } } });
        Assert.All(Pwms(board, 6), pwm => Assert.Equal(255, pwm));
    }

    [Fact]
    public void TurboOnAndOffLeavesAFullPumpAt100()
    {
        var board = Worst();
        var m = Manager(board);
        m.Apply(6, Fixed(30));
        m.SetTurbo(true);
        Assert.All(Pwms(board, 6), pwm => Assert.Equal(255, pwm));
        m.SetTurbo(false);
        Assert.All(Pwms(board, 6), pwm => Assert.Equal(255, pwm));
    }

    [Fact]
    public void APumpWithAMinimumNeverGoesBelowIt()
    {
        var board = Worst();
        var m = Manager(board);
        m.SetMeta(6, Meta(role: "pump", policy: "min", min: 70));
        m.Apply(6, Fixed(30));
        Assert.Equal(179, Pwms(board, 6).First()); // ceil(70% of 255)
        m.Apply(6, Fixed(90));
        Assert.Equal(Math.Round(90 * 2.55), Pwms(board, 6).First()); // above the floor: as asked
    }

    [Fact]
    public void MarkingAHeaderAsAFullPumpPutsItAt100AtOnce()
    {
        var board = Worst();
        var m = Manager(board);
        Assert.False((bool)Fan(m, 2)["managed"]);
        m.SetMeta(2, Meta(role: "pump"));
        Assert.All(Pwms(board, 2), pwm => Assert.Equal(255, pwm));
        Assert.True((bool)Fan(m, 2)["managed"]);
    }

    [Fact]
    public void LoweringThePumpProtectionWritesTheSavedRequestAgain()
    {
        var board = Worst();
        var m = Manager(board);
        m.Apply(6, Fixed(30));                       // full: 100%
        m.SetMeta(6, Meta(role: "pump", policy: "min", min: 60));
        Assert.Equal(153, Pwms(board, 6).First());   // the saved 30% request, raised to the 60% floor
    }

    [Fact]
    public void ANameAloneWritesNothing()
    {
        var board = Worst();
        var m = Manager(board);
        int writes = board.Writes;
        m.SetMeta(2, Meta(label: "Frente do gabinete"));
        Assert.Equal(writes, board.Writes);
        Assert.Equal("Frente do gabinete", Fan(m, 2)["label"]);
        Assert.Equal("SYS_FAN1", Fan(m, 2)["title"]);
    }

    [Fact]
    public void TheNameFollowsTheHeaderAcrossARestartAndAReorder()
    {
        var board = Worst();
        Manager(board).SetMeta(2, Meta(label: "Frente"));
        var moved = board.Headers[2];
        board.Headers.RemoveAt(2);
        board.Headers.Insert(5, moved);
        var restarted = Manager(board);
        Assert.Equal("Frente", Fan(restarted, 5)["label"]);
        Assert.Null(Fan(restarted, 2)["label"]);
    }

    [Fact]
    public void AnEmptyNameGoesBackToTheChipsName()
    {
        var m = Manager(Worst());
        m.SetMeta(2, Meta(label: "Frente"));
        m.SetMeta(2, Meta(label: "   "));
        Assert.Null(Fan(m, 2)["label"]);
    }

    [Theory]
    [InlineData("este nome tem bem mais do que trinta e dois caracteres", null, null, null)]
    [InlineData(null, "bomba", null, null)]
    [InlineData(null, "pump", "metade", null)]
    [InlineData(null, "pump", "min", 30)]
    public void InvalidMetaIsRefused(string label, string role, string policy, int? min)
    {
        var m = Manager(Worst());
        Assert.Throws<UserError>(() => m.SetMeta(2, Meta(label, role, policy, min)));
    }

    [Fact]
    public void TheProtectionCannotChangeDuringTurbo()
    {
        var m = Manager(Worst());
        m.SetTurbo(true);
        var ex = Assert.Throws<UserError>(() => m.SetMeta(2, Meta(role: "pump")));
        Assert.Contains("Turbo", ex.Message);
        m.SetMeta(2, Meta(label: "Frente")); // a name is fine
    }

    [Fact]
    public void RestoringAPumpIsStillAllowed()
    {
        var board = Worst();
        var m = Manager(board);
        var original = board.GetCurve(6).Signature();
        m.Apply(6, Fixed(30));
        m.Restore(6);
        Assert.Equal(original, board.GetCurve(6).Signature());
    }
}
