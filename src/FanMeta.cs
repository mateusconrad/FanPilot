// What the user calls each header and what it drives (item 17), kept by the header's stable key in fan-meta.json.
// A pump is protected in every write FanManager makes (the editor, Turbo, profiles, re-applying saved curves):
// "full" turns any request into 100%, "min" raises the floor to PumpMinPercent. Restoring the original curve (the
// BIOS's or GCC's) stays allowed.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

sealed class FanMeta
{
    public const int DefaultPumpMinPercent = 70, LowestPumpMinPercent = 50, MaxLabelLength = 32;
    // What chips and LibreHardwareMonitor call pump headers: "SYS_FAN5_PUMP", "W_PUMP+", "AIO Pump", "AIO_PUMP".
    static readonly Regex PumpTitle = new Regex(@"pump|bomba|(^|[^a-z])aio([^a-z]|$)", RegexOptions.IgnoreCase);

    public string Label;                // null: the chip's name
    public string Role = "fan";         // "fan" or "pump"
    public string PumpPolicy = "full";  // "full" or "min"
    public int PumpMinPercent = DefaultPumpMinPercent;

    public bool IsPump { get { return Role == "pump"; } }

    public static bool LooksLikePump(string title) { return title != null && PumpTitle.IsMatch(title); }

    // A header nobody configured: a pump when its name says so, protected at 100%.
    public static FanMeta Default(string title) { return new FanMeta { Role = LooksLikePump(title) ? "pump" : "fan" }; }

    // From the panel (POST /api/fans/{i}/meta) or fan-meta.json. ArgumentException says what is wrong.
    public static FanMeta Parse(Dictionary<string, object> d, string title)
    {
        string label = (Json.GetString(d, "label", null) ?? "").Trim();
        if (label.Length > MaxLabelLength) throw new ArgumentException("O nome pode ter até " + MaxLabelLength + " caracteres.");
        if (label.Any(char.IsControl)) throw new ArgumentException("O nome não pode ter quebras de linha nem tabulações.");
        var m = new FanMeta {
            Label = label.Length == 0 ? null : label,
            Role = Json.GetString(d, "role", Default(title).Role),
            PumpPolicy = Json.GetString(d, "pumpPolicy", "full"),
            PumpMinPercent = Json.GetInt(d, "pumpMinPercent", DefaultPumpMinPercent)
        };
        if (m.Role != "fan" && m.Role != "pump") throw new ArgumentException("Função inválida: use fan ou pump.");
        if (m.PumpPolicy != "full" && m.PumpPolicy != "min") throw new ArgumentException("Proteção da bomba inválida: use full ou min.");
        if (m.PumpMinPercent < LowestPumpMinPercent || m.PumpMinPercent > 100)
            throw new ArgumentException("O mínimo da bomba vai de " + LowestPumpMinPercent + "% a 100%.");
        return m;
    }

    public Dictionary<string, object> ToDictionary()
    {
        return new Dictionary<string, object> {
            { "label", Label }, { "role", Role }, { "pumpPolicy", PumpPolicy }, { "pumpMinPercent", PumpMinPercent }
        };
    }

    // Whether going from other to this changes what may be written to the header (a new name alone does not).
    public bool SameProtection(FanMeta other)
    {
        if (Role != other.Role) return false;
        if (!IsPump) return true;
        return PumpPolicy == other.PumpPolicy && (PumpPolicy != "min" || PumpMinPercent == other.PumpMinPercent);
    }
}
