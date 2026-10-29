// The command line, read once. Options FanPilot does not know are left for whoever reads them (--fan N belongs to
// --hardware-test).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

sealed class AppOptions
{
    public bool Version, Diagnose, HardwareTest, Service, RestoreFans, Tui, Log;
    // A person is at the console: the browser opens, a missing PawnIO driver can be installed. Never for a service.
    public bool Interactive = true;
    // --data <folder>: where the state lives (the installed service uses %ProgramData%\FanPilot). Null: next to the exe.
    public string DataDir;
    // --import-data <folder>: copy a portable install's state into this one's data folder, then exit.
    public string ImportFrom;
    // Debug builds only (App.FakeBoardScenario): the fake board's scenario.
    public string FakeScenario;
    public string[] Raw;

    public bool Fake { get { return FakeScenario != null; } }

    public static AppOptions Parse(string[] args, bool fakeBoardAllowed)
    {
        var o = new AppOptions { Raw = args };
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            switch (a)
            {
                case "--version": o.Version = true; break;
                case "--diagnose": o.Diagnose = true; break;
                case "--hardware-test": o.HardwareTest = true; break;
                case "--service": o.Service = true; break;
                case "--restore-fans": o.RestoreFans = true; o.Interactive = false; break;
                case "--tui": o.Tui = true; break;
                case "--log": o.Log = true; break;
                case "--no-browser": o.Interactive = false; break;
                case "--data": o.DataDir = Value(args, ref i, a); break;
                case "--import-data": o.ImportFrom = Value(args, ref i, a); break;
                case "--fake-board": if (fakeBoardAllowed) o.FakeScenario = "demo"; break;
                default:
                    if (a.StartsWith("--data=")) o.DataDir = NonEmpty(a.Substring("--data=".Length), "--data");
                    else if (a.StartsWith("--import-data=")) o.ImportFrom = NonEmpty(a.Substring("--import-data=".Length), "--import-data");
                    else if (a.StartsWith("--fake-board=") && fakeBoardAllowed) o.FakeScenario = a.Substring("--fake-board=".Length);
                    break;
            }
        }
        if (o.Service) o.Interactive = false;
        return o;
    }

    static string Value(string[] args, ref int i, string name)
    {
        if (i + 1 >= args.Length || args[i + 1].StartsWith("--")) throw new UserError("console.option.folder", "option", name);
        return args[++i];
    }

    static string NonEmpty(string value, string name)
    {
        if (value.Trim().Length == 0) throw new UserError("console.option.empty", "option", name);
        return value;
    }
}

static class DataDirectory
{
    // --data, with environment variables expanded and made absolute; else data\ (data-fake\ for the fake board) next
    // to the exe, the portable layout.
    public static string Resolve(string requested, string baseDir, bool fake)
    {
        if (string.IsNullOrWhiteSpace(requested)) return Path.Combine(baseDir, fake ? "data-fake" : "data");
        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(requested.Trim()));
    }

    // As a service, the folder is the service's alone (Platform: SYSTEM and Administrators on Windows, root on Linux):
    // it trusts the pairings and the config.json in it.
    public static void Restrict(string dir) { Platform.RestrictDirectory(dir); }
}

// FanPilot.exe --import-data <portable data folder> [--data <folder>]: moving from the zip to the installed version.
static class DataImport
{
    // The state worth keeping; logs and the .bak/.corrompido copies stay behind.
    static readonly string[] Files = {
        "config.json", "machine.json", "sessions.json", "backup-curves.json", "desired-curves.json", "turbo.json", "global-mode.json", "fan-meta.json", "alerts.json"
    };

    // Returns the process exit code. running: a FanPilot is using the destination right now.
    public static int Run(string from, string to, bool running)
    {
        from = Path.GetFullPath(from);
        if (running)
        {
            Console.WriteLine(new Msg("console.import.busy").In(I18n.SystemLanguage));
            return 4;
        }
        if (!Directory.Exists(from))
        {
            Console.WriteLine(new Msg("console.import.notFound", "path", from).In(I18n.SystemLanguage));
            return 1;
        }
        if (string.Equals(from.TrimEnd('\\'), Path.GetFullPath(to).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(new Msg("console.import.same").In(I18n.SystemLanguage));
            return 1;
        }
        Directory.CreateDirectory(to);
        var copied = new List<string>();
        foreach (string name in Files)
        {
            string src = Path.Combine(from, name);
            if (!File.Exists(src)) continue;
            File.Copy(src, Path.Combine(to, name), true);
            copied.Add(name);
        }
        // Data from another PC or another board is moved aside here, exactly as on a normal start.
        MachineData.Check(to);
        Log.Info(copied.Count == 0 ? new Msg("console.import.nothing", "path", from)
                                   : new Msg("console.import.done", "path", from, "files", string.Join(", ", copied)));
        return 0;
    }

    // For the tests: what is copied.
    internal static IEnumerable<string> FileNames { get { return Files.AsEnumerable(); } }
}
