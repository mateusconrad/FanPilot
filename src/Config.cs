using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

sealed class AppConfig
{
    public int Port = 8787;
    // LAN address put in the QR code; null = auto-detect.
    public string PublicHost = null;
    // Floor applied to every motherboard fan curve point (percent of full PWM).
    public int MinDutyPercent = 20;
    // "Fixed %" mode still goes to 100% at this temperature (enforced by the Super I/O itself).
    public int FixedModeSafetyTemp = 85;
    // Upper bound for the full-speed point of a custom curve.
    public int MaxFullSpeedTemp = 90;
    // A manual GPU fan setting is reverted to automatic at or above this temperature.
    public int GpuSafetyTemp = 83;
    public int PairCodeTtlSeconds = 180;
    public int SessionDays = 30;
    // "auto" (Gigabyte Control Center if installed, else LibreHardwareMonitor), "gigabyte" or "lhm".
    public string Backend = "auto";

    public static AppConfig Load(string path)
    {
        var cfg = new AppConfig();
        if (File.Exists(path))
        {
            var d = Json.ParseObject(File.ReadAllText(path));
            cfg.Port = Json.GetInt(d, "port", cfg.Port);
            cfg.PublicHost = Json.GetString(d, "publicHost", cfg.PublicHost);
            cfg.MinDutyPercent = Json.GetInt(d, "minDutyPercent", cfg.MinDutyPercent);
            cfg.FixedModeSafetyTemp = Json.GetInt(d, "fixedModeSafetyTemp", cfg.FixedModeSafetyTemp);
            cfg.MaxFullSpeedTemp = Json.GetInt(d, "maxFullSpeedTemp", cfg.MaxFullSpeedTemp);
            cfg.GpuSafetyTemp = Json.GetInt(d, "gpuSafetyTemp", cfg.GpuSafetyTemp);
            cfg.PairCodeTtlSeconds = Json.GetInt(d, "pairCodeTtlSeconds", cfg.PairCodeTtlSeconds);
            cfg.SessionDays = Json.GetInt(d, "sessionDays", cfg.SessionDays);
            cfg.Backend = Json.GetString(d, "backend", cfg.Backend);
        }
        else
        {
            File.WriteAllText(path, Json.Serialize(cfg.ToDictionary()), Encoding.UTF8);
        }
        cfg.MinDutyPercent = Clamp(cfg.MinDutyPercent, 0, 100);
        // Must stay above the last flat point (70 C) of the fixed-mode curve.
        cfg.FixedModeSafetyTemp = Clamp(cfg.FixedModeSafetyTemp, 75, 95);
        cfg.MaxFullSpeedTemp = Clamp(cfg.MaxFullSpeedTemp, 50, 100);
        cfg.GpuSafetyTemp = Clamp(cfg.GpuSafetyTemp, 60, 90);
        cfg.Backend = (cfg.Backend ?? "auto").Trim().ToLowerInvariant();
        if (cfg.Backend != "gigabyte" && cfg.Backend != "lhm") cfg.Backend = "auto";
        return cfg;
    }

    public Dictionary<string, object> ToDictionary()
    {
        return new Dictionary<string, object> {
            { "port", Port }, { "publicHost", PublicHost }, { "minDutyPercent", MinDutyPercent },
            { "fixedModeSafetyTemp", FixedModeSafetyTemp }, { "maxFullSpeedTemp", MaxFullSpeedTemp },
            { "gpuSafetyTemp", GpuSafetyTemp }, { "pairCodeTtlSeconds", PairCodeTtlSeconds }, { "sessionDays", SessionDays },
            { "backend", Backend }
        };
    }

    public static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }
}

static class Json
{
    static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };

    public static string Serialize(object o) { return Serializer.Serialize(o); }

    public static Dictionary<string, object> ParseObject(string text)
    {
        var d = Serializer.DeserializeObject(text) as Dictionary<string, object>;
        if (d == null) throw new FormatException("JSON object expected");
        return d;
    }

    public static object Get(Dictionary<string, object> d, string key)
    {
        object v;
        return d != null && d.TryGetValue(key, out v) ? v : null;
    }

    public static int GetInt(Dictionary<string, object> d, string key, int fallback)
    {
        object v = Get(d, key);
        return v == null ? fallback : (int)Math.Round(ToDouble(v));
    }

    public static string GetString(Dictionary<string, object> d, string key, string fallback)
    {
        object v = Get(d, key);
        return v == null ? fallback : Convert.ToString(v, CultureInfo.InvariantCulture);
    }

    public static double ToDouble(object v)
    {
        if (v == null) throw new FormatException("number expected");
        return Convert.ToDouble(v, CultureInfo.InvariantCulture);
    }

    public static List<object> ToList(object v)
    {
        var e = v as IEnumerable;
        if (e == null || v is string) throw new FormatException("array expected");
        var list = new List<object>();
        foreach (object o in e) list.Add(o);
        return list;
    }
}
