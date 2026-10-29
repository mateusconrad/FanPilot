// What the tray icon says (item 14b), from the service's /api/state: the tooltip and the mark on the icon. Apart from
// the WinForms code, so the tests read it (FanPilot.Tests compiles this file too).
using System;
using System.Collections.Generic;
using System.Linq;

enum TrayLevel { Ok, Alert, Down }

// An alert for a Windows notification (item 16): its title and detail in Windows' language.
sealed class TrayNotice
{
    public string Id, Title, Detail;
    public bool Critical;
}

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

    // The active alerts that appeared since the last reading (seen: the ids active then, updated here), for a
    // notification each. One already acknowledged in the panel stays quiet; one that resolved and came back is shown again.
    public static List<TrayNotice> NewAlerts(Dictionary<string, object> state, HashSet<string> seen, string lang)
    {
        var alerts = Json.Get(state, "alerts");
        var active = alerts == null ? new List<Dictionary<string, object>>()
            : Json.ToList(alerts).OfType<Dictionary<string, object>>().Where(a => Json.Get(a, "resolved") == null && Json.GetString(a, "id", null) != null).ToList();
        var fresh = active
            .Where(a => !seen.Contains(Json.GetString(a, "id", null)) && !(Json.Get(a, "acknowledged") is bool && (bool)Json.Get(a, "acknowledged")))
            .Select(a => new TrayNotice {
                Id = Json.GetString(a, "id", null),
                Title = Text(Json.Get(a, "title") as Dictionary<string, object>, lang) ?? "FanPilot",
                Detail = Text(Json.Get(a, "detail") as Dictionary<string, object>, lang) ?? "",
                Critical = Json.GetString(a, "severity", null) == "critical" })
            .ToList();
        seen.Clear();
        foreach (var a in active) seen.Add(Json.GetString(a, "id", null));
        return fresh;
    }

    // The global modes the tray offers (item 18), in the panel's order; "Máximo" is the Turbo's job.
    public static readonly string[] Modes = { "custom", "profile:quiet", "profile:standard", "profile:performance" };

    public static string ModeLabel(string mode, string lang)
    {
        return I18n.T(lang, mode == "custom" ? "profiles.custom" : "preset." + mode.Substring("profile:".Length));
    }

    // What the server said went wrong (its { "code", "error" }), in Windows' language.
    public static string ErrorText(string body, string lang)
    {
        try
        {
            var d = Json.ParseObject(body);
            string code = Json.GetString(d, "code", null);
            if (code != null && I18n.Has(code)) return I18n.T(lang, code, Json.Get(d, "params") as Dictionary<string, object>);
            return Json.GetString(d, "error", null);
        }
        catch (FormatException) { return null; }
    }

    // A message as the server sends it: { "code", "params", "text" }.
    static string Text(Dictionary<string, object> msg, string lang)
    {
        if (msg == null) return null;
        string code = Json.GetString(msg, "code", null);
        return code != null && I18n.Has(code) ? I18n.T(lang, code, Json.Get(msg, "params") as Dictionary<string, object>) : Json.GetString(msg, "text", null);
    }

    static string Fit(string text)
    {
        return text.Length <= MaxTooltip ? text : text.Substring(0, MaxTooltip - 1) + "…";
    }
}
