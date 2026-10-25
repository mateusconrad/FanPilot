using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;

static class Log
{
    static readonly LogWriter Writer = new LogWriter();

    // data\fanpilot.log; null (the tests) = console only.
    public static string FilePath { get { return Writer.FilePath; } set { Writer.FilePath = value; } }

    public static void Info(string msg) { Writer.Write("INFO", msg); }
    public static void Warn(string msg) { Writer.Write("WARN", msg); }
    // Something took the fans out of FanPilot's control (handed back to the BIOS, the board stopped answering).
    public static void Error(string msg) { Writer.Write("ERROR", msg); }
}

// The console and data\fanpilot.log, rotated by size (fanpilot.1.log … fanpilot.3.log). The same line repeated in a
// row is written once and then summarized every minute ("repetida N vezes"), so a failure that recurs every second
// costs a few lines instead of 170 thousand a day.
sealed class LogWriter
{
    const int Generations = 3;
    static readonly TimeSpan SummaryEvery = TimeSpan.FromSeconds(60);
    readonly object _sync = new object();
    readonly Func<DateTime> _now;
    readonly long _maxBytes;
    string _lastLevel, _lastText;
    int _repeats;
    DateTime _repeatSince;

    public string FilePath;
    public bool ToConsole = true;

    public LogWriter() : this(() => DateTime.Now, 5 * 1024 * 1024) { }

    // now and maxBytes: the tests move the clock and use small files.
    internal LogWriter(Func<DateTime> now, long maxBytes)
    {
        _now = now;
        _maxBytes = maxBytes;
    }

    public void Write(string level, string text)
    {
        lock (_sync)
        {
            var now = _now();
            if (level == _lastLevel && text == _lastText)
            {
                _repeats++;
                if (now - _repeatSince < SummaryEvery) return;
                Summarize(now);
                return;
            }
            if (_repeats > 0) Summarize(now);
            _lastLevel = level;
            _lastText = text;
            _repeatSince = now;
            Emit(now, level, text);
        }
    }

    void Summarize(DateTime now)
    {
        Emit(now, _lastLevel, "(a mensagem anterior se repetiu " + _repeats + (_repeats == 1 ? " vez" : " vezes") + " em " +
                              Math.Max(1, (int)(now - _repeatSince).TotalSeconds) + " s)");
        _repeats = 0;
        _repeatSince = now;
    }

    void Emit(DateTime now, string level, string text)
    {
        string line = now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + level + "] " + text;
        if (ToConsole) Console.WriteLine(line);
        if (FilePath == null) return;
        try
        {
            string entry = line + Environment.NewLine;
            var file = new FileInfo(FilePath);
            if (file.Exists && file.Length + System.Text.Encoding.UTF8.GetByteCount(entry) > _maxBytes) Rotate();
            File.AppendAllText(FilePath, entry);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
    }

    // fanpilot.log -> fanpilot.1.log -> … -> fanpilot.3.log, the oldest dropped.
    void Rotate()
    {
        string oldest = Generation(Generations);
        if (File.Exists(oldest)) File.Delete(oldest);
        for (int i = Generations - 1; i >= 1; i--)
            if (File.Exists(Generation(i))) File.Move(Generation(i), Generation(i + 1));
        File.Move(FilePath, Generation(1));
    }

    string Generation(int n)
    {
        return Path.Combine(Path.GetDirectoryName(FilePath), Path.GetFileNameWithoutExtension(FilePath) + "." + n + Path.GetExtension(FilePath));
    }
}

// Problems the user has to see in the panel, not only in the log: an invalid config.json, a damaged state file.
static class AppNotices
{
    static readonly object Sync = new object();
    static readonly List<string> Items = new List<string>();

    public static void Add(string message)
    {
        Log.Warn(message);
        lock (Sync) if (!Items.Contains(message)) Items.Add(message);
    }

