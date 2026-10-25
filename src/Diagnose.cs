// FanPilot.exe --diagnose: read-only report for boards nobody has tested yet. Opens LibreHardwareMonitor, lists what
// it found and how FanPilot maps it (reference sensors, controllable headers), and saves it to data\ to be attached
// to a public issue: serial numbers and UUIDs are left out. Never changes a fan speed. In Windows' language (item 21).
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using LibreHardwareMonitor.Hardware;

static class Diagnose
{
    const string NewIssueUrl = "https://github.com/mateusconrad/FanPilot/issues/new";

    // The compatibility form (.github/ISSUE_TEMPLATE/compatibilidade.yml) with the board and the version filled in.
    internal static string ReportUrl(string board)
    {
        string title = "[Compatibilidade] " + (string.IsNullOrEmpty(board) ? "placa desconhecida" : board);
        return NewIssueUrl + "?template=compatibilidade.yml&title=" + Uri.EscapeDataString(title) +
               "&placa=" + Uri.EscapeDataString(board ?? "") + "&versao=" + Uri.EscapeDataString(AppVersion.Text);
    }

    // What identifies this very machine rather than its model: the SMBIOS serial numbers (board, chassis, processor),
    // asset and service tags, UUIDs, and any GUID elsewhere. LibreHardwareMonitor's report has several of them.
    // No "$": in Multiline mode it matches before "\n" only, and the report's lines end with "\r\n".
    static readonly Regex IdentifyingField = new Regex(@"^(?<key>[^:\r\n]*\b(serial|uuid|guid|asset ?tag|service ?tag)[^:\r\n]*:[ \t]*)\S[^\r\n]*",
                                                       RegexOptions.IgnoreCase | RegexOptions.Multiline);
    static readonly Regex Guid = new Regex(@"\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b", RegexOptions.IgnoreCase);

    internal static string Redact(string report, string lang)
    {
        string omitted = I18n.T(lang, "diag.omitted");
        return Guid.Replace(IdentifyingField.Replace(report, "${key}" + omitted), omitted);
    }
    // Where GCC is, its version, and whether this FanPilot can use it (what an update broke, if anything).
    static Msg GccLine()
    {
        if (!File.Exists(Path.Combine(Program.GccLibDir, GigabyteApi.EngineDll))) return new Msg("diag.gcc.missing");
        var api = GigabyteApi.Load(Program.GccLibDir);
        return api.Compatible ? new Msg("diag.gcc.ok", "dir", Program.GccLibDir, "version", api.Version)
                              : new Msg("diag.gcc.incompatible", "dir", Program.GccLibDir, "version", api.Version, "problem", api.Problem);
    }

    // otherInstance: a FanPilot is running and may be driving the fans while this reads them. interactive: a person
    // is at the console (no --no-browser), so the report opens in Notepad.
    public static int Run(string dataDir, AppConfig cfg, bool otherInstance, bool interactive)
    {
        string lang = I18n.Windows;
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
        try
        {
            sb.AppendLine(T("diag.section.board"));
            sb.AppendLine(T("diag.notice", "notice", board.Notice != null ? board.Notice.In(lang) : T("diag.none")));
            sb.AppendLine(T("diag.inputs"));
            var titles = board.TempInputTitles;
            System.Collections.Generic.List<float> temps, rpms;
            board.ReadFanTach(out temps, out rpms);
            for (int i = 0; i < titles.Length; i++)
                sb.AppendLine("  [" + i + "] " + titles[i] + " = " + (i < temps.Count && !float.IsNaN(temps[i]) ? temps[i].ToString("0.0") + " °C" : T("diag.noReading")));
            sb.AppendLine(T("diag.fans"));
            var curves = board.GetCurves();
            for (int i = 0; i < curves.Count; i++)
                sb.AppendLine("  [" + i + "] " + curves[i].Title + " · " + (i < rpms.Count ? rpms[i].ToString("0") : "?") + " RPM");
            if (curves.Count == 0) sb.AppendLine("  " + T("diag.none"));
            sb.AppendLine(T("diag.readings"));
            foreach (var r in board.ReadSensors()) sb.AppendLine("  " + r.Type + " | " + r.Title + " = " + r.Value + " " + r.Unit + "   (" + r.Id + ")");
        }
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
