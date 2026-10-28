// The motherboard on Windows: Gigabyte Control Center (curves in the chip) or LibreHardwareMonitor. Boards.Linux.cs
// has the same members (item 28).
using System.Diagnostics;
using System.IO;

static class Boards
{
    // "auto": Gigabyte Control Center when installed (curves in hardware), else LibreHardwareMonitor.
    // interactive: a person is at the console, so a missing PawnIO driver can be installed right away.
    public static IBoard Create(AppConfig cfg, bool interactive)
    {
        bool gcc = File.Exists(Path.Combine(GigabyteInstall.LibDir, GigabyteApi.EngineDll));
        var api = gcc && cfg.Backend != "lhm" ? GigabyteApi.Load(GigabyteInstall.LibDir) : null;
        if (Choose(cfg.Backend, gcc, api) == "gigabyte")
        {
            Log.Info("Gigabyte Control Center " + api.Version + " at " + GigabyteInstall.LibDir);
            return new GigabyteBridge(api);
        }
        PawnIoSetup.OfferInstall(interactive);
        return new LhmBoard();
    }

    // "gigabyte" or "lhm". An installed but incompatible GCC (an update changed what FanPilot uses) falls back to
    // LibreHardwareMonitor with a notice in "auto", and stops with the reason when config.json insists on "gigabyte".
    internal static string Choose(string backend, bool gccInstalled, GigabyteApi api)
    {
        if (backend == "lhm") return "lhm";
        if (!gccInstalled)
        {
            if (backend == "gigabyte")
                throw new Unavailable("startup.gccMissing", "dir", GigabyteInstall.LibDir);
            return "lhm";
        }
        if (api.Compatible) return "gigabyte";
        if (backend == "gigabyte") throw new Unavailable("gcc.incompatible", "version", api.Version, "problem", api.Problem);
        AppNotices.Add(new Msg("notice.gccFallback", "version", api.Version, "problem", api.Problem));
        return "lhm";
    }

    // For the hardware test's report: the GCC's version when the board is reached through it, since an update of it is
    // what most often changes this path.
    public static string VersionNote(IBoard board)
    {
        string dll = Path.Combine(GigabyteInstall.LibDir, GigabyteApi.EngineDll);
        return board.BackendName.Contains("Gigabyte") && File.Exists(dll) ? " (GCC " + FileVersionInfo.GetVersionInfo(dll).FileVersion + ")" : "";
    }
}