    public static List<string> All() { lock (Sync) return Items.ToList(); }
}

static class App
{
    public static int Run(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        if (args.Contains("--version"))
        {
            Console.WriteLine("FanPilot " + AppVersion.Text);
            return 0;
        }
        bool fake = FakeBoardRequested(args);
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        // The fake board writes its own curves, backup and pairings: never into the real board's data\.
        string dataDir = Path.Combine(baseDir, fake ? "data-fake" : "data");
        Directory.CreateDirectory(dataDir);
        Log.FilePath = Path.Combine(dataDir, "fanpilot.log");
        Log.Info("FanPilot " + AppVersion.Text + (fake ? " (placa de teste)" : ""));

        if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
        {
            Console.WriteLine("FanPilot precisa rodar como Administrador (o acesso aos sensores e fans da placa-mãe exige).");
            return 2;
        }
        bool diagnose = args.Contains("--diagnose");
        bool interactive = !args.Contains("--no-browser");
        // One FanPilot at a time: a second one would fight the first over the fans. --diagnose only reads.
        if (!diagnose && !SingleInstance.TryAcquire(fake))
        {
            if (args.Contains("--hardware-test"))
            {
                Console.WriteLine("Feche o FanPilot antes do teste em hardware: os dois disputariam os fans.");
                return 4;
            }
            string panel = "http://localhost:" + AppConfig.Load(Path.Combine(dataDir, "config.json")).Port + "/";
            Console.WriteLine("O FanPilot já está rodando. Painel: " + panel);
            if (interactive) try { Process.Start(panel); } catch { }
            return 4;
        }
        MachineData.Check(dataDir);
        var cfg = AppConfig.Load(Path.Combine(dataDir, "config.json"));
        if (diagnose) return Diagnose.Run(dataDir, cfg, SingleInstance.IsRunning(fake));
        if (args.Contains("--hardware-test"))
        {
            // The fake board must survive the test's "open the board again" step, as a chip would.
            var fakeBoard = fake ? FakeBoard.Demo() : null;
            return HardwareTest.Run(args, cfg, dataDir, () => fake ? fakeBoard : Boards.Create(cfg, false),
                                    fake ? new FakeGpu(cfg.GpuSafetyTemp) : GpuMonitor.Create(cfg.GpuSafetyTemp));
        }

        // The port before the hardware: a port taken by another program must fail before anything touches the fans.
        var server = new WebServer(cfg, Path.Combine(baseDir, "wwwroot"));
        try { server.Start(); }
        catch (HttpListenerException ex)
        {
            Console.WriteLine(WebServer.PortProblem(cfg.Port, ex));
            Log.Warn(WebServer.PortProblem(cfg.Port, ex));
            return 5;
        }

        IBoard bridge;
        try { bridge = fake ? FakeBoard.Demo() : Boards.Create(cfg, interactive); }
        catch (InvalidOperationException ex) { Console.WriteLine(ex.Message); return 3; }
        Log.Info("Placa-mãe via " + bridge.BackendName + (bridge.HardwareCurves ? " (curvas no chip)" : " (curvas executadas pelo FanPilot)"));
        if (bridge.Notice != null) Log.Warn(bridge.Notice);

        var fans = new FanManager(bridge, cfg, dataDir);
        var gpu = fake ? new FakeGpu(cfg.GpuSafetyTemp) { Animated = true } : GpuMonitor.Create(cfg.GpuSafetyTemp);
        var auth = new AuthManager(dataDir, cfg);
        var sampler = new Sampler(bridge, gpu);

        var stop = new ManualResetEvent(false);
        // Software-curve boards must get their fans back on every way out: hook all the ones Windows tells us about.
        Exit.Register(stop, () => { server.Stop(); bridge.Shutdown(); gpu.Stop(); gpu.Shutdown(); });

        try { fans.Start(); } catch (Exception ex) { Log.Warn("Fan manager: " + ex.Message); }
        gpu.Start();
        sampler.Start();
        // Only now: until Start() has saved the original curves, no request may change a fan.
        server.Attach(auth, sampler, fans, gpu);

        string pairUrl = "http://localhost:" + cfg.Port + "/pair";
        Log.Info("FanPilot rodando na porta " + cfg.Port + ". Endereços na rede: " + string.Join(", ", NetworkInfo.CandidateHosts(cfg).Select(h => "http://" + h + ":" + cfg.Port)));
        Log.Info("Para parear o celular, abra " + pairUrl + " neste PC e escaneie o QR code.");
        if (interactive) try { Process.Start(pairUrl); } catch { }

        stop.WaitOne();
        Log.Info(bridge.HardwareCurves
            ? "Encerrando: fan da GPU volta para automático; curvas da placa-mãe permanecem no chip (com ponto de segurança)."
            : "Encerrando: fans da placa-mãe e da GPU voltam ao controle automático (BIOS/driver).");
        Exit.Cleanup();
        return 0;
    }

    // Debug builds only: the whole app on FakeBoard/FakeGpu, for the end-to-end tests and UI work without hardware.
    static bool FakeBoardRequested(string[] args)
    {
#if DEBUG
        return args.Contains("--fake-board");
#else
        return false;
#endif
    }
}

static class Boards
{
    // "auto": Gigabyte Control Center when installed (curves in hardware), else LibreHardwareMonitor.
    // interactive: a person is at the console, so a missing PawnIO driver can be installed right away.
    public static IBoard Create(AppConfig cfg, bool interactive)
    {
        bool gcc = File.Exists(Path.Combine(Program.GccLibDir, GigabyteApi.EngineDll));
        var api = gcc && cfg.Backend != "lhm" ? GigabyteApi.Load(Program.GccLibDir) : null;
        if (Choose(cfg.Backend, gcc, api) == "gigabyte")
        {
            Log.Info("Gigabyte Control Center " + api.Version + " em " + Program.GccLibDir);
            return new GigabyteBridge(api);
        }
        PawnIoSetup.OfferInstall(interactive);
        return new LhmBoard();
    }

