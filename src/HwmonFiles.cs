// The kernel's hwmon drivers on Linux (item 28b): one folder per chip in /sys/class/hwmon, files of one number each
// (millidegrees, RPM, millivolts, microwatts, 0..255 duty). Everything here is relative to a root folder, "/" for
// real, a scratch folder in the tests (HwmonBoardTests builds a fake /sys there).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

static class HwmonFiles
{
    public static string HwmonDir(string root) { return Path.Combine(root, "sys", "class", "hwmon"); }

    public static long? ReadLong(string path)
    {
        long v;
        string text = ReadText(path);
        return text != null && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : (long?)null;
    }

    // null when missing or unreadable: a driver can refuse a read (EIO, ENODATA) for a sensor that isn't wired.
    public static string ReadText(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path).Trim() : null; }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return null; }
    }

    public static void Write(string path, long value)
    {
        File.WriteAllText(path, value.ToString(CultureInfo.InvariantCulture));
    }

    // The chips, in hwmonN order. Name and device say which chip it is; N changes between boots.
    public static List<Chip> Chips(string root)
    {
        var list = new List<Chip>();
        string dir = HwmonDir(root);
        if (!Directory.Exists(dir)) return list;
        foreach (string path in Directory.GetDirectories(dir).Concat(Directory.GetFiles(dir)).Distinct().OrderBy(Number))
        {
            string name = ReadText(Path.Combine(path, "name"));
            if (name == null) continue;
            list.Add(new Chip(path, name, DeviceId(path)));
        }
        return list;
    }

    static int Number(string path)
    {
        var m = Regex.Match(Path.GetFileName(path), @"\d+$");
        return m.Success ? int.Parse(m.Value, CultureInfo.InvariantCulture) : int.MaxValue;
    }

    // Where the chip sits ("nct6775.656", "0000:00:18.3", "coretemp.0"), which stays put across boots, unlike hwmonN.
    static string DeviceId(string chipDir)
    {
        string device = Path.Combine(chipDir, "device");
        try
        {
            var target = Directory.ResolveLinkTarget(device, true);
            if (target != null) return target.Name;
        }
        catch (IOException) { }
        return Directory.Exists(device) ? new DirectoryInfo(device).Name : "";
    }

    public sealed class Chip
    {
        public readonly string Dir, Name, DeviceId;

        public Chip(string dir, string name, string deviceId) { Dir = dir; Name = name; DeviceId = deviceId; }

        // Stable across boots and the same for every reading of the chip: "nct6798@nct6775.656".
        public string Key { get { return DeviceId.Length > 0 ? Name + "@" + DeviceId : Name; } }

        public string File(string name) { return Path.Combine(Dir, name); }

        // The indexes N of the files "<kind>N_input" (temp, fan, in, power) or "pwmN", in order.
        public List<int> Indexes(string kind)
        {
            var found = new List<int>();
            if (!Directory.Exists(Dir)) return found;
            var pattern = kind == "pwm" ? new Regex("^pwm(\\d+)$") : new Regex("^" + kind + "(\\d+)_(input|average)$");
            foreach (string f in Directory.GetFiles(Dir))
            {
                var m = pattern.Match(Path.GetFileName(f));
                if (m.Success) found.Add(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
            }
            return found.Distinct().OrderBy(n => n).ToList();
        }

        public string Label(string kind, int n) { return ReadText(File(kind + n + "_label")); }
    }

    // What the chips are, by the name their driver gives them.
    public static bool IsCpu(string name) { return name == "k10temp" || name == "coretemp" || name == "zenpower" || name == "k8temp"; }

    public static bool IsGpu(string name) { return name == "amdgpu" || name == "radeon" || name == "nouveau" || name == "i915" || name == "xe"; }

    // The motherboard's sensor and fan chips: Super I/O (nct67xx, it87xx, Fintek, Winbond), the embedded controllers
    // and WMI interfaces of ASUS, Gigabyte and Dell, and any other chip that has a fan it can control.
    static readonly string[] BoardPrefixes = { "nct", "it8", "f71", "f81", "w83", "sch5", "asus", "gigabyte", "dell_smm", "thinkpad", "applesmc" };

    public static bool IsBoard(Chip chip)
    {
        if (IsCpu(chip.Name) || IsGpu(chip.Name)) return false;
        return BoardPrefixes.Any(p => chip.Name.StartsWith(p, StringComparison.Ordinal)) ||
               chip.Indexes("pwm").Any(n => System.IO.File.Exists(chip.File("pwm" + n + "_enable")));
    }

    // The drivers of motherboard chips, as /proc/modules lists them: one loaded without a chip of its own in hwmon is
    // usually a chip the BIOS keeps for itself (an ACPI conflict) or one it needs to be told about.
    public static readonly string[] BoardModules = { "nct6775", "nct6683", "nct6687", "it87", "asus_ec_sensors", "asus_wmi_sensors", "gigabyte_wmi", "dell_smm_hwmon" };

    public static List<string> LoadedModules(string root)
    {
        string text = ReadText(Path.Combine(root, "proc", "modules"));
        if (text == null) return new List<string>();
        return text.Split('\n').Select(l => l.Split(' ')[0].Trim()).Where(m => m.Length > 0).ToList();
    }
}
