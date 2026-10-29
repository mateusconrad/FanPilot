// Motherboard access on Linux (item 28b) through the kernel's hwmon drivers (nct6775, it87, asus_ec_sensors…): the
// sensors and fans of every motherboard chip in /sys/class/hwmon, and the CPU's temperature from k10temp or coretemp.
// Needs root to write.
//
// A header is taken by writing pwmN_enable = 1 (manual) and then a duty in pwmN (0..255); the value pwmN_enable had
// before is what "back to the BIOS" writes again. The chip keeps no curve, so, as on LhmBoard, this class runs the
// curves: once a second it reads the sensors, asks SoftwareCurveEngine for each managed header's duty and writes it.
// Safety, in order of what can go wrong:
//  - reference temperature missing or implausible -> that header goes back to the BIOS until it reads again;
//  - reading or writing the chip fails repeatedly -> every header goes back to the BIOS;
//  - FanPilot exits (Ctrl+C, SIGTERM, an unhandled exception) -> Shutdown() returns all to the BIOS;
//  - FanPilot is killed (kill -9, a crash of the runtime): the original pwmN_enable values are also in
//    /run/fanpilot/hwmon-restore.json; the next FanPilot, or "FanPilot --restore-fans" (the service's ExecStopPost),
//    writes them back. /run is emptied on every boot, as the chip is reset.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

sealed class HwmonBoard : IBoard, IFollowsGpu, IReportsDuty
{
    const int FailuresBeforeRelease = 5;
    // pwmN_enable: 1 is manual on every hwmon driver; what the automatic modes are numbered varies by chip.
    const int Manual = 1;

    sealed class Header
    {
        public HwmonFiles.Chip Chip;
        public int N;
        public string Title;
        public string Key;      // chip and pwm number, e.g. hwmon/nct6798@nct6775.656/pwm2: stable across boots
        public FanCurve Curve;  // null = BIOS
        public bool OnBiosFallback;
        public long? Original;  // pwmN_enable before FanPilot took the header; null while it hasn't
        public double? Duty;    // what FanPilot wrote last, 0..100%; null on the BIOS
        public string Pwm { get { return Chip.File("pwm" + N); } }
        public string Enable { get { return Chip.File("pwm" + N + "_enable"); } }
        public string Tach { get { return Chip.File("fan" + N + "_input"); } }
    }

    sealed class Temp
    {
        public string Path;
        public string Title;
    }

    readonly string _root;
    readonly object _sync = new object();
    readonly ManualResetEvent _stop = new ManualResetEvent(false);
    readonly SoftwareCurveEngine _engine = new SoftwareCurveEngine();
    readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    readonly RestoreFile _restore;
    double _lastApply;
    List<HwmonFiles.Chip> _boardChips = new List<HwmonFiles.Chip>();
    HwmonFiles.Chip _cpuChip;
    List<Temp> _temps = new List<Temp>();
    List<Header> _headers = new List<Header>();
    List<SensorReading> _readings = new List<SensorReading>();
    List<float> _tempValues = new List<float>(), _rpmValues = new List<float>();
    // The CPU's energy counter (RAPL) at the last reading, for its power.
    long? _lastEnergy;
    double _lastEnergyAt;
    int _failures;
    bool _released;

    public string BackendName { get { return "Linux hwmon"; } }
    public bool HardwareCurves { get { return false; } }
    public Msg Notice { get; private set; }
    public bool FanControlSupported { get { lock (_sync) return _headers.Count > 0; } }
    public string[] TempInputTitles { get; private set; }
    public string BoardName { get; private set; }
    // The GPU's temperature now, for curves that follow it (item 25); FanPilotHost sets it once the GPU is open.
    public Func<double?> GpuTemp { get; set; }
    public string Chips { get { lock (_sync) return string.Join(", ", _boardChips.Select(c => c.Name)); } }

    // root: "/" (the tests: a fake one). restoreLeftovers: a FanPilot that was killed left headers taken, and this one
    // gives them back first; --diagnose, which only reads (another FanPilot may be running), doesn't. loop: the
    // once-a-second thread (the tests call Tick themselves).
    public HwmonBoard(string root = "/", bool restoreLeftovers = true, bool loop = true)
    {
        _root = root;
        _restore = new RestoreFile(RestorePath(root));
        TempInputTitles = new string[0];
        BoardName = Platform.BoardName;
        if (restoreLeftovers)
        {
            int handed = RestoreFans(root);
            if (handed > 0) Log.Warn("The previous FanPilot ended without shutting down cleanly: " + handed + " fan(s) handed back to the BIOS.");
        }
        Open();
        if (loop) new Thread(Loop) { IsBackground = true, Name = "hwmon" }.Start();
    }

