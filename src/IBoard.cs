// What FanManager and Sampler need from a motherboard, whichever way it is reached:
//  - GigabyteBridge: Gigabyte Control Center's service; curves are written to and executed by the Super I/O chip.
//  - LhmBoard: LibreHardwareMonitor (any vendor with a supported chip); the chip only takes a duty cycle, so
//    FanPilot executes the curves itself and hands the fans back to the BIOS whenever it stops.
using System;
using System.Collections.Generic;
using System.Linq;

interface IBoard
{
    // Shown in the panel and the log, e.g. "Gigabyte Control Center".
    string BackendName { get; }
    // true when curves keep running in hardware after FanPilot exits.
    bool HardwareCurves { get; }
    // Why fan control or sensors are unavailable (driver missing, chip unsupported), or null.
    string Notice { get; }
    bool FanControlSupported { get; }
    string[] TempInputTitles { get; }

    int CountFans();
    List<SensorReading> ReadSensors();
    // Index-aligned with TempInputTitles / fan controls.
    void ReadFanTach(out List<float> temps, out List<float> rpms);
    FanCurve GetCurve(int index);
    List<FanCurve> GetCurves();
    // Applies a curve and returns what the board reports afterwards. A curve with Mode == FanCurve.BiosMode hands
    // the header back to the BIOS (software-curve boards only).
    FanCurve SetCurve(FanCurve curve);
    // Called on every exit path FanPilot can intercept.
    void Shutdown();
    // What the board did for safety and is still doing right now (a header back on the BIOS because its reference
    // sensor stopped reading, every header back after repeated failures), one message each; empty when all is well.
    // The alerts (item 16) show them.
    List<string> ControlProblems();
}

sealed class SensorReading
{
    public string Id;
    public string Type;
    public string Title;
    public double Value;
    public string Unit;
}

sealed class FanCurve
{
    // Software-curve boards: the header is left to the BIOS (no points).
    public const string BiosMode = "BiosDefault";

    public int Index;
    // Stable identity of the header, unlike Index (its position this session): what saved state is keyed by.
    public string Key;
    public string Title;
    public int TempInput;
    // "SmartFanMode" (linear between points) or "StairStepMode" (steps); other values come from hardware only.
    public string Mode;
    public int DeltaT;
    // [temperature C, pwm 0..255]
    public List<double[]> Points = new List<double[]>();
    // How a curve FanPilot runs itself follows the temperature: a SoftwareCurveEngine.Responses key, or null to
    // follow it as it is (fixed speed, Turbo). Boards that keep the curve in the chip ignore it.
    public string Response;

    public bool IsBios { get { return Mode == BiosMode; } }

    // PWM (0..255) for temperature t: linear between points, or held until the next point in StairStepMode.
    // Same rule as curveAt() in wwwroot/js/curve.mjs; tests/fixtures/curves.json is checked against both.
    public double PwmAt(double t)
    {
        if (Points.Count == 0) return 255;
        if (t <= Points[0][0]) return Points[0][1];
        for (int i = 1; i < Points.Count; i++)
        {
            double x0 = Points[i - 1][0], y0 = Points[i - 1][1], x1 = Points[i][0], y1 = Points[i][1];
            if (t < x1) return Mode == "StairStepMode" ? y0 : y0 + (y1 - y0) * (t - x0) / (x1 - x0);
        }
        return Points[Points.Count - 1][1];
    }

    public string Signature()
    {
        return (IsBios ? "bios|" : "") + TempInput + "|" + string.Join(";", Points.Select(p => (int)Math.Round(p[0]) + "," + (int)Math.Round(p[1])));
    }

    public Dictionary<string, object> ToDictionary()
    {
        return new Dictionary<string, object> {
            { "index", Index }, { "key", Key }, { "title", Title }, { "tempInput", TempInput }, { "mode", Mode },
            { "deltaT", DeltaT }, { "points", Points.Select(p => new[] { p[0], p[1] }).ToList() }, { "response", Response }
        };
    }

    public static FanCurve FromDictionary(Dictionary<string, object> d)
    {
        var c = new FanCurve();
        c.Index = Json.GetInt(d, "index", 0);
        c.Key = Json.GetString(d, "key", null);
        c.Title = Json.GetString(d, "title", "");
        c.TempInput = Json.GetInt(d, "tempInput", 0);
        c.Mode = Json.GetString(d, "mode", "SmartFanMode");
        c.DeltaT = Json.GetInt(d, "deltaT", 1);
        c.Response = Json.GetString(d, "response", null);
        foreach (object p in Json.ToList(Json.Get(d, "points")))
        {
            var xy = Json.ToList(p);
            c.Points.Add(new[] { Json.ToDouble(xy[0]), Json.ToDouble(xy[1]) });
        }
        return c;
    }
}
