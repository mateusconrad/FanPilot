// FanPilot.exe --diagnose: read-only report for boards nobody has tested yet, saved to data\ to be attached to a public
// issue: serial numbers and UUIDs are left out. Never changes a fan speed. In the system's language (item 21). What
// it reads is per system: Diagnose.Windows.cs (LibreHardwareMonitor and the GCC) and Diagnose.Linux.cs.
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

static partial class Diagnose
{
    const string NewIssueUrl = "https://github.com/mateusconrad/FanPilot/issues/new";

    // The compatibility form (.github/ISSUE_TEMPLATE/compatibilidade.yml, in English with Portuguese labels) with the
    // board and the version filled in.
    internal static string ReportUrl(string board)
    {
        string title = "[Compatibility] " + (string.IsNullOrEmpty(board) ? "unknown board" : board);
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

    // How FanPilot would see this board (the same class the app uses; no curve is set, so nothing is written).
    static void DescribeBoard(StringBuilder sb, IBoard board, string section, string lang)
    {
        string T(string key, params object[] args) { return new Msg(key, args).In(lang); }
        sb.AppendLine(T(section));
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
}
