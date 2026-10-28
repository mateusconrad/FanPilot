// Where Gigabyte Control Center keeps the client DLLs FanPilot loads at run time (GigabyteApi), and the resolver for
// the Gigabyte assemblies those depend on. Windows only.
using System;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

static class GigabyteInstall
{
    public const string DefaultLibDir = @"C:\Program Files\GIGABYTE\Control Center\Lib\MBEasyTune";
    // Resolved once from the installer's registry entries, so a GCC installed on another drive/folder still works.
    public static readonly string LibDir = FindLibDir();

    public static Assembly ResolveAssembly(object sender, ResolveEventArgs e)
    {
        // Only Gigabyte's own assemblies: GCC's folder may hold other copies of libraries FanPilot ships itself
        // (System.Memory etc. for LibreHardwareMonitor), and those must come from FanPilot's folder.
        string name = new AssemblyName(e.Name).Name;
        if (!name.StartsWith("Gigabyte.", StringComparison.OrdinalIgnoreCase)) return null;
        string path = Path.Combine(LibDir, name + ".dll");
        return File.Exists(path) ? Assembly.LoadFrom(path) : null;
    }

    // "GIGABYTE Performance Library" (MBEasyTune) and "GIGABYTE Control Center" register uninstallers whose folders
    // locate the client DLLs. InstallLocation is empty in these entries, so the uninstaller path is used instead.
    static string FindLibDir()
    {
        string[][] candidates = {
            new[] { "MBEasyTune", "" },
            new[] { "GIGABYTE Control Center", @"Lib\MBEasyTune" }
        };
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            foreach (var c in candidates)
            {
                try
                {
                    using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + c[0]))
                    {
                        if (key == null) continue;
                        string uninstall = (key.GetValue("UninstallString") as string ?? "").Trim().Trim('"');
                        if (uninstall.Length == 0) continue;
                        string dir = Path.Combine(Path.GetDirectoryName(uninstall), c[1]);
                        if (File.Exists(Path.Combine(dir, "Gigabyte.Engine.EnvironmentControl.dll"))) return dir;
                    }
                }
                catch { }
            }
        }
        return DefaultLibDir;
    }
}
