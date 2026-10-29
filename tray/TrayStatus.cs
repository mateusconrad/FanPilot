// What the tray icon says (item 14b), from the service's /api/state: the tooltip and the mark on the icon. Apart from
// the WinForms code, so the tests read it (FanPilot.Tests compiles this file too).
using System;
using System.Collections.Generic;
using System.Linq;

enum TrayLevel { Ok, Alert, Down }

sealed class TrayStatus
{
    // The .NET Framework's NotifyIcon refuses a longer tooltip.
    public const int MaxTooltip = 63;

    public TrayLevel Level { get; private set; }
    public string Tooltip { get; private set; }

    // FanPilot doesn't answer: the service is stopped, or still starting.
    public static TrayStatus Down(string lang)
    {
        return new TrayStatus { Level = TrayLevel.Down, Tooltip = Fit("FanPilot · " + I18n.T(lang, "tray.down")) };
    }

    // "FanPilot · 1 alerta · CPU 36 °C · GPU 41 °C": the alerts first, as they are what the mark is for.
    public static TrayStatus Of(Dictionary<string, object> state, string lang)
    {
        var parts = new List<string> { "FanPilot" };
        var level = TrayLevel.Ok;

        var service = Json.Get(state, "service") as Dictionary<string, object>;
        if (service != null && Json.Get(service, "ok") is bool && !(bool)Json.Get(service, "ok"))
        {
            level = TrayLevel.Alert;
            parts.Add(I18n.T(lang, "tray.boardDown"));
        }

        var alerts = Json.Get(state, "alerts");
        int active = alerts == null ? 0 : Json.ToList(alerts).OfType<Dictionary<string, object>>().Count(a => Json.Get(a, "resolved") == null);
        if (active > 0)
        {
            level = TrayLevel.Alert;
            parts.Add(I18n.T(lang, "tray.alerts", new Dictionary<string, object> { { "count", active } }));
        }

        var cpu = CpuTemperature(state);
        if (cpu.HasValue) parts.Add("CPU " + Math.Round(cpu.Value) + " °C");
        var gpu = Json.Get(state, "gpu") as Dictionary<string, object>;
        if (gpu != null && Json.Get(gpu, "temp") != null) parts.Add("GPU " + Math.Round(Json.ToDouble(Json.Get(gpu, "temp"))) + " °C");

        return new TrayStatus { Level = level, Tooltip = Fit(string.Join(" · ", parts)) };
    }

    // The panel's and the terminal's CPU reading: the one marked "cpu-main", or the one titled "CPU" (CpuRoles.MainOf).
    static double? CpuTemperature(Dictionary<string, object> state)
    {
        var sensors = Json.Get(state, "sensors");
        if (sensors == null) return null;
        var list = Json.ToList(sensors).OfType<Dictionary<string, object>>().ToList();
        var main = list.FirstOrDefault(s => Json.GetString(s, "role", null) == "cpu-main")
            ?? list.FirstOrDefault(s => Json.GetString(s, "type", null) == "Temperature" && Json.GetString(s, "title", null) == "CPU");
        return main == null || Json.Get(main, "value") == null ? (double?)null : Json.ToDouble(Json.Get(main, "value"));
    }

    static string Fit(string text)
    {
        return text.Length <= MaxTooltip ? text : text.Substring(0, MaxTooltip - 1) + "…";
    }
}
