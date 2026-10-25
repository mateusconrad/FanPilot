// Item 15: the curves FanPilot runs itself, fed one temperature a second, as LhmBoard does.
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

public class SoftwareCurveTests
{
    // The panel's "Padrão" preset, as FanManager stores it: [°C, PWM 0..255], full speed from 82 °C.
    static FanCurve Standard(string response)
    {
        var c = new FanCurve { Index = 0, TempInput = 0, Mode = "SmartFanMode", DeltaT = 1, Response = response };
        int[] tp = { 0, 30, 35, 30, 45, 40, 55, 55, 62, 65, 68, 75, 74, 85, 82, 100 };
        for (int i = 0; i < tp.Length; i += 2) c.Points.Add(new[] { (double)tp[i], i == tp.Length - 2 ? 255 : Math.Round(tp[i + 1] * 2.55) });
        return c;
    }

    static double Percent(FanCurve c, double t) { return c.PwmAt(t) / 2.55; }

    // One step a second at each temperature; returns what the engine wrote at every step.
    static List<double> Run(SoftwareCurveEngine engine, FanCurve curve, IEnumerable<double> temps)
    {
        return temps.Select(t => engine.Next(0, curve, t, 1).Value).ToList();
    }

    static IEnumerable<double> Hold(double t, int seconds) { return Enumerable.Repeat(t, seconds); }

    [Fact]
    public void OscillatingAroundAPointChangesTheSpeedAtMostTwiceAMinute()
    {
        var engine = new SoftwareCurveEngine();
        var curve = Standard("balanced");
        Run(engine, curve, Hold(55, 60));
        // ±1 °C, 5 s each way: a CPU with bursts of load.
        var square = Enumerable.Range(0, 60).Select(s => s / 5 % 2 == 0 ? 56.0 : 54.0);
        var written = Run(engine, curve, square).Select(p => Math.Round(p)).ToList();
        int changes = written.Zip(written.Skip(1), (a, b) => a != b ? 1 : 0).Sum();
        Assert.True(changes <= 2, changes + " mudanças de velocidade em 60 s: " + string.Join(" ", written));
    }

    [Fact]
    public void WithoutAResponseTheSameOscillationFollowsEveryDegree()
    {
        // What item 15 changes: a fixed speed (or the old behavior) follows the raw temperature.
        var engine = new SoftwareCurveEngine();
        var curve = Standard(null);
        var written = Run(engine, curve, Enumerable.Range(0, 60).Select(s => s / 5 % 2 == 0 ? 56.0 : 54.0)).Select(p => Math.Round(p)).ToList();
        Assert.True(written.Zip(written.Skip(1), (a, b) => a != b ? 1 : 0).Sum() >= 10);
    }

    [Fact]
    public void AJumpRisesNoFasterThanTheRampAndTheLastPointIsFullSpeedAtOnce()
    {
        var engine = new SoftwareCurveEngine();
        var curve = Standard("balanced");
        var rise = SoftwareCurveEngine.Responses["balanced"].RisePerSecond;
        double before = Run(engine, curve, Hold(40, 60)).Last();
        var climb = Run(engine, curve, Hold(80, 40));
        Assert.All(climb.Zip(new[] { before }.Concat(climb), (now, prev) => now - prev), step => Assert.InRange(step, 0, rise + 1e-9));
        Assert.Equal(Percent(curve, 80), climb.Last(), 1);
        Assert.True(climb.Last() < 100);
        // 83 °C is past the last point (82 °C): the raw reading wins over the average and the ramp.
        Assert.Equal(100, engine.Next(0, curve, 83, 1).Value);
    }

    [Fact]
    public void TheRampLimitsASteepCurve()
    {
        // 20% up to 50 °C, 90% from 51 °C: once the average crosses 51 °C the curve asks for +70% in one step.
        var curve = new FanCurve { Index = 0, TempInput = 0, Mode = "SmartFanMode", DeltaT = 1, Response = "balanced" };
        foreach (var p in new[] { new[] { 0.0, 51 }, new[] { 50.0, 51 }, new[] { 51.0, 230 }, new[] { 85.0, 255 } }) curve.Points.Add(p);
        var engine = new SoftwareCurveEngine();
        double before = Run(engine, curve, Hold(45, 60)).Last();
        var climb = Run(engine, curve, Hold(60, 30));
        var steps = climb.Zip(new[] { before }.Concat(climb), (now, prev) => now - prev).ToList();
        double rise = SoftwareCurveEngine.Responses["balanced"].RisePerSecond;
        Assert.All(steps, step => Assert.True(step <= rise + 1e-9, "subiu " + step + " %/s"));
        Assert.Contains(steps, step => Math.Abs(step - rise) < 1e-9); // the ramp did bind
    }

    [Fact]
    public void CoolingByLessThanTheHysteresisKeepsTheSpeed()
    {
        var engine = new SoftwareCurveEngine();
        var curve = Standard("balanced");
        double at60 = Run(engine, curve, Hold(60, 60)).Last();
        Assert.All(Run(engine, curve, Hold(59, 60)), p => Assert.Equal(at60, p, 6));
    }

