// The motherboard on Linux: the kernel's hwmon drivers (/sys/class/hwmon, item 28b). Boards.Windows.cs has the same
// members.
static class Boards
{
    // "auto" and "hwmon" are the same here; Gigabyte Control Center and LibreHardwareMonitor only exist on Windows.
    public static IBoard Create(AppConfig cfg, bool interactive)
    {
        if (cfg.Backend == "gigabyte" || cfg.Backend == "lhm") throw new Unavailable("startup.backendOtherSystem", "backend", cfg.Backend);
        return new HwmonBoard();
    }

    public static string VersionNote(IBoard board)
    {
        var hwmon = board as HwmonBoard;
        return hwmon != null && hwmon.Chips.Length > 0 ? " (" + hwmon.Chips + ")" : "";
    }

    // FanPilot --restore-fans: what a FanPilot that was killed left taken goes back to the BIOS (the service's
    // ExecStopPost runs it, item 28c).
    public static int RestoreFans() { return HwmonBoard.RestoreFans(); }
}