    internal static string RestorePath(string root) { return Path.Combine(root, "run", "fanpilot", "hwmon-restore.json"); }

    void Open()
    {
        var chips = HwmonFiles.Chips(_root);
        var board = chips.Where(HwmonFiles.IsBoard).ToList();
        var cpu = chips.FirstOrDefault(c => HwmonFiles.IsCpu(c.Name));

        // Reference sensors for curves: the CPU first (what most curves follow), then the board's own inputs.
        var temps = new List<Temp>();
        string cpuTemp = cpu == null ? null : CpuTempFile(cpu);
        if (cpuTemp != null) temps.Add(new Temp { Path = cpuTemp, Title = "CPU" });
        foreach (var chip in board)
            foreach (int n in chip.Indexes("temp"))
                temps.Add(new Temp { Path = chip.File("temp" + n + "_input"), Title = chip.Label("temp", n) ?? "Temp " + n });
        var titles = new List<string>();
        foreach (var t in temps)
        {
            string unique = t.Title;
            for (int n = 2; titles.Contains(unique); n++) unique = t.Title + " (" + n + ")";
            titles.Add(unique);
            t.Title = unique;
        }

        // A header needs pwmN and pwmN_enable: without the second, the driver can't hand it back to the BIOS.
        var headers = new List<Header>();
        foreach (var chip in board)
            foreach (int n in chip.Indexes("pwm").Where(n => File.Exists(chip.File("pwm" + n + "_enable"))))
            {
                string title = chip.Label("fan", n) ?? "Fan " + n;
                if (board.Count > 1) title = chip.Name + " " + title;
                headers.Add(new Header { Chip = chip, N = n, Title = title, Key = "hwmon/" + chip.Key + "/pwm" + n });
            }

        lock (_sync)
        {
            _boardChips = board;
            _cpuChip = cpu;
            _temps = temps;
            _headers = headers;
            TempInputTitles = titles.ToArray();
            Notice = NoticeFor(board, headers, HwmonFiles.LoadedModules(_root));
            Refresh(); // so the first curve applied already sees real temperatures
        }
        Log.Info("Linux hwmon: board " + (BoardName.Length > 0 ? BoardName : "?") + "; chips: " +
                 (board.Count > 0 ? string.Join(", ", board.Select(c => c.Key)) : "none") + "; CPU: " + (cpu != null ? cpu.Name : "none") + "; " +
                 headers.Count + " fans that can be controlled; " + temps.Count + " temperature sensors");
    }

    static Msg NoticeFor(List<HwmonFiles.Chip> board, List<Header> headers, List<string> modules)
    {
        if (board.Count == 0)
        {
            // A board driver loaded with no chip of its own: the BIOS keeps it (ACPI), or it needs telling (it87).
            string module = HwmonFiles.BoardModules.FirstOrDefault(modules.Contains);
            return module != null ? new Msg("board.hwmon.loadedNoChip", "module", module) : new Msg("board.hwmon.noChip");
        }
        if (headers.Count == 0) return new Msg("board.hwmon.noControl", "chips", string.Join(", ", board.Select(c => c.Name)));
        return null;
    }

    // Intel: "Package id 0"; AMD: "Tctl" (what the CPU's own fan control follows) or "Tdie"; else its first temperature.
    static string CpuTempFile(HwmonFiles.Chip cpu)
    {
        var temps = cpu.Indexes("temp");
        foreach (string label in new[] { "Package id 0", "Tctl", "Tdie" })
            foreach (int n in temps)
                if (cpu.Label("temp", n) == label) return cpu.File("temp" + n + "_input");
        return temps.Count > 0 ? cpu.File("temp" + temps[0] + "_input") : null;
    }

    void Loop()
    {
        while (!_stop.WaitOne(0))
        {
            Tick();
            _stop.WaitOne(1000);
        }
    }

