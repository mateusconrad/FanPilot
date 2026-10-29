// --diagnose on Linux (item 28b): how FanPilot sees the board through hwmon, then every hwmon chip with its files
// (what a report needs to add a board: names, labels, pwmN_enable), the motherboard drivers loaded and the kernel.
using System;
using System.IO;
using System.Linq;
using System.Text;

static partial class Diagnose
{
    // otherInstance: a FanPilot is running and may be driving the fans while this reads them. interactive: unused here;
    // FanPilot runs as root and opens nothing (Platform.OpenInBrowser).
    public static int Run(string dataDir, AppConfig cfg, bool otherInstance, bool interactive)
    {
        return Run(dataDir, cfg, otherInstance, "/");
    }

    // root: "/" (the tests: a fake one).
    internal static int Run(string dataDir, AppConfig cfg, bool otherInstance, string root)
    {
        string lang = I18n.SystemLanguage;
        string T(string key, params object[] args) { return new Msg(key, args).In(lang); }
        string path = Path.Combine(dataDir, "diagnostico-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        var sb = new StringBuilder();
        sb.AppendLine("FanPilot " + AppVersion.Text + " --diagnose  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine(T("diag.howToReport", "url", ReportUrl(MachineData.Board)));
        sb.AppendLine(T("diag.redacted"));
        if (otherInstance) sb.AppendLine(T("diag.otherInstance"));
        sb.AppendLine("Linux: " + (HwmonFiles.ReadText(Path.Combine(root, "proc", "sys", "kernel", "osrelease")) ?? "?") + "; " + Distribution(root));
        sb.AppendLine(T("diag.board.linux", "board", MachineData.Board));
        sb.AppendLine(T("diag.backend", "backend", cfg.Backend));
        var modules = HwmonFiles.LoadedModules(root);
        var boardModules = HwmonFiles.BoardModules.Where(modules.Contains).ToList();
        sb.AppendLine(T("diag.modules", "modules", boardModules.Count > 0 ? string.Join(", ", boardModules) : T("diag.none")));
        sb.AppendLine();

        // Read only: the board is opened without giving back what another FanPilot holds, and closed without a curve set.
        var board = new HwmonBoard(root, false, false);
        try { DescribeBoard(sb, board, "diag.section.hwmonBoard", lang); }
        finally { board.Shutdown(); }
        sb.AppendLine();

        sb.AppendLine(T("diag.section.hwmon"));
        var chips = HwmonFiles.Chips(root);
        if (chips.Count == 0) sb.AppendLine("  " + T("diag.none"));
        foreach (var chip in chips)
        {
            sb.AppendLine(Path.GetFileName(chip.Dir) + ": " + chip.Name + (chip.DeviceId.Length > 0 ? " @ " + chip.DeviceId : ""));
            foreach (string file in Directory.GetFiles(chip.Dir).Select(Path.GetFileName).Where(Interesting).OrderBy(f => f, StringComparer.Ordinal))
                sb.AppendLine("  " + file + " = " + (HwmonFiles.ReadText(chip.File(file)) ?? T("diag.noReading")));
        }

        File.WriteAllText(path, Redact(sb.ToString(), lang), Encoding.UTF8);
        Console.WriteLine(T("diag.saved", "path", path));
        Console.WriteLine(T("diag.nothingChanged"));
        Console.WriteLine(ReportUrl(MachineData.Board));
        return 0;
    }

    // The sensors and the fan controls; not the alarms, beeps and the like.
    static bool Interesting(string file)
    {
        return System.Text.RegularExpressions.Regex.IsMatch(file, @"^(temp|fan|in|power|pwm|freq)\d+(_(input|label|average|enable|mode|cap))?$");
    }

    static string Distribution(string root)
    {
        string text = HwmonFiles.ReadText(Path.Combine(root, "etc", "os-release")) ?? "";
        var line = text.Split('\n').FirstOrDefault(l => l.StartsWith("PRETTY_NAME=", StringComparison.Ordinal));
        return line == null ? "?" : line.Substring("PRETTY_NAME=".Length).Trim('"');
    }
}
