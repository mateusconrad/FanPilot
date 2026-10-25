// In-memory motherboard for the tests and for running FanPilot without hardware ("--fake-board", Debug builds only,
// with its own data-fake\ folder). By default it behaves like a board whose curves run in the chip (Gigabyte with
// GCC); hardwareCurves: false makes it behave like LibreHardwareMonitor (headers start on the BIOS, Shutdown hands
// them back).
using System;
using System.Collections.Generic;
using System.Linq;

sealed class FakeBoard : IBoard
{
    public sealed class Header
    {
        public string Key;          // stable identity; moving Headers around simulates a reordered board
        public string Title;
        public FanCurve Curve;      // what the "chip" holds right now
        public bool HasTach = true; // like CPU and System 3 on the B760M: nothing with a tachometer plugged in
        public int MaxRpm = 2400;
        public float? StuckRpm;     // a fan that ignores its PWM (wired to a fixed-speed hub, broken)
    }

    readonly object _sync = new object();
    readonly DateTime _started = DateTime.UtcNow;
    public readonly List<Header> Headers = new List<Header>();
    public float[] Temps = { 38, 45, 52, 40, 48, 36 };
    // SetCurve calls the board accepted.
    public int Writes;
    public bool ShutDown;
    // Lossy readback, like hardware that rounds what it stores: applied to every curve the board accepts.
    public Func<FanCurve, FanCurve> Readback;
    // Thrown once, by the next call, like the Gigabyte service going away.
    public Exception NextFailure;
    // Every sensor read (ReadDelayMs) or curve write (WriteDelayMs) takes this long, like the Gigabyte service
    // answering slowly over IPC; the write delay also widens the windows a race would need.
    public int ReadDelayMs, WriteDelayMs;
    // Temperatures drift with time (the --fake-board demo); the tests keep them fixed.
    public bool Animated;

    public FakeBoard(int fans = 4, bool hardwareCurves = true)
    {
        HardwareCurves = hardwareCurves;
        TempInputTitles = new[] { "System 1", "PCH", "CPU", "PCIEX16", "VRM MOS", "System 2" };
        string[] titles = { "CPU", "System 1", "System 2", "System 3", "System 4", "System 5", "System 6", "System 7" };
        for (int i = 0; i < fans; i++)
            Headers.Add(new Header
            {
                Key = "fake/" + i,
                Title = i < titles.Length ? titles[i] : "Fan " + (i + 1),
                Curve = hardwareCurves ? DefaultCurve(i, i % 2 == 0 ? 2 : 0) : BiosCurve(i)
            });
    }

    // The board FanPilot was developed on (B760M GAMING X AX), moving a little so the panel looks alive.
    public static FakeBoard Demo()
    {
        var b = new FakeBoard(4) { Animated = true };
        b.Headers[0].HasTach = false;
        b.Headers[3].HasTach = false;
        return b;
    }

    // 8 points, 25% until 35 °C and 100% from 82 °C.
    public static FanCurve DefaultCurve(int index, int tempInput)
    {
        var c = new FanCurve { Index = index, TempInput = tempInput, Mode = "SmartFanMode", DeltaT = 1 };
        double[][] points = { new[] { 0.0, 64 }, new[] { 35.0, 64 }, new[] { 45.0, 89 }, new[] { 55.0, 140 },
                              new[] { 62.0, 166 }, new[] { 68.0, 191 }, new[] { 74.0, 217 }, new[] { 82.0, 255 } };
        c.Points.AddRange(points);
        return c;
    }

    static FanCurve BiosCurve(int index)
    {
        return new FanCurve { Index = index, Mode = FanCurve.BiosMode, TempInput = 0, DeltaT = 1 };
    }

    public string BackendName { get { return "Placa de teste"; } }
    public bool HardwareCurves { get; private set; }
    public string Notice { get; set; }
    public bool FanControlSupported { get { lock (_sync) return Headers.Count > 0; } }
    public string[] TempInputTitles { get; set; }

    public int CountFans() { lock (_sync) { Fail(); return Headers.Count; } }

