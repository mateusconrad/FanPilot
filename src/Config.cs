using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

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
    // Days of history kept on disk, 10 s per row (item 24); 0 turns it off (the panel keeps its 10 minutes).
    public int HistoryDays = 7;

    public static AppConfig Load(string path)
    {
        var cfg = new AppConfig();
        if (File.Exists(path))
        {
            try
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
                cfg.HistoryDays = Json.GetInt(d, "historyDays", cfg.HistoryDays);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is InvalidOperationException || ex is OverflowException)
            {
                // Edited by hand: the user's file stays exactly as it is, and FanPilot starts with the defaults.
                cfg = new AppConfig();
                AppNotices.Add(new Msg("notice.configInvalid", "detail", ex.Message));
            }
        }
        else
        {
            DataFile.WriteAtomic(path, cfg.ToDictionary());
        }
        if (cfg.Port < 1 || cfg.Port > 65535)
        {
            AppNotices.Add(new Msg("notice.configPort", "port", cfg.Port));
            cfg.Port = 8787;
        }
        cfg.MinDutyPercent = Clamp(cfg.MinDutyPercent, 0, 100);
        // Must stay above the last flat point (70 C) of the fixed-mode curve.
        cfg.FixedModeSafetyTemp = Clamp(cfg.FixedModeSafetyTemp, 75, 95);
        cfg.MaxFullSpeedTemp = Clamp(cfg.MaxFullSpeedTemp, 50, 100);
        cfg.GpuSafetyTemp = Clamp(cfg.GpuSafetyTemp, 60, 90);
        cfg.HistoryDays = Clamp(cfg.HistoryDays, 0, 90);
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
            { "backend", Backend }, { "historyDays", HistoryDays }
        };
    }

    public static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }
}
