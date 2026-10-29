// FanPilot.exe --service: the same FanPilot under the Service Control Manager, as LocalSystem in session 0, from boot
// and with no console. The installer (item 14c) registers it with --data %ProgramData%\FanPilot and the recovery
// actions (restart after 1 s, 5 s and 60 s), so Windows brings it back if it dies.
using System;
using System.Diagnostics;
using System.ServiceProcess;
using System.Threading;

sealed class FanPilotService : ServiceBase
{
    public const string Name = "FanPilot";
    readonly FanPilotHost _host;

    public FanPilotService(FanPilotHost host)
    {
        _host = host;
        ServiceName = Name;
        CanStop = true;
        CanShutdown = true;
        CanHandlePowerEvent = true;
        AutoLog = true; // started and stopped, in the Windows event log
    }

    // stop: unused here, the Service Control Manager stops the service (FanPilotService.Linux.cs waits on it).
    public static int Run(FanPilotHost host, ManualResetEvent stop)
    {
        // Warnings and errors also go to the event log ("FanPilot" in Application): there is no console to see them.
        Log.Mirror = (level, text) =>
        {
            if (level == "INFO") return;
            try { EventLog.WriteEntry(Name, text, level == "ERROR" ? EventLogEntryType.Error : EventLogEntryType.Warning); } catch { }
        };
        ServiceBase.Run(new FanPilotService(host));
        return 0;
    }

    // What autostart.ps1 registered before item 30: a task that opened FanPilot.exe in a window at every logon.
    static bool OldTaskExists()
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", "/Query /TN FanPilot") {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var p = Process.Start(psi))
            {
                p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                return p.WaitForExit(10000) && p.ExitCode == 0;
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException) { return false; }
    }

    protected override void OnStart(string[] args)
    {
        // Opening the board can take seconds (the Gigabyte service over IPC): Windows is answered at once.
        new Thread(() =>
        {
            int code = _host.Start();
            if (code == 0)
            {
                if (OldTaskExists())
                    Log.Warn("The scheduled task 'FanPilot' of the old autostart.ps1 still exists: at logon it opens a second FanPilot " +
                             "in a window. Run scripts\\autostart.ps1 again (as Administrator) to remove it.");
                return;
            }
            ExitCode = code;
            Stop();
        }) { IsBackground = true, Name = "start" }.Start();
    }

    protected override void OnStop() { _host.Stop(); }

    protected override void OnShutdown() { _host.Stop(); }

    protected override bool OnPowerEvent(PowerBroadcastStatus status)
    {
        if (status == PowerBroadcastStatus.ResumeSuspend || status == PowerBroadcastStatus.ResumeAutomatic) _host.Resumed();
        return true;
    }
}
