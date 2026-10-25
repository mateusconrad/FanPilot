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
    // Also called with every line written (level, text): the service copies warnings and errors to the event log.
    public static Action<string, string> Mirror { get { return Writer.Mirror; } set { Writer.Mirror = value; } }

    public static void Info(string msg) { Writer.Write("INFO", msg); }
    public static void Warn(string msg) { Writer.Write("WARN", msg); }
    // Something took the fans out of FanPilot's control (handed back to the BIOS, the board stopped answering).
    public static void Error(string msg) { Writer.Write("ERROR", msg); }

    // A line for the person at the console as well (item 21): the window shows it in Windows' language, the log file
    // keeps it in English like every other line.
    public static void Info(Msg m) { Writer.Write("INFO", m.Text, m.In(I18n.Windows)); }
    public static void Warn(Msg m) { Writer.Write("WARN", m.Text, m.In(I18n.Windows)); }
    public static void Error(Msg m) { Writer.Write("ERROR", m.Text, m.In(I18n.Windows)); }
}

// The console and data\fanpilot.log, rotated by size (fanpilot.1.log … fanpilot.3.log). The same line repeated in a
// row is written once and then summarized every minute ("repeated N times"), so a failure that recurs every second
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
    public Action<string, string> Mirror;

    public LogWriter() : this(() => DateTime.Now, 5 * 1024 * 1024) { }

    // now and maxBytes: the tests move the clock and use small files.
    internal LogWriter(Func<DateTime> now, long maxBytes)
    {
        _now = now;
        _maxBytes = maxBytes;
    }

    // console: the line as the person at the window reads it (Log.Info(Msg)); null: the same text.
    public void Write(string level, string text, string console = null)
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
            Emit(now, level, text, console);
        }
    }

    void Summarize(DateTime now)
    {
        Emit(now, _lastLevel, "(the previous message repeated " + _repeats + (_repeats == 1 ? " time" : " times") + " in " +
                              Math.Max(1, (int)(now - _repeatSince).TotalSeconds) + " s)");
        _repeats = 0;
        _repeatSince = now;
    }

    void Emit(DateTime now, string level, string text, string console = null)
    {
        string stamp = now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + level + "] ";
        string line = stamp + text;
        if (ToConsole) Console.WriteLine(console == null ? line : stamp + console);
        var mirror = Mirror;
        if (mirror != null) try { mirror(level, text); } catch { }
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
    static readonly List<Msg> Items = new List<Msg>();

    public static void Add(Msg message)
    {
        Log.Warn(message);
        lock (Sync) if (!Items.Any(m => m.Text == message.Text)) Items.Add(message);
    }

    public static List<Msg> All() { lock (Sync) return Items.ToList(); }
}

static class App
{
    public static int Run(string[] args)
    {
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch (IOException) { } // a service has no console
        AppOptions o;
        try { o = AppOptions.Parse(args, FakeBoardAllowed); }
        catch (ArgumentException ex)
        {
            Console.WriteLine(Msg.TextOf(ex, I18n.Windows));
            return 1;
        }
        if (o.Version)
        {
            Console.WriteLine("FanPilot " + AppVersion.Text);
            return 0;
        }
        string scenario = o.FakeScenario;
        bool fake = o.Fake;
        if (fake && !FakeBoard.Scenarios.Contains(scenario))
        {
            Console.WriteLine(new Msg("console.unknownScenario", "scenario", scenario, "list", string.Join(", ", FakeBoard.Scenarios)).In(I18n.Windows));
            return 1;
        }
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        // The fake board writes its own curves, backup and pairings: never into the real board's data\.
        string dataDir = DataDirectory.Resolve(o.DataDir, baseDir, fake);
        if (o.Service) DataDirectory.Restrict(dataDir); else Directory.CreateDirectory(dataDir);
        Log.FilePath = Path.Combine(dataDir, "fanpilot.log");
        Log.Info("FanPilot " + AppVersion.Text + (fake ? " (fake board: " + scenario + ")" : "") + (o.Service ? " as a service" : "") +
                 "; data in " + dataDir);

        // The fake board touches no hardware, so it also runs without Administrator, on localhost only (phones need
        // the "+" prefix, which needs Administrator): that is how the end-to-end tests run on a developer's PC.
        bool admin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        if (!admin && (!fake || o.Diagnose))
        {
            Console.WriteLine(new Msg("console.admin").In(I18n.Windows));
            return 2;
        }
        if (o.ImportFrom != null) return DataImport.Run(o.ImportFrom, dataDir, SingleInstance.IsRunning(fake));
        // One FanPilot at a time: a second one would fight the first over the fans. --diagnose only reads.
        if (!o.Diagnose && !SingleInstance.TryAcquire(fake))
        {
            if (o.HardwareTest)
            {
                Console.WriteLine(new Msg("console.hwtestBusy").In(I18n.Windows));
                return 4;
            }
            string panel = "http://localhost:" + AppConfig.Load(Path.Combine(dataDir, "config.json")).Port + "/";
            Console.WriteLine(new Msg("console.alreadyRunning", "url", panel).In(I18n.Windows));
            if (o.Interactive) try { Process.Start(panel); } catch { }
            return 4;
        }
        MachineData.Check(dataDir);
        var cfg = AppConfig.Load(Path.Combine(dataDir, "config.json"));
        if (o.Diagnose) return Diagnose.Run(dataDir, cfg, SingleInstance.IsRunning(fake), o.Interactive);
        if (o.HardwareTest)
        {
            // The fake board must survive the test's "open the board again" step, as a chip would.
            var fakeBoard = fake ? FakeBoard.Scenario(scenario) : null;
            return HardwareTest.Run(args, cfg, dataDir, () => fake ? fakeBoard : Boards.Create(cfg, false),
                                    fake ? FakeGpu.Scenario(scenario, cfg.GpuSafetyTemp) : GpuMonitor.Create(cfg.GpuSafetyTemp));
        }

        var host = new FanPilotHost(cfg, Path.Combine(baseDir, "wwwroot"), dataDir,
            () => fake ? FakeBoard.Scenario(scenario) : Boards.Create(cfg, o.Interactive),
            () => fake ? (GpuMonitor)FakeGpu.Scenario(scenario, cfg.GpuSafetyTemp) : GpuMonitor.Create(cfg.GpuSafetyTemp),
            o.Interactive, admin);
        var stop = new ManualResetEvent(false);
        // Software-curve boards must get their fans back on every way out: hook all the ones Windows tells us about,
        // before anything starts.
        Exit.Register(stop, host.Stop);
        if (o.Service) return FanPilotService.Run(host);
        int code = host.Start();
        if (code != 0) return code;
        stop.WaitOne();
        Exit.Cleanup();
        return 0;
    }

