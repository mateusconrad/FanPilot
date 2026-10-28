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
}
