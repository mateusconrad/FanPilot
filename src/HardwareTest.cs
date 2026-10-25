// FanPilot.exe --hardware-test [--fan N]: the hardware checklist of docs/ROADMAP.md, run on the real board and written
// to data\hwtest-<date>.md for the issue. Safety, in order: it touches only the chosen fan (all of them for the Turbo
// step, at 100%), never below 40%; it refuses the CPU header, pumps and a fan it could not put back; it aborts if the CPU
// or the GPU passes 80 °C; and it puts every fan and the GPU back as it found them, Ctrl+C included. Closing the window
// mid-test is the one way out it cannot intercept: use Ctrl+C. The console and the report are in Windows' language.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

sealed class HardwareTest
{
    public const int Passed = 0, Failed = 6, Aborted = 7, Refused = 8;
    const double AbortTemp = 80;
    // Never below 40%, unlike the panel's presets.
    static readonly double[][] TestCurve = {
        new[] { 0.0, 40 }, new[] { 35.0, 40 }, new[] { 45.0, 50 }, new[] { 55.0, 60 },
        new[] { 62.0, 70 }, new[] { 68.0, 80 }, new[] { 74.0, 90 }, new[] { 82.0, 100 } };
    static readonly Regex CpuHeader = new Regex(@"^CPU", RegexOptions.IgnoreCase);
    // The steps, by their dictionary keys (CurrentStep says which one is running).
    internal const string StepReadings = "hwtest.step.readings", StepFixed = "hwtest.step.fixed", StepCurve = "hwtest.step.curve",
        StepTurbo = "hwtest.step.turbo", StepGpu = "hwtest.step.gpu", StepReopen = "hwtest.step.reopen", StepPutBack = "hwtest.step.putBack";
    const string Ok = "hwtest.result.ok", Fail = "hwtest.result.failed", Stopped = "hwtest.result.aborted";

    // The CPU's header and pumps (by name, or marked by the user: the "role" FanManager reports) are never tested.
    static bool NotForTests(Dictionary<string, object> fan)
    {
        return CpuHeader.IsMatch((string)fan["title"]) || (string)fan["role"] == "pump";
    }

    sealed class StepFailed : Exception
    {
        public readonly Msg Msg;
        public StepFailed(string key, params object[] args) : this(new Msg(key, args)) { }
        StepFailed(Msg msg) : base(msg.Text) { Msg = msg; }
    }

    sealed class AbortedException : Exception
    {
        public readonly Msg Msg;
        public AbortedException(string key, params object[] args) : this(new Msg(key, args)) { }
        AbortedException(Msg msg) : base(msg.Text) { Msg = msg; }
    }

    sealed class Step
    {
        public string Name, Result; // dictionary keys
        public Msg Details;
        public TimeSpan Took;
    }

    readonly GpuMonitor _gpu;
    readonly AppConfig _cfg;
    readonly string _dataDir, _lang;
    readonly Func<IBoard> _reopen;
    readonly Action<TimeSpan> _sleep;
    readonly TimeSpan _settle;
    readonly List<Step> _steps = new List<Step>();
    IBoard _board;
    FanManager _fans;
    volatile bool _cancel;
    bool _touchedFans, _touchedGpu;

    // reopen: a new connection to the board, as after a restart. sleep and settle (the time a fan takes to reach a new
    // speed): the tests do not wait for real. lang: what the console and the report are written in.
    internal HardwareTest(IBoard board, GpuMonitor gpu, AppConfig cfg, string dataDir, Func<IBoard> reopen, Action<TimeSpan> sleep, TimeSpan settle, string lang)
    {
        _board = board;
        _gpu = gpu;
        _cfg = cfg;
        _dataDir = dataDir;
        _reopen = reopen;
        _sleep = sleep;
        _settle = settle;
        _lang = lang;
        _fans = new FanManager(board, cfg, dataDir);
    }

    // The step running now, by its key (the tests use it to act at the right moment).
    internal string CurrentStep { get; private set; }
    internal IBoard Board { get { return _board; } }

    public void Cancel() { _cancel = true; }

    string T(string key, params object[] args) { return new Msg(key, args).In(_lang); }