    // One second of the loop: read, then apply the curves.
    internal void Tick()
    {
        try
        {
            lock (_sync)
            {
                if (_released) return;
                Refresh();
                ApplyCurves();
            }
            if (_failures >= FailuresBeforeRelease) Log.Info("Linux hwmon answering again: FanPilot's curves applied again.");
            _failures = 0;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            // The log records the transitions (first failure, hand-back, recovery), not every failed second.
            if (++_failures == 1) Log.Warn("Linux hwmon: " + ex.Message);
            if (_failures == FailuresBeforeRelease)
                Log.Error("Linux hwmon failed " + _failures + " times in a row: fans handed back to the BIOS until it answers again. Last error: " + ex.Message);
            if (_failures >= FailuresBeforeRelease) lock (_sync) foreach (var h in _headers) GiveBack(h);
        }
    }

    void Refresh()
    {
        var readings = new List<SensorReading>();
        // The panel's tiles look for these three by id (see wwwroot/app.js renderTiles).
        double? cpuTemp = _temps.Count > 0 && _temps[0].Title == "CPU" ? Celsius(_temps[0].Path) : null;
        if (cpuTemp.HasValue) readings.Add(Reading("Temperature", "cpu-temp", "CPU", cpuTemp.Value, "°C"));
        double? power = CpuPower();
        if (power.HasValue) readings.Add(Reading("Power", "cpu-power", "CPU Package", power.Value, "W"));
        double? clock = CpuClock();
        if (clock.HasValue) readings.Add(Reading("Frequency", "cpu-clock", "CPU", clock.Value, "MHz"));

        foreach (var chip in _boardChips)
        {
            foreach (int n in chip.Indexes("temp"))
            {
                double? t = Celsius(chip.File("temp" + n + "_input"));
                if (t.HasValue) readings.Add(Reading("Temperature", "hwmon/" + chip.Key + "/temp" + n, chip.Label("temp", n) ?? "Temp " + n, t.Value, "°C"));
            }
            foreach (int n in chip.Indexes("fan"))
            {
                long? rpm = HwmonFiles.ReadLong(chip.File("fan" + n + "_input"));
                if (rpm.HasValue) readings.Add(Reading("Fan", "hwmon/" + chip.Key + "/fan" + n, chip.Label("fan", n) ?? "Fan " + n, rpm.Value, "RPM"));
            }
            foreach (int n in chip.Indexes("in"))
            {
                long? mv = HwmonFiles.ReadLong(chip.File("in" + n + "_input"));
                if (mv.HasValue) readings.Add(Reading("Voltage", "hwmon/" + chip.Key + "/in" + n, chip.Label("in", n) ?? "in" + n, mv.Value / 1000.0, "V"));
            }
        }
        CpuRoles.Assign(readings, null, false, null); // the "CPU" reading here is already the package (Tctl, Package id 0)
        _readings = readings;
        _tempValues = _temps.Select(t => { double? c = Celsius(t.Path); return c.HasValue ? (float)c.Value : float.NaN; }).ToList();
        _rpmValues = _headers.Select(h => (float)(HwmonFiles.ReadLong(h.Tach) ?? 0)).ToList();
    }

    static double? Celsius(string path)
    {
        long? milli = HwmonFiles.ReadLong(path);
        return milli.HasValue ? milli.Value / 1000.0 : (double?)null;
    }

    // RAPL's energy counter for the CPU package (Intel, and AMD on recent kernels), in microjoules: the power is what it
    // grew by since the last reading. Only root can read it.
    double? CpuPower()
    {
        string dir = Path.Combine(_root, "sys", "class", "powercap", "intel-rapl:0");
        long? energy = HwmonFiles.ReadLong(Path.Combine(dir, "energy_uj"));
        double now = _clock.Elapsed.TotalSeconds;
        long? last = _lastEnergy;
        double dt = now - _lastEnergyAt;
        _lastEnergy = energy;
        _lastEnergyAt = now;
        if (!energy.HasValue || !last.HasValue || dt < 0.2) return null;
        long grew = energy.Value - last.Value;
        if (grew < 0) grew += HwmonFiles.ReadLong(Path.Combine(dir, "max_energy_range_uj")) ?? 0; // the counter wrapped
        return grew >= 0 ? grew / dt / 1e6 : (double?)null;
    }

    // The fastest core now, from cpufreq (kHz).
    double? CpuClock()
    {
        string dir = Path.Combine(_root, "sys", "devices", "system", "cpu");
        if (!Directory.Exists(dir)) return null;
        long max = 0;
        foreach (string cpu in Directory.GetDirectories(dir, "cpu*"))
        {
            long? khz = HwmonFiles.ReadLong(Path.Combine(cpu, "cpufreq", "scaling_cur_freq"));
            if (khz.HasValue && khz.Value > max) max = khz.Value;
        }
        return max > 0 ? max / 1000.0 : (double?)null;
    }

