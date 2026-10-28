using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

// The words FanPilot writes for people, in English or Portuguese (item 21). The dictionaries are the panel's
// (wwwroot/i18n/en.json and pt-BR.json), embedded in the exe: flat keys with {name} parameters. The log, and the text
// that goes with every message in the API, are always in English; the panel, the console, the reports and the alerts
// sent out pick a language.
static class I18n
{
    public const string English = "en", Portuguese = "pt-BR";
    public static readonly string[] Languages = { English, Portuguese };
    static readonly Dictionary<string, Dictionary<string, string>> Dicts = Languages.ToDictionary(l => l, Load);

    static Dictionary<string, string> Load(string lang)
    {
        using (var stream = typeof(I18n).Assembly.GetManifestResourceStream("i18n." + lang + ".json"))
        using (var reader = new StreamReader(stream, Encoding.UTF8))
            return Json.ParseObject(reader.ReadToEnd()).ToDictionary(kv => kv.Key, kv => Convert.ToString(kv.Value, CultureInfo.InvariantCulture));
    }

    // Portuguese for "pt", "pt-BR", "pt-PT"…; English for anything else.
    public static string Of(string tag)
    {
        if (tag == null) return English;
        tag = tag.Trim().ToLowerInvariant();
        return tag == "pt" || tag.StartsWith("pt-") ? Portuguese : English;
    }

    // The system's display language (Windows' setting; LANG and LC_* on Linux): the console and the reports.
    public static string SystemLanguage { get { return Of(CultureInfo.CurrentUICulture.Name); } }

    // The browser's first choice between the two ("pt-BR,pt;q=0.9,en;q=0.8"), English when it names neither.
    public static string FromAcceptLanguage(string header)
    {
        if (string.IsNullOrEmpty(header)) return English;
        var ranked = header.Split(',').Select(part =>
        {
            var pieces = part.Split(';');
            double q = 1;
            foreach (var p in pieces.Skip(1))
                if (p.Trim().StartsWith("q=")) double.TryParse(p.Trim().Substring(2), NumberStyles.Float, CultureInfo.InvariantCulture, out q);
            return new { Tag = pieces[0].Trim().ToLowerInvariant(), Q = q };
        }).Where(x => x.Q > 0).OrderByDescending(x => x.Q); // stable: equal weights keep the header's order
        foreach (var x in ranked)
        {
            if (x.Tag == "pt" || x.Tag.StartsWith("pt-")) return Portuguese;
            if (x.Tag == "en" || x.Tag.StartsWith("en-")) return English;
        }
        return English;
    }

    public static bool Has(string key) { return Dicts[English].ContainsKey(key) || Dicts[English].ContainsKey(key + ".other"); }

    // The text of key in lang with {name} filled from args: numbers in the language's format (one decimal at most, as
    // the panel does), a Msg in the same language. With a numeric "count", key.one or key.other when the dictionary has
    // them. A missing key gives the key itself, so the gap shows instead of a blank.
    public static string T(string lang, string key, IDictionary<string, object> args = null)
    {
        Dictionary<string, string> dict;
        if (lang == null || !Dicts.TryGetValue(lang, out dict)) { lang = English; dict = Dicts[English]; }
        string template = null;
        object count;
        if (args != null && args.TryGetValue("count", out count) && IsNumber(count))
            dict.TryGetValue(key + "." + Plural(lang, Convert.ToDouble(count, CultureInfo.InvariantCulture)), out template);
        if (template == null && !dict.TryGetValue(key, out template)) return key;
        var culture = CultureInfo.GetCultureInfo(lang == Portuguese ? "pt-BR" : "en-US");
        return Regex.Replace(template, @"\{(\w+)\}", m =>
        {
            object v;
            return args != null && args.TryGetValue(m.Groups[1].Value, out v) ? Format(v, lang, culture) : m.Value;
        });
    }

    // CLDR's rules for the two: Portuguese "one" is 0 and 1, English only 1.
    static string Plural(string lang, double n)
    {
        bool one = lang == Portuguese ? n >= 0 && n < 2 : n == 1;
        return one ? "one" : "other";
    }

    static bool IsNumber(object v) { return v is int || v is long || v is double || v is float || v is decimal; }

    static string Format(object v, string lang, CultureInfo culture)
    {
        var msg = v as Msg;
        if (msg != null) return msg.In(lang);
        if (v is double || v is float || v is decimal)
        {
            double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
            return d.ToString(d == Math.Floor(d) ? "0" : "0.0", culture);
        }
        return Convert.ToString(v, culture);
    }
}

// Something to tell people, in any language: a dictionary key and its parameters. Text is the English version, for
// the log and for anything that reads the API without a dictionary.
sealed class Msg
{
    public readonly string Key;
    public readonly Dictionary<string, object> Args = new Dictionary<string, object>();

    // new Msg("err.curve.points", "n", 8)
    public Msg(string key, params object[] nameValuePairs)
    {
        Key = key;
        for (int i = 0; i + 1 < nameValuePairs.Length; i += 2) Args[(string)nameValuePairs[i]] = nameValuePairs[i + 1];
    }

    public string Text { get { return In(I18n.English); } }
    public string In(string lang) { return I18n.T(lang, Key, Args); }
    public override string ToString() { return Text; }

    // What the API sends: the panel translates code with params, and shows text when it can't.
    public Dictionary<string, object> ToJson()
    {
        var d = new Dictionary<string, object> { { "code", Key }, { "text", Text } };
        if (Args.Count > 0) d["params"] = Args.ToDictionary(kv => kv.Key, kv => kv.Value is Msg ? ((Msg)kv.Value).ToJson() : kv.Value);
        return d;
    }

    // What to tell people about an exception: its message in lang if it is one of ours, else its own text.
    public static string TextOf(Exception ex, string lang)
    {
        var m = Of(ex);
        return m != null ? m.In(lang) : ex.Message;
    }

    // The message an exception carries for people, if it is one of ours.
    public static Msg Of(Exception ex)
    {
        var user = ex as UserError;
        if (user != null) return user.Msg;
        var unavailable = ex as Unavailable;
        return unavailable != null ? unavailable.Msg : null;
    }
}

// A request FanPilot turns down (400), saying why in any language.
sealed class UserError : ArgumentException
{
    public readonly Msg Msg;
    public UserError(string key, params object[] nameValuePairs) : this(new Msg(key, nameValuePairs)) { }
    public UserError(Msg msg) : base(msg.Text) { Msg = msg; }
}

// The hardware or a service can't do it now (503), saying why in any language.
sealed class Unavailable : InvalidOperationException
{
    public readonly Msg Msg;
    public Unavailable(string key, params object[] nameValuePairs) : this(new Msg(key, nameValuePairs), null) { }
    public Unavailable(Msg msg, Exception inner) : base(msg.Text, inner) { Msg = msg; }
}