    public List<SensorReading> ReadSensors()
    {
        if (ReadDelayMs > 0) System.Threading.Thread.Sleep(ReadDelayMs);
        lock (_sync)
        {
            Fail();
            var temps = CurrentTemps();
            var list = new List<SensorReading>();
            for (int i = 0; i < TempInputTitles.Length; i++)
                list.Add(new SensorReading { Id = "temperature-fake-" + i, Type = "Temperature", Title = TempInputTitles[i], Value = Math.Round(temps[i], 1), Unit = "°C" });
            var rpms = Rpms(temps);
            for (int i = 0; i < Headers.Count; i++)
                list.Add(new SensorReading { Id = "fan-fake-" + i, Type = "Fan", Title = Headers[i].Title, Value = rpms[i], Unit = "RPM" });
            // The panel's tiles look these up by title (see wwwroot/app.js renderTiles).
            list.Add(new SensorReading { Id = "power-fake-0", Type = "Power", Title = "CPU Package", Value = 41.2, Unit = "W" });
            list.Add(new SensorReading { Id = "frequency-fake-0", Type = "Frequency", Title = "CPU", Value = 4600, Unit = "MHz" });
            list.Add(new SensorReading { Id = "voltage-fake-0", Type = "Voltage", Title = "VCore", Value = 1.104, Unit = "V" });
            return list;
        }
    }

    public void ReadFanTach(out List<float> temps, out List<float> rpms)
    {
        lock (_sync)
        {
            Fail();
            var t = CurrentTemps();
            temps = t.ToList();
            rpms = Rpms(t).Select(r => (float)r).ToList();
        }
    }

    float[] CurrentTemps()
    {
        if (!Animated) return Temps.ToArray();
        double s = (DateTime.UtcNow - _started).TotalSeconds;
        return Temps.Select((v, i) => (float)(v + 4 * Math.Sin(s / 20 + i))).ToArray();
    }

    // Duty from the curve at its reference temperature; a header left to the BIOS spins at half speed.
    List<double> Rpms(float[] temps)
    {
        return Headers.Select(h =>
        {
            if (!h.HasTach) return 0.0;
            if (h.StuckRpm.HasValue) return h.StuckRpm.Value;
            double pwm = h.Curve.IsBios ? 128 : h.Curve.PwmAt(h.Curve.TempInput < temps.Length ? temps[h.Curve.TempInput] : 0);
            return Math.Round(h.MaxRpm * pwm / 255);
        }).ToList();
    }

    public FanCurve GetCurve(int index)
    {
        lock (_sync)
        {
            Fail();
            if (index < 0 || index >= Headers.Count) throw new ArgumentException("Fan inexistente.");
            return Copy(Headers[index].Curve, index);
        }
    }

    public List<FanCurve> GetCurves()
    {
        int n = CountFans();
        var list = new List<FanCurve>();
        for (int i = 0; i < n; i++) list.Add(GetCurve(i));
        return list;
    }

    // Same constraints as the real boards: GigabyteBridge refuses curves the chip would silently drop, and only
    // software-curve boards can hand a header back to the BIOS.
    public FanCurve SetCurve(FanCurve curve)
    {
        if (WriteDelayMs > 0) System.Threading.Thread.Sleep(WriteDelayMs);
        lock (_sync)
        {
            Fail();
            if (curve.Index < 0 || curve.Index >= Headers.Count) throw new ArgumentException("Fan inexistente.");
            bool bios = curve.IsBios || curve.Points.Count == 0;
            if (bios && HardwareCurves) throw new ArgumentException("Esta placa não devolve fans à BIOS.");
            if (!bios && (curve.Points.Count < 3 || Math.Round(curve.Points[curve.Points.Count - 1][1]) != 255))
                throw new ArgumentException("A curva precisa de >= 3 pontos e o último ponto em 255.");
            if (curve.TempInput < 0 || curve.TempInput >= TempInputTitles.Length) throw new ArgumentException("Sensor de temperatura inválido.");
            var stored = bios ? BiosCurve(curve.Index) : Copy(curve, curve.Index);
            Headers[curve.Index].Curve = Readback != null && !bios ? Readback(stored) : stored;
            Writes++;
            return Copy(Headers[curve.Index].Curve, curve.Index);
        }
    }

    // What GCC, the BIOS or a resume from sleep would do behind FanPilot's back.
    public void ChangeExternally(int index, FanCurve curve)
    {
        lock (_sync) Headers[index].Curve = Copy(curve, index);
    }

    public void Shutdown()
    {
        lock (_sync)
        {
            ShutDown = true;
            if (!HardwareCurves) for (int i = 0; i < Headers.Count; i++) Headers[i].Curve = BiosCurve(i);
        }
    }

    void Fail()
    {
        if (NextFailure == null) return;
        var ex = NextFailure;
        NextFailure = null;
        throw ex;
    }

    FanCurve Copy(FanCurve c, int index)
    {
        var copy = FanCurve.FromDictionary(c.ToDictionary());
        copy.Index = index;
        copy.Key = Headers[index].Key;
        copy.Title = Headers[index].Title;
        return copy;
    }
}