    static SensorReading Reading(string type, string id, string title, double value, string unit)
    {
        return new SensorReading { Id = type.ToLowerInvariant() + "-" + id, Type = type, Title = title, Value = Math.Round(value, 3), Unit = unit };
    }

    // Re-asserted every second, which also undoes the BIOS taking a header back (after sleep, for instance).
    void ApplyCurves()
    {
        double now = _clock.Elapsed.TotalSeconds, dt = now - _lastApply;
        _lastApply = now;
        for (int i = 0; i < _headers.Count; i++)
        {
            var h = _headers[i];
            if (h.Curve == null) continue;
            var gpuTemp = GpuTemp;
            double t = SoftwareCurveEngine.SourceTemp(h.Curve, _tempValues, gpuTemp == null ? null : gpuTemp());
            double? duty = _engine.Next(i, h.Curve, t, dt);
            if (duty == null)
            {
                if (!h.OnBiosFallback) Log.Warn("Fan " + h.Title + ": no valid reading from the reference sensor; on the BIOS until it comes back.");
                h.OnBiosFallback = true;
                GiveBack(h);
                continue;
            }
            if (h.OnBiosFallback) Log.Info("Fan " + h.Title + ": the reference sensor is back; FanPilot's curve applied again.");
            h.OnBiosFallback = false;
            Take(h);
            double percent = Math.Max(0, Math.Min(100, duty.Value));
            HwmonFiles.Write(h.Pwm, (long)Math.Round(percent * 255 / 100));
            h.Duty = percent;
        }
    }

    // Manual mode, remembering (here and in the restore file) what the header was on before.
    void Take(Header h)
    {
        long? enable = HwmonFiles.ReadLong(h.Enable);
        if (h.Original == null)
        {
            if (!enable.HasValue) throw new IOException("Could not read " + h.Enable + ".");
            h.Original = enable.Value;
            _restore.Add(h.Enable, h.Pwm, enable.Value);
        }
        if (enable != Manual) HwmonFiles.Write(h.Enable, Manual);
    }

    void GiveBack(Header h)
    {
        if (h.Original == null) return; // never taken: the BIOS still has it
        try
        {
            WriteBack(h.Enable, h.Pwm, h.Original.Value);
            h.Original = null;
            h.Duty = null;
            _restore.Remove(h.Enable);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            Log.Warn("Fan " + h.Title + ": could not hand it back to the BIOS: " + ex.Message);
        }
    }

    // A header that was already on manual before FanPilot (another tool's, or a FanPilot killed before the restore file
    // existed) has no automatic mode to go back to: full speed is the safe hand-back.
    static void WriteBack(string enable, string pwm, long original)
    {
        if (original == Manual) HwmonFiles.Write(pwm, 255);
        else HwmonFiles.Write(enable, original);
    }

    // What a FanPilot that was killed left taken goes back to how it was. Returns how many headers it handed back.
    // FanPilot --restore-fans (the service's ExecStopPost) and every start of HwmonBoard run it.
    public static int RestoreFans(string root = "/")
    {
        var file = new RestoreFile(RestorePath(root));
        int handed = 0;
        foreach (var e in file.Entries())
        {
            try
            {
                WriteBack(e.Enable, e.Pwm, e.Original);
                handed++;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Warn("Could not hand " + e.Pwm + " back to the BIOS: " + ex.Message);
            }
        }
        file.Delete();
        return handed;
    }

    public List<Msg> ControlProblems()
    {
        lock (_sync)
        {
            var problems = _headers.Where(h => h.Curve != null && h.OnBiosFallback)
                .Select(h => new Msg("board.problem.sensor", "fan", h.Title)).ToList();
            if (_failures >= FailuresBeforeRelease) problems.Add(new Msg("board.problem.hwmon"));
            return problems;
        }
    }

    public int CountFans() { lock (_sync) return _headers.Count; }

    public List<double?> Duties() { lock (_sync) return _headers.Select(h => h.Original != null ? h.Duty : null).ToList(); }

    public List<SensorReading> ReadSensors() { lock (_sync) return _readings.ToList(); }

    public void ReadFanTach(out List<float> temps, out List<float> rpms)
    {
        lock (_sync) { temps = _tempValues.ToList(); rpms = _rpmValues.ToList(); }
    }

