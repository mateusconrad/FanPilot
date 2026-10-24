// The build itself: FanPilot.exe, the libraries next to it and the binding redirects in FanPilot.exe.config.
// Item 08 adds the tests for FanPilot's rules; these only guard what the SDK-style build (item 09) produces.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Xunit;

public class SmokeTests
{
    static readonly string OutputDir = Path.GetDirectoryName(typeof(FanCurve).Assembly.Location);

    [Fact]
    public void FanPilotAssemblyLoads()
    {
        Assert.Equal("FanPilot", typeof(FanCurve).Assembly.GetName().Name);
    }

    [Fact]
    public void LibreHardwareMonitorAndItsDependenciesLoad()
    {
        // Creating (not opening) a Computer needs no driver or Administrator, but loads the library and what it uses.
        var pc = new LibreHardwareMonitor.Hardware.Computer { IsMotherboardEnabled = true };
        Assert.False(pc.IsGpuEnabled);
    }

    // Each strong-named library shipped next to the exe must be redirected to exactly that copy: an older reference
    // (from another library, or from Gigabyte's assemblies loaded at runtime) would otherwise fail to load.
    [Fact]
    public void ExeConfigRedirectsEveryShippedStrongNamedLibrary()
    {
        string config = Path.Combine(OutputDir, "FanPilot.exe.config");
        Assert.True(File.Exists(config), "FanPilot.exe.config não está ao lado do FanPilot.exe");

        XNamespace asm = "urn:schemas-microsoft-com:asm.v1";
        var redirects = XDocument.Load(config).Descendants(asm + "dependentAssembly").ToDictionary(
            d => (string)d.Element(asm + "assemblyIdentity").Attribute("name"),
            d => (string)d.Element(asm + "bindingRedirect").Attribute("newVersion"));

        var missing = new List<string>();
        foreach (var name in ShippedClosure().Where(n => n.GetPublicKeyToken().Length > 0))
        {
            string target;
            if (!redirects.TryGetValue(name.Name, out target) || target != name.Version.ToString())
                missing.Add(name.Name + " " + name.Version + (target != null ? " (redirecionado para " + target + ")" : " (sem redirecionamento)"));
        }
        Assert.True(missing.Count == 0, "Redirecionamentos faltando ou errados: " + string.Join("; ", missing));
    }

    // FanPilot.exe and every library it reaches that is shipped in the same folder (the framework is in the GAC).
    static IEnumerable<AssemblyName> ShippedClosure()
    {
        var seen = new Dictionary<string, AssemblyName>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        queue.Enqueue(Path.Combine(OutputDir, "FanPilot.exe"));
        while (queue.Count > 0)
        {
            var name = AssemblyName.GetAssemblyName(queue.Dequeue());
            if (seen.ContainsKey(name.Name)) continue;
            seen[name.Name] = name;
            foreach (var reference in ReferencesOf(name))
            {
                string path = Path.Combine(OutputDir, reference.Name + ".dll");
                if (File.Exists(path) && !seen.ContainsKey(reference.Name)) queue.Enqueue(path);
            }
        }
        seen.Remove("FanPilot");
        return seen.Values;
    }

    static AssemblyName[] ReferencesOf(AssemblyName name)
    {
        string file = Path.Combine(OutputDir, name.Name + (name.Name == "FanPilot" ? ".exe" : ".dll"));
        return Assembly.ReflectionOnlyLoadFrom(file).GetReferencedAssemblies();
    }
}