    public static int Run(string[] args, AppConfig cfg, string dataDir, Func<IBoard> openBoard, GpuMonitor gpu)
    {
        int? fan = null;
        int at = Array.IndexOf(args, "--fan"), parsed;
        if (at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], out parsed)) fan = parsed;

        var test = new HardwareTest(openBoard(), gpu, cfg, dataDir, openBoard, t => Thread.Sleep(t), TimeSpan.FromSeconds(8), I18n.Windows);
        ConsoleCancelEventHandler onCtrlC = (s, e) =>
        {
            e.Cancel = true; // the test stops at its next check and puts everything back
            test.Cancel();
            Console.WriteLine(test.T("hwtest.interrupting"));
        };
        Console.CancelKeyPress += onCtrlC;
        try
        {
            Console.WriteLine(test.T("hwtest.start"));
            string report;
            int code = test.Execute(fan, out report);
            if (report != null) Console.WriteLine(test.T("hwtest.reportAt", "path", report));
            Console.WriteLine(test.T(code == Passed ? "hwtest.outcome.passed" : code == Failed ? "hwtest.outcome.failed" :
                                     code == Aborted ? "hwtest.outcome.aborted" : "hwtest.outcome.refused"));
            return code;
        }
        finally
        {
            Console.CancelKeyPress -= onCtrlC;
            try { test.Board.Shutdown(); } catch { }
            try { gpu.Shutdown(); } catch { }
        }
    }

    // Passed, Failed, Aborted or Refused. The report is written whenever the test started.
    internal int Execute(int? requested, out string report)
    {
        report = null;
        var started = DateTime.Now;
        List<Dictionary<string, object>> fans;
        try
        {
            fans = Fans(_fans.Describe()); // loads the saved state and backs up the originals, as Start() does
            if (!_board.HardwareCurves) _fans.ReapplyDesired();
        }
        catch (Exception ex)
        {
            Console.WriteLine(T("hwtest.boardDown", "detail", Msg.TextOf(ex, _lang)));
            return Refused;
        }
        int index = requested ?? PickFan(fans);
        Msg refusal = Refusal(fans, index);
        if (refusal != null)
        {
            Console.WriteLine(refusal.In(_lang));
            return Refused;
        }
        string title = (string)fans[index]["title"];
        Console.WriteLine(T("hwtest.fan", "index", index, "title", title));
        var previous = fans[index]["request"] as Dictionary<string, object>;
        var gpuBefore = _gpu.Read();

        int outcome = Passed;
        try
        {
            RunStep(StepReadings, Readings);
            RunStep(StepFixed, () => Responds(index));
            RunStep(StepCurve, () => CurveHolds(index));
            RunStep(StepTurbo, Turbo);
            RunStep(StepGpu, () => Gpu(gpuBefore));
            RunStep(StepReopen, () => Reopen(index));
            if (_steps.Any(s => s.Result == Fail)) outcome = Failed;
        }
        catch (AbortedException ex)
        {
            outcome = Aborted;
            _steps.Add(new Step { Name = CurrentStep, Result = Stopped, Details = ex.Msg });
        }
        finally
        {
            PutBack(index, previous, gpuBefore);
        }
        report = WriteReport(started, index, title, outcome);
        return outcome;
    }

    static List<Dictionary<string, object>> Fans(Dictionary<string, object> describe)
    {
        return ((List<object>)describe["fans"]).Cast<Dictionary<string, object>>().ToList();
    }

    // The first header that is neither the CPU's nor a pump, preferring one whose tachometer reads.
    int PickFan(List<Dictionary<string, object>> fans)
    {
        var rpm = Rpms();
        var candidates = Enumerable.Range(0, fans.Count).Where(i => !NotForTests(fans[i])).ToList();
        return candidates.Where(i => i < rpm.Count && rpm[i] > 0).DefaultIfEmpty(candidates.DefaultIfEmpty(-1).First()).First();
    }

    Msg Refusal(List<Dictionary<string, object>> fans, int index)
    {
        if (fans.Count == 0) return new Msg("hwtest.refuse.noFans");
        if (index < 0 || index >= fans.Count) return new Msg("hwtest.refuse.noSuchFan", "max", fans.Count - 1);
        string title = (string)fans[index]["title"];
        if (NotForTests(fans[index])) return new Msg("hwtest.refuse.cpuOrPump", "title", title);
        if (_fans.TurboActive) return new Msg("hwtest.refuse.turbo");
        // Editing a fan leaves the profile (item 18): the test must not change the user's mode.
        if (_fans.Mode != "custom") return new Msg("hwtest.refuse.profile");
        if (!(bool)fans[index]["managed"] && !(bool)fans[index]["hasBackup"]) return new Msg("hwtest.refuse.noBackup", "title", title);
        return null;
    }

    void RunStep(string name, Func<Msg> step)
    {
        CurrentStep = name;
        Guard();
        var watch = Stopwatch.StartNew();
        var record = new Step { Name = name };
        try
        {
            record.Details = step();
            record.Result = Ok;
        }
        catch (StepFailed ex) { record.Result = Fail; record.Details = ex.Msg; }
        catch (AbortedException) { throw; }
        catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
        {
            record.Result = Fail;
            record.Details = Msg.Of(ex) ?? new Msg("raw", "text", ex.Message);
        }
        record.Took = watch.Elapsed;
        _steps.Add(record);
        Console.WriteLine("  " + T(name) + ": " + T(record.Result) + (record.Details != null ? " (" + record.Details.In(_lang) + ")" : ""));
    }

    // Before each step and every second of each wait: Ctrl+C and the temperatures.
    void Guard()
    {
        if (_cancel) throw new AbortedException("hwtest.abort.ctrlC");
        double cpu = CpuTemp();
        if (cpu > AbortTemp) throw new AbortedException("hwtest.abort.hot", "what", "CPU", "temp", (int)Math.Round(cpu), "limit", (int)AbortTemp);
        object gpuTemp;
        if (_gpu.Read().TryGetValue("temp", out gpuTemp) && gpuTemp != null && Convert.ToDouble(gpuTemp) > AbortTemp)
            throw new AbortedException("hwtest.abort.hot", "what", "GPU", "temp", Convert.ToInt32(gpuTemp), "limit", (int)AbortTemp);
    }

    void Wait(TimeSpan time)
    {
        var slice = TimeSpan.FromSeconds(1);
        for (var left = time; left > TimeSpan.Zero; left -= slice)
        {
            Guard();
            _sleep(left < slice ? left : slice);
        }
        Guard();
    }

    // The cores' temperature when there is a reading for them (the safer one for the 80 °C abort), else the board's.
    double CpuTemp()
    {
        var cpu = CpuRoles.MainOf(_board.ReadSensors());
        return cpu == null ? double.NaN : cpu.Value;
    }

    List<float> Rpms()
    {
        List<float> temps, rpms;
        _board.ReadFanTach(out temps, out rpms);
        return rpms;
    }

    void Apply(int index, Dictionary<string, object> request)
    {
        _touchedFans = true;
        _fans.Apply(index, request);
    }

    static Dictionary<string, object> Fixed(int percent)
    {
        return new Dictionary<string, object> { { "mode", "fixed" }, { "percent", percent } };
    }

    Msg Readings()
    {
        var sensors = _board.ReadSensors();
        var temps = sensors.Where(s => s.Type == "Temperature").ToList();
        if (temps.Count == 0) throw new StepFailed("hwtest.fail.noTemps");
        var implausible = temps.Where(s => (s.Role != null || s.Title == "CPU") && (s.Value < 5 || s.Value > 110)).ToList();
        if (implausible.Count > 0) throw new StepFailed("hwtest.fail.cpuImpossible", "temp", implausible[0].Value);
        var rpms = Rpms();
        var fans = Fans(_fans.Describe());
        string rpmText = string.Join(", ", fans.Select((f, i) => f["title"] + " " + (i < rpms.Count ? rpms[i].ToString("0") : "?")));
        return new Msg("hwtest.readings", "count", temps.Count, "cpu", (int)Math.Round(CpuTemp()), "rpms", rpmText, "backend", _board.BackendName + GccVersion());
    }

    // Only when the board is reached through GCC: an update of it is what most often changes this path.
    string GccVersion()
    {
        string dll = Path.Combine(Program.GccLibDir, "Gigabyte.Engine.EnvironmentControl.dll");
        return _board.BackendName.Contains("Gigabyte") && File.Exists(dll) ? " (GCC " + FileVersionInfo.GetVersionInfo(dll).FileVersion + ")" : "";
    }

    Msg Responds(int index)
    {
        float before = Rpms()[index];
        Apply(index, Fixed(40));
        Wait(_settle);
        float at40 = Rpms()[index];
        ExpectWritten(index, 102);
        Apply(index, Fixed(100));
        Wait(_settle);
        float at100 = Rpms()[index];
        ExpectWritten(index, 255);
        var seen = new Msg("hwtest.fixed.seen", "before", (int)Math.Round(before), "at40", (int)Math.Round(at40), "at100", (int)Math.Round(at100));
        if (before <= 0 && at40 <= 0 && at100 <= 0) return new Msg("hwtest.fixed.noTach");
        if (at100 < at40 * 1.15) throw new StepFailed("hwtest.fail.rpmFlat", "seen", seen);
        return seen;
    }

    // What the board now runs is what was asked (first point of a fixed-mode curve).
    void ExpectWritten(int index, double pwm)
    {
        var curve = _board.GetCurve(index);
        if (curve.Points.Count == 0 || Math.Abs(curve.Points[0][1] - pwm) > 3)
            throw new StepFailed("hwtest.fail.notWritten", "detail", curve.Points.Count == 0
                ? new Msg("hwtest.written.none") : new Msg("hwtest.written.other", "got", curve.Points[0][1], "want", pwm));
    }

    Msg CurveHolds(int index)
    {
        int count = Convert.ToInt32(Fans(_fans.Describe())[index]["pointCount"]);
        var points = Resample(TestCurve, count).Select(p => (object)new object[] { p[0], p[1] }).ToList();
        Apply(index, new Dictionary<string, object> { { "mode", "curve" }, { "points", points } });
        int before = _fans.ExternalChanges;
        for (int round = 0; round < 4; round++)
        {
            Wait(TimeSpan.FromSeconds(15));
            _fans.Watchdog();
        }
        int reapplied = _fans.ExternalChanges - before;
        if (reapplied > 0) throw new StepFailed("hwtest.fail.watchdog", "n", reapplied);
        return new Msg("hwtest.curve.ok");
    }

    // count points spread over the same range (the same rule as resample() in the panel).
    static List<double[]> Resample(double[][] pts, int count)
    {
        var curve = new FanCurve { Mode = "SmartFanMode" };
        curve.Points.AddRange(pts);
        double t0 = pts[0][0], t1 = pts[pts.Length - 1][0];
        return Enumerable.Range(0, count).Select(k =>
        {
            double t = Math.Round(t0 + (t1 - t0) * k / (count - 1));
            return new[] { t, Math.Round(curve.PwmAt(t)) };
        }).ToList();
    }

    Msg Turbo()
    {
        var before = Rpms();
        var signatures = Enumerable.Range(0, before.Count).Select(i => _board.GetCurve(i).Signature()).ToList();
        _touchedFans = true;
        _fans.SetTurbo(true);
        Wait(_settle);
        var on = Rpms();
        for (int i = 0; i < before.Count; i++)
        {
            var curve = _board.GetCurve(i);
            if (curve.Points.Any(p => p[1] < 255)) throw new StepFailed("hwtest.fail.turboNot100", "fan", curve.Title);
            if (before[i] > 0 && on[i] < before[i] * 0.9)
                throw new StepFailed("hwtest.fail.turboSlower", "fan", curve.Title, "before", (int)Math.Round(before[i]), "on", (int)Math.Round(on[i]));
        }
        _fans.SetTurbo(false);
        Wait(_settle);
        for (int i = 0; i < before.Count; i++)
            if (_board.GetCurve(i).Signature() != signatures[i]) throw new StepFailed("hwtest.fail.turboNotBack", "fan", _board.GetCurve(i).Title);
        return new Msg("hwtest.turbo.ok", "rpms", string.Join(", ", before.Select((r, i) => r.ToString("0") + " → " + on[i].ToString("0"))));
    }

    Msg Gpu(Dictionary<string, object> before)
    {
        if (!(before["ok"] is bool ok && ok)) return new Msg("hwtest.gpu.noGpu");
        if (!before.ContainsKey("fanMin")) return new Msg("hwtest.gpu.noControl");
        _touchedGpu = true;
        try { _gpu.SetFixed(50); }
        catch (InvalidOperationException ex) { return new Msg("hwtest.gpu.skipped", "detail", Msg.Of(ex) ?? new Msg("raw", "text", ex.Message)); }
        Wait(TimeSpan.FromSeconds(5));
        var r = _gpu.Read();
        var speeds = (r["fans"] as IEnumerable<int> ?? Enumerable.Empty<int>()).ToList();
        if ((string)r["fanMode"] != "fixed") throw new StepFailed("hwtest.fail.gpuNotFixed");
        if (speeds.Count > 0 && speeds.Any(v => Math.Abs(v - 50) > 10)) throw new StepFailed("hwtest.fail.gpuSpeed", "speeds", string.Join("/", speeds));
        _gpu.SetAuto();
        Wait(TimeSpan.FromSeconds(2));
        if ((string)_gpu.Read()["fanMode"] != "auto") throw new StepFailed("hwtest.fail.gpuNotAuto");
        return new Msg("hwtest.gpu.ok", "speeds", speeds.Count > 0 ? (object)(string.Join("/", speeds) + "%") : new Msg("hwtest.gpu.noReading"));
    }

    // What a fan does when FanPilot exits: in the chip (Gigabyte) the curve stays; other boards hand it to the BIOS.
    Msg Reopen(int index)
    {
        Apply(index, Fixed(40));
        string written = _board.GetCurve(index).Signature();
        bool hardware = _board.HardwareCurves;
        _board.Shutdown();
        _board = _reopen();
        _fans = new FanManager(_board, _cfg, _dataDir);
        var after = _board.GetCurve(index);
        if (hardware && after.Signature() != written) throw new StepFailed("hwtest.fail.notInChip");
        if (!hardware && !after.IsBios) throw new StepFailed("hwtest.fail.notBios");
        _fans.Describe();
        if (!_board.HardwareCurves) _fans.ReapplyDesired(); // what the next start does
        return new Msg(hardware ? "hwtest.reopen.chip" : "hwtest.reopen.bios");
    }

    // Exactly what was there before: the user's own setting for that fan, or its original curve; the GPU likewise.
    void PutBack(int index, Dictionary<string, object> previous, Dictionary<string, object> gpuBefore)
    {
        var problems = new List<string>();
        var watch = Stopwatch.StartNew();
        if (_touchedFans)
        {
            try { if (_fans.TurboActive) _fans.SetTurbo(false); }
            catch (Exception ex) { problems.Add("Turbo: " + Msg.TextOf(ex, _lang)); }
            try { if (previous != null) _fans.Apply(index, previous); else _fans.Restore(index); }
            catch (Exception ex) { problems.Add("fan: " + Msg.TextOf(ex, _lang)); }
        }
        if (_touchedGpu)
        {
            try
            {
                object target;
                if ((string)gpuBefore["fanMode"] == "fixed" && gpuBefore.TryGetValue("fanTarget", out target) && target != null) _gpu.SetFixed(Convert.ToInt32(target));
                else _gpu.SetAuto();
            }
            catch (Exception ex) { problems.Add("GPU: " + Msg.TextOf(ex, _lang)); }
        }
        if (!_touchedFans && !_touchedGpu) return;
        _steps.Add(new Step
        {
            Name = StepPutBack,
            Result = problems.Count == 0 ? Ok : Fail,
            Details = problems.Count == 0 ? new Msg(previous != null ? "hwtest.putBack.user" : "hwtest.putBack.original")
                                          : new Msg("raw", "text", string.Join("; ", problems)),
            Took = watch.Elapsed
        });
    }

    string WriteReport(DateTime started, int index, string title, int outcome)
    {
        var sb = new StringBuilder();
        var took = DateTime.Now - started;
        sb.AppendLine(T("hwtest.report.title"));
        sb.AppendLine();
        sb.AppendLine(T("hwtest.report.when", "when", started.ToString("yyyy-MM-dd HH:mm:ss"), "minutes", (int)took.TotalMinutes, "seconds", took.Seconds));
        sb.AppendLine("- FanPilot: " + AppVersion.Text);
        sb.AppendLine(T("hwtest.report.board", "board", Cell(MachineData.Board), "backend", _board.BackendName + GccVersion()));
        sb.AppendLine(T("hwtest.report.fan", "index", index, "title", Cell(title)));
        sb.AppendLine(T("hwtest.report.outcome", "outcome", T(outcome == Passed ? "hwtest.word.passed" : outcome == Failed ? Fail : Stopped)));
        sb.AppendLine();
        sb.AppendLine(T("hwtest.report.table"));
        sb.AppendLine("|---|---|---|---|");
        foreach (var s in _steps)
            sb.AppendLine("| " + Cell(s.Name == null ? "—" : T(s.Name)) + " | " + T(s.Result) + " | " + Cell(s.Details == null ? null : s.Details.In(_lang)) + " | " +
                          s.Took.TotalSeconds.ToString("0.0") + " s |");
        string path = Path.Combine(_dataDir, "hwtest-" + started.ToString("yyyyMMdd-HHmmss") + ".md");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        return path;
    }

    // Header names come from the hardware: a "|" would break the table.
    static string Cell(string text) { return (text ?? "").Replace("|", "\\|").Replace("\r", " ").Replace("\n", " "); }
}