    public FanCurve GetCurve(int index)
    {
        lock (_sync)
        {
            if (index < 0 || index >= _headers.Count) throw new UserError("err.fanMissing");
            var h = _headers[index];
            if (h.Curve == null)
                return new FanCurve { Index = index, Key = h.Key, Title = h.Title, Mode = FanCurve.BiosMode, TempInput = 0, DeltaT = 1 };
            var c = FanCurve.FromDictionary(h.Curve.ToDictionary());
            c.Title = h.Title;
            c.Key = h.Key;
            return c;
        }
    }

    public List<FanCurve> GetCurves()
    {
        int n = CountFans();
        var list = new List<FanCurve>();
        for (int i = 0; i < n; i++) list.Add(GetCurve(i));
        return list;
    }

    public FanCurve SetCurve(FanCurve curve)
    {
        lock (_sync)
        {
            if (_released) throw new Unavailable("board.closing");
            if (curve.Index < 0 || curve.Index >= _headers.Count) throw new UserError("err.fanMissing");
            var h = _headers[curve.Index];
            _engine.Reset(curve.Index);
            if (curve.IsBios || curve.Points.Count == 0)
            {
                h.Curve = null;
                h.OnBiosFallback = false;
                GiveBack(h);
            }
            else
            {
                if (curve.Points.Count < 3 || Math.Round(curve.Points[curve.Points.Count - 1][1]) != 255)
                    throw new UserError("err.curve.shape");
                if (curve.TempInput < 0 || curve.TempInput >= _temps.Count) throw new UserError("err.tempInput");
                h.Curve = FanCurve.FromDictionary(curve.ToDictionary());
                h.Curve.Index = curve.Index;
                try { ApplyCurves(); } // take effect now, not on the next tick
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    h.Curve = null;
                    GiveBack(h);
                    throw new Unavailable("board.hwmon.writeFailed", "fan", h.Title, "detail", ex.Message);
                }
            }
        }
        return GetCurve(curve.Index);
    }

    // Every exit path FanPilot can see ends here: stop the loop and give each header back to the BIOS.
    public void Shutdown()
    {
        lock (_sync)
        {
            if (_released) return;
            _released = true;
            _stop.Set();
            int handed = 0;
            foreach (var h in _headers) if (h.Original != null) { GiveBack(h); handed++; }
            if (handed > 0) Log.Info("Linux hwmon: " + handed + " fan(s) handed back to the BIOS.");
        }
    }

    // /run/fanpilot/hwmon-restore.json: each header FanPilot has taken and its pwmN_enable before, written before the
    // first duty, so that a FanPilot killed with kill -9 leaves a way back.
    sealed class RestoreFile
    {
        public sealed class Entry
        {
            public string Enable, Pwm;
            public long Original;
        }

        readonly string _path;
        readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>();

        public RestoreFile(string path) { _path = path; }

        public void Add(string enable, string pwm, long original)
        {
            _entries[enable] = new Entry { Enable = enable, Pwm = pwm, Original = original };
            Save();
        }

        public void Remove(string enable)
        {
            if (_entries.Remove(enable)) Save();
        }

        void Save()
        {
            if (_entries.Count == 0) { Delete(); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            DataFile.WriteAtomic(_path, new Dictionary<string, object> {
                { "headers", _entries.Values.Select(e => new Dictionary<string, object> { { "enable", e.Enable }, { "pwm", e.Pwm }, { "original", e.Original } }).ToList() }
            });
        }

        public List<Entry> Entries()
        {
            if (!File.Exists(_path)) return new List<Entry>();
            Dictionary<string, object> d;
            try { d = Json.ParseObject(File.ReadAllText(_path)); }
            catch (Exception ex) when (ex is IOException || ex is FormatException)
            {
                Log.Warn("Unreadable " + _path + ": " + ex.Message);
                return new List<Entry>();
            }
            var list = new List<Entry>();
            foreach (object o in Json.ToList(Json.Get(d, "headers") ?? new object[0]))
            {
                var e = o as Dictionary<string, object>;
                int original = e == null ? -1 : Json.GetInt(e, "original", -1);
                if (original < 0) continue;
                list.Add(new Entry { Enable = Json.GetString(e, "enable", ""), Pwm = Json.GetString(e, "pwm", ""), Original = original });
            }
            return list;
        }

        public void Delete()
        {
            _entries.Clear();
            try { File.Delete(_path); File.Delete(_path + ".bak"); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
        }
    }
}
