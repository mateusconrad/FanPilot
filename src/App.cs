using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;

static class Log
{
    static readonly object Sync = new object();
    public static string FilePath;

    static void Write(string level, string msg)
    {
        string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + level + "] " + msg;
        lock (Sync)
        {
            Console.WriteLine(line);
            if (FilePath != null) try { File.AppendAllText(FilePath, line + Environment.NewLine); } catch { }
        }
    }

    public static void Info(string msg) { Write("INFO", msg); }
    public static void Warn(string msg) { Write("WARN", msg); }
}

static class App
{
    public static int Run(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        bool fake = FakeBoardRequested(args);
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        // The fake board writes its own curves, backup and pairings: never into the real board's data\.
        string dataDir = Path.Combine(baseDir, fake ? "data-fake" : "data");
        Directory.CreateDirectory(dataDir);
        Log.FilePath = Path.Combine(dataDir, "fanpilot.log");

        if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
        {
            Console.WriteLine("FanPilot precisa rodar como Administrador (o acesso aos sensores e fans da placa-mãe exige).");
            return 2;
        }
        MachineData.Check(dataDir);
        var cfg = AppConfig.Load(Path.Combine(dataDir, "config.json"));
        if (args.Contains("--diagnose")) return Diagnose.Run(dataDir, cfg);

        IBoard bridge;
        try { bridge = fake ? FakeBoard.Demo() : Boards.Create(cfg, !args.Contains("--no-browser")); }
        catch (InvalidOperationException ex) { Console.WriteLine(ex.Message); return 3; }
        Log.Info("Placa-mãe via " + bridge.BackendName + (bridge.HardwareCurves ? " (curvas no chip)" : " (curvas executadas pelo FanPilot)"));
        if (bridge.Notice != null) Log.Warn(bridge.Notice);

        var fans = new FanManager(bridge, cfg, dataDir);
        var gpu = fake ? new FakeGpu(cfg.GpuSafetyTemp) { Animated = true } : GpuMonitor.Create(cfg.GpuSafetyTemp);
        var auth = new AuthManager(dataDir, cfg);
        var sampler = new Sampler(bridge, gpu);
        var server = new WebServer(cfg, Path.Combine(baseDir, "wwwroot"), auth, sampler, fans, gpu);

        var stop = new ManualResetEvent(false);
        // Software-curve boards must get their fans back on every way out: hook all the ones Windows tells us about.
        Exit.Register(stop, () => { server.Stop(); bridge.Shutdown(); gpu.Shutdown(); });

        try { fans.Start(); } catch (Exception ex) { Log.Warn("Fan manager: " + ex.Message); }
        sampler.Start();
        server.Start();

        string pairUrl = "http://localhost:" + cfg.Port + "/pair";
        Log.Info("FanPilot rodando na porta " + cfg.Port + ". Endereços na rede: " + string.Join(", ", NetworkInfo.CandidateHosts(cfg).Select(h => "http://" + h + ":" + cfg.Port)));
        Log.Info("Para parear o celular, abra " + pairUrl + " neste PC e escaneie o QR code.");
        if (!args.Contains("--no-browser")) try { Process.Start(pairUrl); } catch { }

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
        bool gcc = Directory.Exists(Program.GccLibDir);
        if (cfg.Backend == "gigabyte" && !gcc)
            throw new InvalidOperationException("config.json pede \"backend\": \"gigabyte\", mas o Gigabyte Control Center não foi encontrado (procurado em " + Program.GccLibDir + ").");
        if (cfg.Backend == "gigabyte" || (cfg.Backend == "auto" && gcc)) return CreateGigabyte(interactive);
        return CreateLhm(interactive);
    }

    static IBoard CreateLhm(bool interactive)
    {
        PawnIoSetup.OfferInstall(interactive);
        return new LhmBoard();
    }

#if NO_GIGABYTE
    // Built without Gigabyte Control Center installed (build.ps1 warns): only LibreHardwareMonitor is available.
    static IBoard CreateGigabyte(bool interactive)
    {
        Log.Warn("Este FanPilot foi compilado sem suporte ao Gigabyte Control Center; usando o LibreHardwareMonitor.");
        return CreateLhm(interactive);
    }
#else
    // Separate and never inlined: Gigabyte's assemblies only exist where GCC is installed, so nothing that touches
    // their types may be compiled on the LibreHardwareMonitor path.
    [MethodImpl(MethodImplOptions.NoInlining)]
    static IBoard CreateGigabyte(bool interactive) { return new GigabyteBridge(); }
#endif
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
            Log.Warn("Erro fatal: " + e.ExceptionObject);
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

// Polls hardware once per second and fans the snapshot out to Server-Sent-Events clients; keeps 10 min of history.
sealed class Sampler
{
    const int HistorySize = 600;
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
            int wait = 1000 - (int)(DateTime.UtcNow - started).TotalMilliseconds;
            if (wait > 0) Thread.Sleep(wait);
        }
    }

    Dictionary<string, object> Sample()
    {
        long t = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        var snap = new Dictionary<string, object> { { "t", t } };
        var service = new Dictionary<string, object> { { "ok", true }, { "backend", _bridge.BackendName } };
        if (_bridge.Notice != null) service["notice"] = _bridge.Notice;
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
        }
        catch (Exception ex) { service["ok"] = false; service["error"] = ex.Message; }
        snap["service"] = service;

        var gpu = _gpu.Read();
        object gpuTemp;
        if (gpu.TryGetValue("temp", out gpuTemp)) _gpu.EnforceSafety(gpuTemp);
        snap["gpu"] = gpu;
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
