// FanPilot.exe --hardware-test [--fan N]: the hardware checklist of docs/ROADMAP.md, run on the real board and written
// to data\hwtest-<date>.md for the issue. Safety, in order: it touches only the chosen fan (all of them for the Turbo
// step, at 100%), never below 40%; it refuses the CPU header, pumps and a fan it could not put back; it aborts if the CPU
// or the GPU passes 80 °C; and it puts every fan and the GPU back as it found them, Ctrl+C included. Closing the window
// mid-test is the one way out it cannot intercept: use Ctrl+C.
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
    static readonly Regex NotForTests = new Regex(@"^CPU|PUMP|BOMBA|\bAIO\b", RegexOptions.IgnoreCase);

    sealed class StepFailed : Exception { public StepFailed(string message) : base(message) { } }
    sealed class AbortedException : Exception { public AbortedException(string message) : base(message) { } }

    sealed class Step
    {
        public string Name, Result, Details;
        public TimeSpan Took;
    }

    readonly GpuMonitor _gpu;
    readonly AppConfig _cfg;
    readonly string _dataDir;
    readonly Func<IBoard> _reopen;
    readonly Action<TimeSpan> _sleep;
    readonly TimeSpan _settle;
    readonly List<Step> _steps = new List<Step>();
    IBoard _board;
    FanManager _fans;
    volatile bool _cancel;
    bool _touchedFans, _touchedGpu;

    // reopen: a new connection to the board, as after a restart. sleep and settle (the time a fan takes to reach a new
    // speed): the tests do not wait for real.
    internal HardwareTest(IBoard board, GpuMonitor gpu, AppConfig cfg, string dataDir, Func<IBoard> reopen, Action<TimeSpan> sleep, TimeSpan settle)
    {
        _board = board;
        _gpu = gpu;
        _cfg = cfg;
        _dataDir = dataDir;
        _reopen = reopen;
        _sleep = sleep;
        _settle = settle;
        _fans = new FanManager(board, cfg, dataDir);
    }

    // The step running now (the tests use it to act at the right moment).
    internal string CurrentStep { get; private set; }
    internal IBoard Board { get { return _board; } }

    public void Cancel() { _cancel = true; }

    public static int Run(string[] args, AppConfig cfg, string dataDir, Func<IBoard> openBoard, GpuMonitor gpu)
    {
        int? fan = null;
        int at = Array.IndexOf(args, "--fan"), parsed;
        if (at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], out parsed)) fan = parsed;

        var test = new HardwareTest(openBoard(), gpu, cfg, dataDir, openBoard, t => Thread.Sleep(t), TimeSpan.FromSeconds(8));
        ConsoleCancelEventHandler onCtrlC = (s, e) =>
        {
            e.Cancel = true; // the test stops at its next check and puts everything back
            test.Cancel();
            Console.WriteLine("Interrompendo: devolvendo os fans e a GPU como estavam…");
        };
        Console.CancelKeyPress += onCtrlC;
        try
        {
            Console.WriteLine("Teste em hardware: cerca de 2 minutos. Ctrl+C interrompe e devolve tudo como estava.");
            string report;
            int code = test.Execute(fan, out report);
            if (report != null) Console.WriteLine("Relatório: " + report);
            Console.WriteLine(code == Passed ? "Resultado: passou." : code == Failed ? "Resultado: falhou (veja o relatório)." :
                              code == Aborted ? "Resultado: interrompido." : "Teste não executado.");
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
            Console.WriteLine("A placa não respondeu: " + ex.Message);
            return Refused;
        }
        int index = requested ?? PickFan(fans);
        string refusal = Refusal(fans, index);
        if (refusal != null)
        {
            Console.WriteLine(refusal);
            return Refused;
        }
        string title = (string)fans[index]["title"];
        Console.WriteLine("Fan testado: [" + index + "] " + title);
        var previous = fans[index]["request"] as Dictionary<string, object>;
        var gpuBefore = _gpu.Read();

        int outcome = Passed;
        try
        {
            RunStep("Leituras", Readings);
            RunStep("Fixo 40% e 100%", () => Responds(index));
            RunStep("Curva por 60 s sem o watchdog reaplicar", () => CurveHolds(index));
            RunStep("Turbo liga e desliga", Turbo);
            RunStep("GPU: fixo 50% e automático", () => Gpu(gpuBefore));
            RunStep("Encerrar e abrir a placa de novo", () => Reopen(index));
            if (_steps.Any(s => s.Result == "falhou")) outcome = Failed;
        }
        catch (AbortedException ex)
        {
            outcome = Aborted;
            _steps.Add(new Step { Name = CurrentStep ?? "—", Result = "interrompido", Details = ex.Message });
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
        var candidates = Enumerable.Range(0, fans.Count).Where(i => !NotForTests.IsMatch((string)fans[i]["title"])).ToList();
        return candidates.Where(i => i < rpm.Count && rpm[i] > 0).DefaultIfEmpty(candidates.DefaultIfEmpty(-1).First()).First();
    }

    string Refusal(List<Dictionary<string, object>> fans, int index)
    {
        if (fans.Count == 0) return "Esta placa não oferece controle de fans pelo FanPilot: não há o que testar.";
        if (index < 0 || index >= fans.Count) return "Fan inexistente. Escolha um entre 0 e " + (fans.Count - 1) + " com --fan.";
        string title = (string)fans[index]["title"];
        if (NotForTests.IsMatch(title))
            return "'" + title + "' parece ser o fan da CPU ou uma bomba: o teste não mexe nele. Escolha um fan de gabinete com --fan.";
        if (_fans.TurboActive) return "Desligue o Turbo antes do teste.";
        if (!(bool)fans[index]["managed"] && !(bool)fans[index]["hasBackup"])
            return "'" + title + "' não tem a curva original salva: o teste não teria como devolvê-lo. Escolha outro fan.";
        return null;
    }

    void RunStep(string name, Func<string> step)
    {
        CurrentStep = name;
        Guard();
        var watch = Stopwatch.StartNew();
        var record = new Step { Name = name };
        try
        {
            record.Details = step();
            record.Result = "ok";
        }
        catch (StepFailed ex) { record.Result = "falhou"; record.Details = ex.Message; }
        catch (AbortedException) { throw; }
        catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException) { record.Result = "falhou"; record.Details = ex.Message; }
        record.Took = watch.Elapsed;
        _steps.Add(record);
        Console.WriteLine("  " + name + ": " + record.Result + (record.Details != null ? " (" + record.Details + ")" : ""));
    }

    // Before each step and every second of each wait: Ctrl+C and the temperatures.
    void Guard()
    {
        if (_cancel) throw new AbortedException("Ctrl+C: teste interrompido pelo usuário.");
        double cpu = CpuTemp();
        if (cpu > AbortTemp) throw new AbortedException("CPU a " + cpu.ToString("0") + " °C (o teste para acima de " + AbortTemp + " °C).");
        object gpuTemp;
        if (_gpu.Read().TryGetValue("temp", out gpuTemp) && gpuTemp != null && Convert.ToDouble(gpuTemp) > AbortTemp)
            throw new AbortedException("GPU a " + gpuTemp + " °C (o teste para acima de " + AbortTemp + " °C).");
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

    double CpuTemp()
    {
        var cpu = _board.ReadSensors().FirstOrDefault(s => s.Type == "Temperature" && s.Title == "CPU");
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

    string Readings()
    {
        var sensors = _board.ReadSensors();
        var temps = sensors.Where(s => s.Type == "Temperature").ToList();
        if (temps.Count == 0) throw new StepFailed("nenhuma temperatura lida");
        var implausible = temps.Where(s => s.Title == "CPU" && (s.Value < 5 || s.Value > 110)).ToList();
        if (implausible.Count > 0) throw new StepFailed("temperatura da CPU impossível: " + implausible[0].Value + " °C");
        var rpms = Rpms();
        var fans = Fans(_fans.Describe());
        string rpmText = string.Join(", ", fans.Select((f, i) => f["title"] + " " + (i < rpms.Count ? rpms[i].ToString("0") : "?")));
        return temps.Count + " temperaturas (CPU " + CpuTemp().ToString("0") + " °C); RPM: " + rpmText + "; " + _board.BackendName + GccVersion();
    }

    // Only when the board is reached through GCC: an update of it is what most often changes this path.
    string GccVersion()
    {
        string dll = Path.Combine(Program.GccLibDir, "Gigabyte.Engine.EnvironmentControl.dll");
        return _board.BackendName.Contains("Gigabyte") && File.Exists(dll) ? " (GCC " + FileVersionInfo.GetVersionInfo(dll).FileVersion + ")" : "";
    }

    string Responds(int index)
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
        string seen = "antes " + before.ToString("0") + " RPM; 40% → " + at40.ToString("0") + " RPM; 100% → " + at100.ToString("0") + " RPM";
        if (before <= 0 && at40 <= 0 && at100 <= 0) return "sem tacômetro: só a gravação da curva foi conferida";
        if (at100 < at40 * 1.15) throw new StepFailed("o RPM quase não mudou (" + seen + ")");
        return seen;
    }

    // What the board now runs is what was asked (first point of a fixed-mode curve).
    void ExpectWritten(int index, double pwm)
    {
        var curve = _board.GetCurve(index);
        if (curve.Points.Count == 0 || Math.Abs(curve.Points[0][1] - pwm) > 3)
            throw new StepFailed("a placa não ficou com o que foi gravado (" + (curve.Points.Count == 0 ? "sem curva" : curve.Points[0][1] + " em vez de " + pwm) + ")");
    }

    string CurveHolds(int index)
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
        if (reapplied > 0)
            throw new StepFailed("o watchdog precisou reaplicar a curva " + reapplied + " vez(es): algo (GCC, BIOS) está sobrescrevendo os fans");
        return "curva de 40% a 100% ativa por 60 s; o watchdog não precisou reaplicar";
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

    string Turbo()
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
            if (curve.Points.Any(p => p[1] < 255)) throw new StepFailed(curve.Title + " não foi a 100% com o Turbo");
            if (before[i] > 0 && on[i] < before[i] * 0.9) throw new StepFailed(curve.Title + " ficou mais lento com o Turbo (" + before[i] + " → " + on[i] + " RPM)");
        }
        _fans.SetTurbo(false);
        Wait(_settle);
        for (int i = 0; i < before.Count; i++)
            if (_board.GetCurve(i).Signature() != signatures[i]) throw new StepFailed(_board.GetCurve(i).Title + " não voltou ao que estava depois do Turbo");
        return "RPM antes → com Turbo: " + string.Join(", ", before.Select((r, i) => r.ToString("0") + " → " + on[i].ToString("0"))) +
               "; ao desligar, cada fan voltou ao que estava";
    }

    string Gpu(Dictionary<string, object> before)
    {
        if (!(before["ok"] is bool ok && ok)) return "pulado: sem GPU";
        if (!before.ContainsKey("fanMin")) return "pulado: o fan desta GPU não é controlável";
        _touchedGpu = true;
        try { _gpu.SetFixed(50); }
        catch (InvalidOperationException ex) { return "pulado: " + ex.Message; }
        Wait(TimeSpan.FromSeconds(5));
        var r = _gpu.Read();
        var speeds = (r["fans"] as IEnumerable<int> ?? Enumerable.Empty<int>()).ToList();
        if ((string)r["fanMode"] != "fixed") throw new StepFailed("o fan da GPU não ficou em modo fixo");
        if (speeds.Count > 0 && speeds.Any(v => Math.Abs(v - 50) > 10)) throw new StepFailed("o fan da GPU marca " + string.Join("/", speeds) + "% em vez de 50%");
        _gpu.SetAuto();
        Wait(TimeSpan.FromSeconds(2));
        if ((string)_gpu.Read()["fanMode"] != "auto") throw new StepFailed("o fan da GPU não voltou ao automático");
        return "50% → " + (speeds.Count > 0 ? string.Join("/", speeds) + "%" : "sem leitura") + "; automático de novo";
    }

    // What a fan does when FanPilot exits: in the chip (Gigabyte) the curve stays; other boards hand it to the BIOS.
    string Reopen(int index)
    {
        Apply(index, Fixed(40));
        string written = _board.GetCurve(index).Signature();
        bool hardware = _board.HardwareCurves;
        _board.Shutdown();
        _board = _reopen();
        _fans = new FanManager(_board, _cfg, _dataDir);
        var after = _board.GetCurve(index);
        if (hardware && after.Signature() != written) throw new StepFailed("a curva não ficou no chip depois de encerrar");
        if (!hardware && !after.IsBios) throw new StepFailed("o fan não voltou à BIOS ao encerrar");
        _fans.Describe();
        if (!_board.HardwareCurves) _fans.ReapplyDesired(); // what the next start does
        return hardware ? "a curva continuou no chip depois de encerrar" : "ao encerrar, o fan voltou à BIOS";
    }

    // Exactly what was there before: the user's own setting for that fan, or its original curve; the GPU likewise.
    void PutBack(int index, Dictionary<string, object> previous, Dictionary<string, object> gpuBefore)
    {
        var problems = new List<string>();
        var watch = Stopwatch.StartNew();
        if (_touchedFans)
        {
            try { if (_fans.TurboActive) _fans.SetTurbo(false); }
            catch (Exception ex) { problems.Add("Turbo: " + ex.Message); }
            try { if (previous != null) _fans.Apply(index, previous); else _fans.Restore(index); }
            catch (Exception ex) { problems.Add("fan: " + ex.Message); }
        }
        if (_touchedGpu)
        {
            try
            {
                object target;
                if ((string)gpuBefore["fanMode"] == "fixed" && gpuBefore.TryGetValue("fanTarget", out target) && target != null) _gpu.SetFixed(Convert.ToInt32(target));
                else _gpu.SetAuto();
            }
            catch (Exception ex) { problems.Add("GPU: " + ex.Message); }
        }
        if (!_touchedFans && !_touchedGpu) return;
        _steps.Add(new Step
        {
            Name = "Devolver tudo como estava",
            Result = problems.Count == 0 ? "ok" : "falhou",
            Details = problems.Count == 0 ? (previous != null ? "configuração do usuário reaplicada" : "curva original restaurada") : string.Join("; ", problems),
            Took = watch.Elapsed
        });
    }

    string WriteReport(DateTime started, int index, string title, int outcome)
    {
        var sb = new StringBuilder();
        var took = DateTime.Now - started;
        sb.AppendLine("# FanPilot · teste em hardware");
        sb.AppendLine();
        sb.AppendLine("- Quando: " + started.ToString("yyyy-MM-dd HH:mm:ss") + " (" + (int)took.TotalMinutes + " min " + took.Seconds + " s)");
        sb.AppendLine("- FanPilot: " + typeof(HardwareTest).Assembly.GetName().Version);
        sb.AppendLine("- Placa: " + Cell(MachineData.Board) + " · " + _board.BackendName + GccVersion());
        sb.AppendLine("- Fan testado: [" + index + "] " + Cell(title));
        sb.AppendLine("- Resultado: **" + (outcome == Passed ? "passou" : outcome == Failed ? "falhou" : "interrompido") + "**");
        sb.AppendLine();
        sb.AppendLine("| Passo | Resultado | Detalhes | Tempo |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var s in _steps)
            sb.AppendLine("| " + Cell(s.Name) + " | " + s.Result + " | " + Cell(s.Details) + " | " + s.Took.TotalSeconds.ToString("0.0") + " s |");
        string path = Path.Combine(_dataDir, "hwtest-" + started.ToString("yyyyMMdd-HHmmss") + ".md");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        return path;
    }

    // Header names come from the hardware: a "|" would break the table.
    static string Cell(string text) { return (text ?? "").Replace("|", "\\|").Replace("\r", " ").Replace("\n", " "); }
}