    // --fake-board: the whole app on FakeBoard/FakeGpu, for the end-to-end tests and UI work without hardware. Debug
    // builds only.
#if DEBUG
    const bool FakeBoardAllowed = true;
#else
    const bool FakeBoardAllowed = false;
#endif
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
            Log.Info("Gigabyte Control Center " + api.Version + " at " + Program.GccLibDir);
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
                throw new Unavailable("startup.gccMissing", "dir", Program.GccLibDir);
            return "lhm";
        }
        if (api.Compatible) return "gigabyte";
        if (backend == "gigabyte") throw new Unavailable("gcc.incompatible", "version", api.Version, "problem", api.Problem);
        AppNotices.Add(new Msg("notice.gccFallback", "version", api.Version, "problem", api.Problem));
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
            Log.Warn("The previous FanPilot ended without shutting down cleanly.");
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
            Log.Error("Fatal error: " + e.ExceptionObject);
            Cleanup();
        };
    }

    public static void Cleanup()
    {
        if (Interlocked.Exchange(ref _done, 1) == 1 || _cleanup == null) return;
        try { _cleanup(); } catch (Exception ex) { Log.Warn("Shutdown: " + ex.Message); }
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
    volatile Dictionary<string, object> _latest;
    volatile bool _stopped;
    Thread _thread;
    // The active alerts (item 16), put in every snapshot so the panel's banner follows them live.
    public Func<List<Dictionary<string, object>>> Alerts;

    public Sampler(IBoard bridge, GpuMonitor gpu) { _bridge = bridge; _gpu = gpu; }

    public string LatestJson { get { return _latestJson; } }
    // The same snapshot, not serialized (never changed after it is published); null before the first one.
    public Dictionary<string, object> Latest { get { return _latest; } }

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
                Log.Warn("Slow sensor reading: " + (int)took.TotalMilliseconds + " ms.");
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
        if (_bridge.Notice != null) service["notice"] = _bridge.Notice.ToJson();
        var notices = AppNotices.All();
        if (notices.Count > 0) service["notices"] = notices.Select(m => m.ToJson()).ToList();
        try
        {
            snap["sensors"] = _bridge.ReadSensors().Select(s =>
            {
                var d = new Dictionary<string, object> { { "id", s.Id }, { "type", s.Type }, { "title", s.Title }, { "value", s.Value }, { "unit", s.Unit } };
                if (s.Role != null) d["role"] = s.Role;
                if (s.TitleKey != null) d["titleKey"] = s.TitleKey;
                if (s.Hint != null) { d["hint"] = s.Hint.Text; d["hintKey"] = s.Hint.Key; }
                return d;
            }).ToList();
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
                Log.Info("Board sensors answering again.");
            }
        }
        catch (Exception ex)
        {
            service["ok"] = false;
            service["error"] = ex.Message;
            if (!_boardDown)
            {
                _boardDown = true;
                Log.Error("Board sensors unavailable: " + ex.Message);
            }
        }
        service["sampleMs"] = watch.ElapsedMilliseconds;
        snap["service"] = service;
        snap["gpu"] = _gpu.Latest;
        var alerts = Alerts;
        if (alerts != null) snap["alerts"] = alerts();
        return snap;
    }

    void Publish(Dictionary<string, object> snap)
    {
        string json = Json.Serialize(snap);
        _latestJson = json;
        _latest = snap;
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
