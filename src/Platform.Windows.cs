// What FanPilot asks of the operating system, on Windows. Platform.Linux.cs has the same members (item 28).
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;

static class Platform
{
    // Before anything else runs: Gigabyte's client assemblies come from the GCC install instead of being redistributed.
    public static void Init()
    {
        AppDomain.CurrentDomain.AssemblyResolve += GigabyteInstall.ResolveAssembly;
    }

    // Reaching the motherboard's sensors and fans needs Administrator.
    public static bool IsElevated
    {
        get { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
    }

    public static Msg ElevationNeeded { get { return new Msg("console.admin"); } }

    // What machine.json compares: this Windows install and the board in it.
    public static string MachineId { get { return ReadHklm(@"SOFTWARE\Microsoft\Cryptography", "MachineGuid"); } }

    public static string BoardName
    {
        get
        {
            return (ReadHklm(@"HARDWARE\DESCRIPTION\System\BIOS", "BaseBoardManufacturer") + " " +
                    ReadHklm(@"HARDWARE\DESCRIPTION\System\BIOS", "BaseBoardProduct")).Trim();
        }
    }

    // The service runs as LocalSystem: its pairings, curves and config must not be writable by other users, or a
    // standard user could change what a SYSTEM process trusts. Only SYSTEM and Administrators, inherited by every file.
    public static void RestrictDirectory(string dir)
    {
        Directory.CreateDirectory(dir);
        var acl = new DirectorySecurity();
        acl.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        Directory.SetAccessControl(dir, acl);
    }

    // WebServer's "this PC only": http.sys listens on both loopback addresses for "localhost".
    public const string LocalhostPrefixHost = "localhost";

    // The person at the console: the panel opens in their browser.
    public static void OpenInBrowser(string url)
    {
        try { Process.Start(url); } catch { }
    }

    delegate bool ConsoleCtrlHandler(int ctrlType);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetConsoleCtrlHandler(ConsoleCtrlHandler handler, bool add);

    const int CtrlC = 0, CtrlBreak = 1, CtrlClose = 2, CtrlLogoff = 5, CtrlShutdown = 6;
    static ConsoleCtrlHandler _handler; // kept referenced so the GC doesn't collect the callback

    // stop: Ctrl+C or Ctrl+Break, after which Main cleans up and returns. cleanup: closing the console window, logoff
    // or shutdown, when Windows ends the process right after the handler returns.
    public static void HandleConsoleSignals(Action stop, Action cleanup)
    {
        _handler = type =>
        {
            if (type == CtrlC || type == CtrlBreak) { stop(); return true; }
            cleanup();
            return true;
        };
        SetConsoleCtrlHandler(_handler, true);
    }

    static string ReadHklm(string subKey, string name)
    {
        try
        {
            using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var key = hklm.OpenSubKey(subKey))
                return key == null ? "" : Convert.ToString(key.GetValue(name) ?? "");
        }
        catch { return ""; }
    }
}
