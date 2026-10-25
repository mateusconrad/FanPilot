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
        public double? StopsAfterSeconds; // a fan that seizes this long after the board opened (the "stall" scenario)
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
    // Set: every call fails with this message, like the Gigabyte service stopped (GigabyteBridge reports it so).
    public string DownReason;
    // Every sensor read (ReadDelayMs) or curve write (WriteDelayMs) takes this long, like the Gigabyte service
    // answering slowly over IPC; the write delay also widens the windows a race would need.
    public int ReadDelayMs, WriteDelayMs;
    // Temperatures drift with time (the --fake-board demo); the tests keep them fixed.
    public bool Animated;
    // The cores this many degrees above the "CPU" sensor, as LibreHardwareMonitor reads them next to Gigabyte Control
    // Center when PawnIO is installed (item 19); null: no such reading.
    public double? PackageAbove;

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

    // The boards the panel has to cope with, as tools/ui-mock shows them: "--fake-board=<name>" picks one, and the
    // end-to-end tests (tests/e2e) run on each.
    public static readonly string[] Scenarios = { "demo", "worst", "unsupported", "down", "lhm", "slow", "stall" };

    public static FakeBoard Scenario(string name)
    {
        switch (name)
        {
            case "demo": return Demo();
            case "worst": return Worst();
            // Monitoring only: no header FanPilot can control.
            case "unsupported": return new FakeBoard(0) { Animated = true };
            // What Windows says when the GCC's service is down (in the PC's language), wrapped as GigabyteBridge does.
            case "down": return new FakeBoard(4) { DownReason = "Falha ao se conectar a uma porta de IPC" };
            // LibreHardwareMonitor: FanPilot runs the curves, and every header starts on the BIOS.
            case "lhm": return new FakeBoard(4, hardwareCurves: false) { Animated = true };
            // The demo board taking 4 s to open, as the Gigabyte service can: a panel meets FanPilot still starting.
            case "slow":
                System.Threading.Thread.Sleep(4000);
                return Demo();
            // The demo board whose System 2 seizes after 35 s of spinning: the stopped-fan alert (item 16).
            case "stall":
                var stall = Demo();
                stall.Headers[2].StopsAfterSeconds = 35;
                return stall;
            default: throw new ArgumentException("Cenário da placa de teste desconhecido: " + name);
        }
    }

    // The board FanPilot was developed on (B760M GAMING X AX), moving a little so the panel looks alive.
    public static FakeBoard Demo()
    {
        var b = new FakeBoard(4) { Animated = true, PackageAbove = 14 };
        b.Headers[0].HasTach = false;
        b.Headers[3].HasTach = false;
        return b;
    }

    // A big board under load: 8 headers (two pumps at full speed, one with a long name), a disconnected thermistor
    // (127 and -128 °C are what an open input reads) and values at the top of their ranges.
    static FakeBoard Worst()
    {
        var b = new FakeBoard(8) { Animated = true };
        string[] titles = { "CPU_FAN", "CPU_OPT", "SYS_FAN1", "SYS_FAN2", "SYS_FAN3", "SYS_FAN4", "SYS_FAN5_PUMP",
                            "SYS_FAN6_PUMP · bomba do watercooler ligada no conector de 4 pinos" };
        for (int i = 0; i < titles.Length; i++) b.Headers[i].Title = titles[i];
        b.TempInputTitles = new[] { "System 1", "PCH", "CPU", "PCIEX16", "VRM MOS", "System 2", "PCIEX8", "EC_TEMP1", "EC_TEMP2" };
        b.Temps = new float[] { 41, 71.5f, 98, 62, 104, 127, -128, 55, 0 };
        b.Headers[1].Curve = FullSpeed(1, 2, "StairStepMode");
        b.Headers[3].HasTach = false;
        b.Headers[4].MaxRpm = 7300;
        b.Headers[6].Curve = FullSpeed(6, 2, "SmartFanMode");
        b.Headers[7].Curve = FullSpeed(7, 2, "SmartFanMode");
        b.Headers[7].StuckRpm = 4815;
        return b;
    }

    static FanCurve FullSpeed(int index, int tempInput, string mode)
    {
        var c = new FanCurve { Index = index, TempInput = tempInput, Mode = mode, DeltaT = 1 };
        foreach (double t in new double[] { 0, 20, 30, 40, 50, 60, 70, 75 }) c.Points.Add(new[] { t, 255.0 });
        return c;
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

    // What ControlProblems() reports; the tests set it.
    public readonly List<Msg> Problems = new List<Msg>();

    public List<Msg> ControlProblems() { lock (_sync) return Problems.ToList(); }

    public string BackendName { get { return "Test board"; } }
    public bool HardwareCurves { get; private set; }
    public Msg Notice { get; set; }
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
            int cpu = Array.IndexOf(TempInputTitles, "CPU");
            double? package = PackageAbove.HasValue && cpu >= 0 ? Math.Round(temps[cpu] + PackageAbove.Value, 1) : (double?)null;
            CpuRoles.Assign(list, package, HardwareCurves, new Msg("cpu.hint.pawnio"));
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
        // A disconnected input keeps reading the same impossible value.
        return Temps.Select((v, i) => v > -100 && v < 120 ? (float)(v + 4 * Math.Sin(s / 20 + i)) : v).ToArray();
    }

    // Duty from the curve at its reference temperature; a header left to the BIOS spins at half speed.
    List<double> Rpms(float[] temps)
    {
        return Headers.Select(h =>
        {
            if (!h.HasTach) return 0.0;
            if (h.StuckRpm.HasValue) return h.StuckRpm.Value;
            if (h.StopsAfterSeconds.HasValue && (DateTime.UtcNow - _started).TotalSeconds >= h.StopsAfterSeconds.Value) return 0.0;
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
        if (DownReason != null) throw new Unavailable(new Msg("board.serviceDown", "service", "EasyTuneEngineService", "detail", DownReason), null);
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