    // "gigabyte" or "lhm". An installed but incompatible GCC (an update changed what FanPilot uses) falls back to
    // LibreHardwareMonitor with a notice in "auto", and stops with the reason when config.json insists on "gigabyte".
    internal static string Choose(string backend, bool gccInstalled, GigabyteApi api)
    {
        if (backend == "lhm") return "lhm";
        if (!gccInstalled)
        {
            if (backend == "gigabyte")
                throw new InvalidOperationException("config.json pede \"backend\": \"gigabyte\", mas o Gigabyte Control Center não foi encontrado (procurado em " + Program.GccLibDir + ").");
            return "lhm";
        }
        if (api.Compatible) return "gigabyte";
        string problem = "O Gigabyte Control Center " + api.Version + " não é compatível com este FanPilot (" + api.Problem + ")";
        if (backend == "gigabyte") throw new InvalidOperationException(problem + ".");
        AppNotices.Add(problem + ": usando o LibreHardwareMonitor.");
        return "lhm";
    }
}

// One FanPilot per PC: a second one would fight the first over the fans (and fail on the port anyway).
static class SingleInstance
{
    // Global\: visible from every session, so a FanPilot running as a service (session 0) counts too.
    const string Name = @"Global\FanPilot-e717e36d-9b66-43c9-a08b-96d2af1a9b7c";
    // Held for the whole run. A local variable could be collected, and finalizing the Mutex would let a second one in.
    static Mutex _held;

    public static bool TryAcquire(bool fake)
    {
        _held = TryAcquire(fake ? Name + "-fake" : Name);
        return _held != null;
    }

    public static bool IsRunning(bool fake) { return IsRunning(fake ? Name + "-fake" : Name); }

    internal static Mutex TryAcquire(string name)
    {
        var mutex = new Mutex(false, name);
        try
        {
            if (mutex.WaitOne(0)) return mutex;
            mutex.Dispose();
            return null;
        }
        catch (AbandonedMutexException)
        {
            // The previous FanPilot ended without releasing it (killed, crashed): the mutex is ours now.
            Log.Warn("O FanPilot anterior terminou sem encerrar normalmente.");
            return mutex;
        }
    }

    internal static bool IsRunning(string name)
    {
        Mutex mutex;
        if (!Mutex.TryOpenExisting(name, out mutex)) return false;
        mutex.Dispose();
        return true;
    }
}

// Runs the cleanup once on Ctrl+C, closing the console window, logoff, shutdown, or an unhandled exception.
// A forced kill (Task Manager "End task" on the process, a crash of the runtime) cannot be intercepted.
static class Exit
{
    delegate bool ConsoleCtrlHandler(int ctrlType);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetConsoleCtrlHandler(ConsoleCtrlHandler handler, bool add);

    const int CtrlC = 0, CtrlBreak = 1, CtrlClose = 2, CtrlLogoff = 5, CtrlShutdown = 6;
    static ConsoleCtrlHandler _handler; // kept referenced so the GC doesn't collect the callback
    static Action _cleanup;
    static int _done;

    public static void Register(ManualResetEvent stop, Action cleanup)
    {
        _cleanup = cleanup;
        _handler = type =>
        {
            if (type == CtrlC || type == CtrlBreak) { stop.Set(); return true; } // Main cleans up and returns
            Cleanup(); // close/logoff/shutdown: Windows ends the process right after this returns
            return true;
        };
        SetConsoleCtrlHandler(_handler, true);
        AppDomain.CurrentDomain.ProcessExit += (s, e) => Cleanup();
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            Log.Error("Erro fatal: " + e.ExceptionObject);
            Cleanup();
        };
    }

    public static void Cleanup()
    {
        if (Interlocked.Exchange(ref _done, 1) == 1 || _cleanup == null) return;
        try { _cleanup(); } catch (Exception ex) { Log.Warn("Encerramento: " + ex.Message); }
    }
}

