// The curves FanPilot runs itself (LibreHardwareMonitor boards: the chip keeps no curve). Once a second it turns each
// header's reference temperature into a speed, smoothed so that a CPU whose temperature jumps a few degrees with every
// burst of load does not make the fan hunt up and down:
//  - the temperature is averaged (exponential moving average, time constant Tau);
//  - the speed only comes down once the temperature has fallen Hysteresis degrees below what justified it;
//  - the speed moves at most RisePerSecond up and FallPerSecond down.
// Safety comes before all that: the raw temperature at or past the curve's last point (full speed) means 100% at once,
// and a missing or impossible reading hands the header back to the BIOS. A curve without a response (a fixed speed,
// Turbo) is followed as it is, with only the BIOS rule. No hardware here: LhmBoard reads the sensors and writes what
// this returns, and the tests feed it temperature sequences.
using System;
using System.Collections.Generic;

sealed class SoftwareCurveEngine
{
    // Readings outside this range come from open thermistor inputs (Nuvoton/ITE report 127, -128, 255...).
    public const double MinPlausibleTemp = -40, MaxPlausibleTemp = 125;
    // Curves saved before responses existed get it too.
    public const string DefaultResponse = "balanced";

    public sealed class Response
    {
        public readonly double Tau, Hysteresis, RisePerSecond, FallPerSecond;

        public Response(double tau, double hysteresis, double risePerSecond, double fallPerSecond)
        {
            Tau = tau; Hysteresis = hysteresis; RisePerSecond = risePerSecond; FallPerSecond = fallPerSecond;
        }
    }

    // The panel's "Resposta" (seconds, °C, % per second). Starting values, to be tuned on hardware.
    public static readonly Dictionary<string, Response> Responses = new Dictionary<string, Response> {
        { "fast", new Response(1, 1, 50, 10) },
        { "balanced", new Response(4, 2, 15, 3) },
        { "smooth", new Response(10, 4, 8, 1) },
    };

    sealed class State
    {
        public double Smoothed, Percent;
    }

    readonly Dictionary<int, State> _states = new Dictionary<int, State>();

    // A curve's temperature sources (item 25; FanCurve.Source). Sensor is stored as null: the curves saved before.
    public const string SensorSource = "sensor", GpuSource = "gpu", MaxSource = "max";

    public static bool Plausible(double t) { return !double.IsNaN(t) && t >= MinPlausibleTemp && t <= MaxPlausibleTemp; }

    // The raw temperature a curve follows, before Next smooths it: its board sensor (TempInput), the GPU, or the higher
    // of the two that read now. NaN when none reads: the header goes back to the BIOS.
    public static double SourceTemp(FanCurve curve, IList<float> boardTemps, double? gpu)
    {
        double board = curve.TempInput >= 0 && curve.TempInput < boardTemps.Count ? boardTemps[curve.TempInput] : double.NaN;
        double g = gpu ?? double.NaN;
        if (curve.Source == GpuSource) return g;
        if (curve.Source != MaxSource) return board;
        bool hasBoard = Plausible(board), hasGpu = Plausible(g);
        return hasBoard && hasGpu ? Math.Max(board, g) : hasBoard ? board : hasGpu ? g : double.NaN;
    }

    // The duty (0..100%) for this header now, or null: hand it back to the BIOS. dt: seconds since the last step.
    public double? Next(int header, FanCurve curve, double rawTemp, double dt)
    {
        Response r;
        if (!Plausible(rawTemp) || curve.Response == null || !Responses.TryGetValue(curve.Response, out r))
        {
            _states.Remove(header);
            return Plausible(rawTemp) ? Percent(curve, rawTemp) : (double?)null;
        }
        State s;
        if (!_states.TryGetValue(header, out s))
        {
            // A new curve, or a sensor back from a gap, starts where the curve says: there is nothing to ramp from.
            _states[header] = s = new State { Smoothed = rawTemp, Percent = Percent(curve, rawTemp) };
        }
        else
        {
            dt = Math.Max(0, dt);
            s.Smoothed += (rawTemp - s.Smoothed) * (1 - Math.Exp(-dt / r.Tau));
            double target = Percent(curve, s.Smoothed);
            if (target < s.Percent) target = Math.Min(s.Percent, Percent(curve, s.Smoothed + r.Hysteresis));
            s.Percent = target > s.Percent ? Math.Min(target, s.Percent + r.RisePerSecond * dt)
                                           : Math.Max(target, s.Percent - r.FallPerSecond * dt);
        }
        if (rawTemp >= curve.Points[curve.Points.Count - 1][0]) s.Percent = 100;
        return s.Percent;
    }

    // The next step starts over from the curve: a curve was set, or the header went back to the BIOS.
    public void Reset(int header) { _states.Remove(header); }

    static double Percent(FanCurve curve, double t) { return curve.PwmAt(t) / 2.55; }
}
