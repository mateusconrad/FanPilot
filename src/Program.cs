// GccWeb - web monitor/fan controller built on top of Gigabyte Control Center's EasyTuneEngineService.
// Written in C# 5 so it builds with the csc.exe that ships with Windows (.NET Framework 4.x);
// .NET Remoting (the service's IPC protocol) only exists on .NET Framework.
using System;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

static class Program
{
    const string DefaultGccLibDir = @"C:\Program Files\GIGABYTE\Control Center\Lib\MBEasyTune";
    // Resolved once from the installer's registry entries, so a GCC installed on another drive/folder still works.
    public static readonly string GccLibDir = FindGccLibDir();

    static int Main(string[] args)
    {
        // Gigabyte's client assemblies are loaded from the GCC install instead of being redistributed.
        // This must be registered before any method touching Gigabyte types is JIT-compiled.
        AppDomain.CurrentDomain.AssemblyResolve += ResolveGigabyteAssembly;
        return App.Run(args);
    }

    static Assembly ResolveGigabyteAssembly(object sender, ResolveEventArgs e)
    {
        string path = Path.Combine(GccLibDir, new AssemblyName(e.Name).Name + ".dll");
        return File.Exists(path) ? Assembly.LoadFrom(path) : null;
    }

    // "GIGABYTE Performance Library" (MBEasyTune) and "GIGABYTE Control Center" register uninstallers whose folders
    // locate the client DLLs. InstallLocation is empty in these entries, so the uninstaller path is used instead.
    static string FindGccLibDir()
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
        return DefaultGccLibDir;
    }
}