static class NetworkInfo
{
    // IPv4 addresses a phone on the LAN can reach; interfaces with a default gateway first, virtual adapters last.
    public static List<string> CandidateHosts(AppConfig cfg)
    {
        var hosts = new List<KeyValuePair<int, string>>();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            var props = ni.GetIPProperties();
            bool hasGateway = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
            bool isVirtual = ni.Description.IndexOf("virtual", StringComparison.OrdinalIgnoreCase) >= 0 || ni.Name.StartsWith("vEthernet", StringComparison.OrdinalIgnoreCase);
            foreach (var a in props.UnicastAddresses)
                if (a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address))
                    hosts.Add(new KeyValuePair<int, string>((hasGateway ? 0 : 2) + (isVirtual ? 1 : 0), a.Address.ToString()));
        }
        var result = hosts.OrderBy(h => h.Key).Select(h => h.Value).Distinct().ToList();
        if (!string.IsNullOrEmpty(cfg.PublicHost)) { result.Remove(cfg.PublicHost); result.Insert(0, cfg.PublicHost); }
        return result;
    }
}

// Polls hardware once per second and hands the snapshot to the subscribers (the SSE hub, which never blocks);
// keeps 10 min of history. The GPU comes from its own loop (GpuMonitor.Latest), safety rule included.
sealed class Sampler
{
    const int HistorySize = 600;
    static readonly TimeSpan SlowSample = TimeSpan.FromSeconds(3);
    DateTime _lastSlowLog = DateTime.MinValue;
    // The log records the board going away and coming back, not every second it stays away.
    bool _boardDown;
    readonly IBoard _bridge;
    readonly GpuMonitor _gpu;
    readonly object _sync = new object();
    readonly LinkedList<Dictionary<string, object>> _history = new LinkedList<Dictionary<string, object>>();
    readonly List<Action<string>> _subscribers = new List<Action<string>>();
    volatile string _latestJson = "{}";
    volatile bool _stopped;
    Thread _thread;

    public Sampler(IBoard bridge, GpuMonitor gpu) { _bridge = bridge; _gpu = gpu; }

    public string LatestJson { get { return _latestJson; } }

    public void Start()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "sampler" };
        _thread.Start();
    }

    public void Stop() { _stopped = true; }

    public void Subscribe(Action<string> s) { lock (_sync) _subscribers.Add(s); }
    public void Unsubscribe(Action<string> s) { lock (_sync) _subscribers.Remove(s); }

    public List<Dictionary<string, object>> History() { lock (_sync) return _history.ToList(); }

    void Loop()
    {
        while (!_stopped)
        {
            var started = DateTime.UtcNow;
            try { Publish(Sample()); }
            catch (Exception ex) { Log.Warn("Sampler: " + ex.Message); }
            var took = DateTime.UtcNow - started;
            // Usually the Gigabyte service answering slowly: worth knowing, not worth a line per second.
            if (took > SlowSample && DateTime.UtcNow - _lastSlowLog > TimeSpan.FromMinutes(1))
            {
                _lastSlowLog = DateTime.UtcNow;
                Log.Warn("Leitura dos sensores lenta: " + (int)took.TotalMilliseconds + " ms.");
            }
            int wait = 1000 - (int)took.TotalMilliseconds;
            if (wait > 0) Thread.Sleep(wait);
        }
    }

    Dictionary<string, object> Sample()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        long t = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        var snap = new Dictionary<string, object> { { "t", t } };
        var service = new Dictionary<string, object> { { "ok", true }, { "backend", _bridge.BackendName } };
        if (_bridge.Notice != null) service["notice"] = _bridge.Notice;
        var notices = AppNotices.All();
        if (notices.Count > 0) service["notices"] = notices;
        try
        {
            snap["sensors"] = _bridge.ReadSensors().Select(s => new Dictionary<string, object> {
                { "id", s.Id }, { "type", s.Type }, { "title", s.Title }, { "value", s.Value }, { "unit", s.Unit } }).ToList();
            if (_bridge.FanControlSupported)
            {
                List<float> temps, rpms;
                _bridge.ReadFanTach(out temps, out rpms);
                snap["fanRpm"] = rpms.Select(v => (int)Math.Round(v)).ToList();
                snap["tempInputs"] = temps.Select(v => Math.Round(v, 1)).ToList();
            }
            if (_boardDown)
            {
                _boardDown = false;
                Log.Info("Sensores da placa voltaram a responder.");
            }
        }
        catch (Exception ex)
        {
            service["ok"] = false;
            service["error"] = ex.Message;
            if (!_boardDown)
            {
                _boardDown = true;
                Log.Error("Sensores da placa indisponíveis: " + ex.Message);
            }
        }
        service["sampleMs"] = watch.ElapsedMilliseconds;
        snap["service"] = service;
        snap["gpu"] = _gpu.Latest;
        return snap;
    }

    void Publish(Dictionary<string, object> snap)
    {
        string json = Json.Serialize(snap);
        _latestJson = json;
        List<Action<string>> subs;
        lock (_sync)
        {
            _history.AddLast(snap);
            while (_history.Count > HistorySize) _history.RemoveFirst();
            subs = _subscribers.ToList();
        }
        foreach (var s in subs) s(json);
    }
}
