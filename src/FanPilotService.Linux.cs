// FanPilot --service on Linux (item 28): systemd starts it at boot, as root and with no console (the unit comes with
// item 28c), and stops it with SIGTERM, which Exit turns into the same cleanup as Ctrl+C. What it prints goes to the
// journal; fanpilot.log in the data folder keeps it as on Windows. FanPilotService.cs is the Windows service.
using System.Threading;

static class FanPilotService
{
    public static int Run(FanPilotHost host, ManualResetEvent stop)
    {
        int code = host.Start();
        if (code != 0) return code;
        stop.WaitOne();
        Exit.Cleanup();
        return 0;
    }
}
