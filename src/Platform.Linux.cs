// What FanPilot asks of the operating system, on Linux (item 28). Platform.Windows.cs has the same members.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

// The .NET 10 build is the Linux build: Unix file modes, POSIX signals and libc are fine everywhere in it.
[assembly: SupportedOSPlatform("linux")]

static class Platform
{
    public static void Init() { }

    [DllImport("libc", SetLastError = true)]
    static extern uint geteuid();

    // Reaching the motherboard's sensors and fans (/sys/class/hwmon, item 28b) needs root.
    public static bool IsElevated { get { return geteuid() == 0; } }

    public static Msg ElevationNeeded { get { return new Msg("console.root"); } }

    // What machine.json compares: this Linux install (systemd's machine id) and the board in it. A dual boot has a
    // different id on each system, and each keeps its own data folder.
    public static string MachineId { get { return ReadLine("/etc/machine-id"); } }

    public static string BoardName
    {
        get { return (ReadLine("/sys/class/dmi/id/board_vendor") + " " + ReadLine("/sys/class/dmi/id/board_name")).Trim(); }
    }

    // The service runs as root: its pairings, curves and config must not be writable (nor its secrets readable) by
    // other users. Owner only, which is root for the service.
    public static void RestrictDirectory(string dir)
    {
        Directory.CreateDirectory(dir);
        File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    // WebServer's "this PC only". .NET's own HttpListener (Linux) listens on the first address "localhost" resolves to,
    // often ::1 alone, and then 127.0.0.1 is refused; so it takes every address ("+": every IPv4 one), which needs no
    // root here, and WebServer turns away whatever doesn't come from this PC.
    public const string LocalhostPrefixHost = "+";

    // FanPilot runs as root here, and a browser opened as root is what nobody wants: the console already shows the
    // address.
    public static void OpenInBrowser(string url) { }

    static readonly List<PosixSignalRegistration> Signals = new List<PosixSignalRegistration>();

    // Ctrl+C (SIGINT), Ctrl+\ (SIGQUIT), systemd stopping the service or a plain kill (SIGTERM) and the terminal
    // closing (SIGHUP) all let Main clean up and return, as Ctrl+C does on Windows. kill -9 can't be caught; the
    // service's ExecStopPost covers it (item 28b).
    public static void HandleConsoleSignals(Action stop, Action cleanup)
    {
        foreach (var signal in new[] { PosixSignal.SIGINT, PosixSignal.SIGQUIT, PosixSignal.SIGTERM, PosixSignal.SIGHUP })
            Signals.Add(PosixSignalRegistration.Create(signal, context =>
            {
                context.Cancel = true; // Main returns on its own, after the cleanup
                stop();
            }));
    }

    static string ReadLine(string path)
    {
        try { return File.ReadAllText(path).Trim(); }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return ""; }
    }
}
