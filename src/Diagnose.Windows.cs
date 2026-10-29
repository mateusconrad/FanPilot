// --diagnose on Windows: opens LibreHardwareMonitor, lists what it found and how FanPilot maps it (reference sensors,
// controllable headers), and where the GCC is.
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using LibreHardwareMonitor.Hardware;

static partial class Diagnose
{
    // Where GCC is, its version, and whether this FanPilot can use it (what an update broke, if anything).
    static Msg GccLine()
    {
        if (!File.Exists(Path.Combine(GigabyteInstall.LibDir, GigabyteApi.EngineDll))) return new Msg("diag.gcc.missing");
        var api = GigabyteApi.Load(GigabyteInstall.LibDir);
        return api.Compatible ? new Msg("diag.gcc.ok", "dir", GigabyteInstall.LibDir, "version", api.Version)
                              : new Msg("diag.gcc.incompatible", "dir", GigabyteInstall.LibDir, "version", api.Version, "problem", api.Problem);
    }

    // otherInstance: a FanPilot is running and may be driving the fans while this reads them. interactive: a person
    // is at the console (no --no-browser), so the report opens in Notepad.
    public static int Run(string dataDir, AppConfig cfg, bool otherInstance, bool interactive)
    {
        string lang = I18n.SystemLanguage;
        string T(string key, params object[] args) { return new Msg(key, args).In(lang); }
        string path = Path.Combine(dataDir, "diagnostico-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        var sb = new StringBuilder();
        sb.AppendLine("FanPilot " + AppVersion.Text + " --diagnose  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        // In the file too: started from a terminal that is not elevated, the console window closes before it can be read.
        sb.AppendLine(T("diag.howToReport", "url", ReportUrl(MachineData.Board)));
        sb.AppendLine(T("diag.redacted"));
        if (otherInstance) sb.AppendLine(T("diag.otherInstance"));
        sb.AppendLine("Windows: " + Environment.OSVersion.VersionString + (Environment.Is64BitOperatingSystem ? " x64" : ""));
        sb.AppendLine(T("diag.board", "board", MachineData.Board));
        sb.AppendLine("Gigabyte Control Center: " + GccLine().In(lang));
        sb.AppendLine(T("diag.backend", "backend", cfg.Backend));
        bool pawnIo = false;
        try
        {
            pawnIo = LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled;
            sb.AppendLine(pawnIo ? T("diag.pawnio.installed", "version", LibreHardwareMonitor.PawnIo.PawnIo.Version) : T("diag.pawnio.missing"));
        }
        catch (Exception ex) { sb.AppendLine(T("diag.pawnio.error", "detail", ex.Message)); }
        sb.AppendLine();

        // How FanPilot would see this board (the same class the app uses; no curve is set, so nothing is written).
        var board = new LhmBoard();
        try { DescribeBoard(sb, board, "diag.section.board", lang); }
        finally { board.Shutdown(); }
        sb.AppendLine();

        sb.AppendLine(T("diag.section.lhm"));
        var pc = new Computer { IsMotherboardEnabled = true, IsCpuEnabled = true, IsGpuEnabled = true, IsControllerEnabled = true };
        try
        {
            pc.Open();
            sb.AppendLine(pc.GetReport());
        }
        catch (Exception ex) { sb.AppendLine(T("diag.failed", "detail", ex.ToString())); }
        finally { try { pc.Close(); } catch { } }

        File.WriteAllText(path, Redact(sb.ToString(), lang), Encoding.UTF8);
        Console.WriteLine(T("diag.saved", "path", path));
        Console.WriteLine(T("diag.nothingChanged"));
        Console.WriteLine(ReportUrl(MachineData.Board));
        if (interactive) try { Process.Start("notepad.exe", "\"" + path + "\""); } catch { }
        return 0;
    }
}
