// The running FanPilot: web server, board, fans, GPU and sampler. Started in that order (the port before the hardware,
// so a port taken by another program fails before anything touches a fan), and stopped by Stop() on every way out.
// The console (App.Run) and the Windows service (FanPilotService) share it.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;

sealed class FanPilotHost
{
    readonly AppConfig _cfg;
    readonly string _wwwroot, _dataDir;
    readonly Func<IBoard> _openBoard;
    readonly Func<GpuMonitor> _openGpu;
    readonly bool _interactive, _admin;
    readonly object _sync = new object();
    WebServer _server;
    IBoard _bridge;
    GpuMonitor _gpu;
    FanManager _fans;
    Sampler _sampler;
    AlertMonitor _alerts;
    bool _stopped;

    // openBoard: Boards.Create, or the fake board; it may throw InvalidOperationException with the reason.
    public FanPilotHost(AppConfig cfg, string wwwroot, string dataDir, Func<IBoard> openBoard, Func<GpuMonitor> openGpu, bool interactive, bool admin)
    {
        _cfg = cfg; _wwwroot = wwwroot; _dataDir = dataDir; _openBoard = openBoard; _openGpu = openGpu;
        _interactive = interactive; _admin = admin;
    }

    internal FanManager Fans { get { return _fans; } }

    // 0 when running; otherwise the exit code (5: port taken, 3: no board), with the reason on the console and in the log.
    public int Start()
    {
        var server = new WebServer(_cfg, _wwwroot) { ListenHost = _admin ? "+" : "localhost" };
        try { server.Start(); }
        catch (HttpListenerException ex)
        {
            Log.Error(WebServer.PortProblem(_cfg.Port, ex));
            return 5;
        }
        lock (_sync)
        {
            if (_stopped) { server.Stop(); return 0; } // stopped while starting (a service stop during a slow board)
            _server = server;
        }

        if (!_admin) Log.Info(new Msg("console.noAdmin", "url", "http://localhost:" + _cfg.Port + "/"));
        IBoard bridge;
        try { bridge = _openBoard(); }
        catch (InvalidOperationException ex)
        {
            Log.Error(Msg.Of(ex) ?? new Msg("raw", "text", ex.Message));
            return 3;
        }
        Log.Info("Motherboard via " + bridge.BackendName + (bridge.HardwareCurves ? " (curves in the chip)" : " (curves run by FanPilot)"));
        if (bridge.Notice != null) Log.Warn(bridge.Notice);

        var gpu = _openGpu();
        var fans = new FanManager(bridge, _cfg, _dataDir);
        var sampler = new Sampler(bridge, gpu);
        var alerts = new AlertMonitor(sampler, fans, bridge, _dataDir);
        sampler.Alerts = alerts.ActiveForSnapshot;
        lock (_sync)
        {
            _bridge = bridge; _gpu = gpu; _fans = fans; _sampler = sampler; _alerts = alerts;
            if (_stopped) { bridge.Shutdown(); gpu.Shutdown(); return 0; }
        }

        try { fans.Start(); } catch (Exception ex) { Log.Warn("Fan manager: " + ex.Message); }
        gpu.Start();
        sampler.Start();
        alerts.Start();
        // Only now: until Start() has saved the original curves, no request may change a fan.
        server.Attach(new AuthManager(_dataDir, _cfg), sampler, fans, gpu, alerts);

        string pairUrl = "http://localhost:" + _cfg.Port + "/pair";
        Log.Info(new Msg("console.running", "port", _cfg.Port, "urls", string.Join(", ", NetworkInfo.CandidateHosts(_cfg).Select(h => "http://" + h + ":" + _cfg.Port))));
        Log.Info(new Msg("console.pairHint", "url", pairUrl));
        if (_interactive) try { Process.Start(pairUrl); } catch { }
        return 0;
    }

    // Every way out ends here, once: the server closes, a software-curve board hands its headers back to the BIOS,
    // the GPU fan goes back to automatic. Fast: the service gets a few seconds at shutdown, the BIOS hand-back is
    // meant to take under one.
    public void Stop()
    {
        IBoard bridge;
        GpuMonitor gpu;
        WebServer server;
        Sampler sampler;
        AlertMonitor alerts;
        lock (_sync)
        {
            if (_stopped) return;
            _stopped = true;
            bridge = _bridge; gpu = _gpu; server = _server; sampler = _sampler; alerts = _alerts;
        }
        if (alerts != null) alerts.Stop();
        var watch = Stopwatch.StartNew();
        if (bridge != null)
            Log.Info(bridge.HardwareCurves
                ? "Stopping: the GPU fan goes back to automatic; the motherboard's curves stay in the chip (with the safety point)."
                : "Stopping: the motherboard's and the GPU's fans go back to automatic control (BIOS, driver).");
        if (sampler != null) sampler.Stop();
        if (server != null) server.Stop();
        if (bridge != null) bridge.Shutdown();
        if (gpu != null) { gpu.Stop(); gpu.Shutdown(); }
        Log.Info("Stopped in " + watch.ElapsedMilliseconds + " ms.");
    }

    // Back from sleep (the service's power event): the BIOS may have reset the chip. Curves are checked and put back
    // now, not on the watchdog's next round (up to 15 s).
    public void Resumed()
    {
        var fans = _fans;
        if (fans == null) return;
        Log.Info("Back from sleep: curves checked now.");
        fans.Watchdog();
    }
}