    [Fact]
    public void CoolingDownSlowsTheFanGraduallyAndOnlyPastTheHysteresis()
    {
        var engine = new SoftwareCurveEngine();
        var curve = Standard("balanced");
        var balanced = SoftwareCurveEngine.Responses["balanced"];
        double at60 = Run(engine, curve, Hold(60, 60)).Last();
        var fall = Run(engine, curve, Hold(50, 120));
        Assert.All(fall.Zip(new[] { at60 }.Concat(fall), (now, prev) => prev - now), step => Assert.InRange(step, 0, balanced.FallPerSecond + 1e-9));
        // Settles where the curve would be H degrees warmer: 52 °C, not 50 °C.
        Assert.Equal(Percent(curve, 50 + balanced.Hysteresis), fall.Last(), 1);
        Assert.True(fall.Last() > Percent(curve, 50));
    }

    [Fact]
    public void AnImpossibleOrMissingReadingHandsTheHeaderBackToTheBios()
    {
        var engine = new SoftwareCurveEngine();
        var curve = Standard("balanced");
        Run(engine, curve, Hold(60, 30));
        Assert.Null(engine.Next(0, curve, double.NaN, 1));
        Assert.Null(engine.Next(0, curve, 127, 1));
        Assert.Null(engine.Next(0, curve, -128, 1));
        // Back from the gap: straight to the curve, no ramp from where it was.
        Assert.Equal(Percent(curve, 45), engine.Next(0, curve, 45, 1).Value, 6);
    }

    [Fact]
    public void AFixedSpeedFollowsItsCurveAtOnce()
    {
        var engine = new SoftwareCurveEngine();
        var curve = Standard(null);
        Assert.Equal(Percent(curve, 40), engine.Next(0, curve, 40, 1).Value, 6);
        Assert.Equal(Percent(curve, 70), engine.Next(0, curve, 70, 1).Value, 6);
    }

    [Fact]
    public void ANewCurveStartsFromTheCurveNotFromARamp()
    {
        var engine = new SoftwareCurveEngine();
        var curve = Standard("smooth");
        Run(engine, curve, Hold(40, 30));
        engine.Reset(0);
        Assert.Equal(Percent(curve, 70), engine.Next(0, curve, 70, 1).Value, 6);
    }

    [Fact]
    public void HeadersAreIndependent()
    {
        var engine = new SoftwareCurveEngine();
        var curve = Standard("balanced");
        engine.Next(0, curve, 40, 1);
        Assert.Equal(Percent(curve, 70), engine.Next(1, curve, 70, 1).Value, 6);
    }

    // Item 25: a curve can follow the GPU, or the higher of its sensor and the GPU.
    static FanCurve WithSource(string source)
    {
        var c = Standard("balanced");
        c.TempInput = 1;
        c.Source = source;
        return c;
    }

    [Fact]
    public void TheSourcePicksTheTemperature()
    {
        var board = new List<float> { 40, 52, 33 };
        Assert.Equal(52, SoftwareCurveEngine.SourceTemp(WithSource(null), board, 75));
        Assert.Equal(75, SoftwareCurveEngine.SourceTemp(WithSource("gpu"), board, 75));
        Assert.Equal(75, SoftwareCurveEngine.SourceTemp(WithSource("max"), board, 75));
        Assert.Equal(52, SoftwareCurveEngine.SourceTemp(WithSource("max"), board, 45));
    }

    // GPU without a reading (its driver restarting): "max" goes on with the sensor, "gpu" hands the fan to the BIOS.
    [Fact]
    public void AMissingReadingFollowsTheRules()
    {
        var board = new List<float> { 40, 52, 33 };
        Assert.Equal(52, SoftwareCurveEngine.SourceTemp(WithSource("max"), board, null));
        Assert.True(double.IsNaN(SoftwareCurveEngine.SourceTemp(WithSource("gpu"), board, null)));
        Assert.Null(new SoftwareCurveEngine().Next(0, WithSource("gpu"), SoftwareCurveEngine.SourceTemp(WithSource("gpu"), board, null), 1));
        // An open thermistor (127 °C) counts as no reading, so "max" does not race to 100% because of it.
        var open = new List<float> { 40, 127, 33 };
        Assert.Equal(61, SoftwareCurveEngine.SourceTemp(WithSource("max"), open, 61));
        Assert.True(double.IsNaN(SoftwareCurveEngine.SourceTemp(WithSource("max"), open, null)));
    }

    [Fact]
    public void ACaseFanOnTheHigherOfCpuAndGpuSpeedsUpWithTheGpuHotAndTheCpuCold()
    {
        var engine = new SoftwareCurveEngine();
        var curve = WithSource("max");
        var cold = new List<float> { 0, 38, 0 };
        double idle = engine.Next(0, curve, SoftwareCurveEngine.SourceTemp(curve, cold, 40), 1).Value;
        double gaming = idle;
        for (int s = 0; s < 60; s++) gaming = engine.Next(0, curve, SoftwareCurveEngine.SourceTemp(curve, cold, 76), 1).Value;
        Assert.Equal(Math.Round(Percent(curve, 40)), Math.Round(idle));
        Assert.Equal(Math.Round(Percent(curve, 76)), Math.Round(gaming));
    }
}
