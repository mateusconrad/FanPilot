// The terminal's screen (item 29): a TerminalView drawn as exactly height lines of at most width columns, in ANSI
// colors (16 of them, which every terminal has) or plain (the tests). Bars and sparklines are block characters, single
// width in every console font FanPilot has met (Consolas, Cascadia, DejaVu Sans Mono).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

static class TerminalScreen
{
    // Below this many columns the bars and sparklines go and the numbers stay; below MinRows, only the temperatures.
    public const int MinBarColumns = 60, MinRows = 10;
    const string Spark = "▁▂▃▄▅▆▇█";

    enum Style { Plain, Dim, Bold, Green, Yellow, Red, Cyan }

    // One line in pieces, so that its width can be counted without the color codes.
    sealed class Line
    {
        readonly List<KeyValuePair<string, Style>> _parts = new List<KeyValuePair<string, Style>>();
        string _link;

        public int Width { get { return _parts.Sum(p => p.Key.Length); } }

        public Line Add(string text, Style style = Style.Plain)
        {
            if (!string.IsNullOrEmpty(text)) _parts.Add(new KeyValuePair<string, Style>(text, style));
            return this;
        }

        // The address as a clickable link (OSC 8) where the terminal knows it; others show the text.
        public Line Link(string url, Style style)
        {
            _link = url;
            return Add(url, style);
        }

        public string Render(int width, bool color)
        {
            var sb = new StringBuilder();
            int left = width;
            foreach (var p in _parts)
            {
                if (left <= 0) break;
                string text = p.Key.Length > left ? p.Key.Substring(0, left) : p.Key;
                left -= text.Length;
                bool link = color && _link != null && p.Key == _link && text == p.Key;
                if (link) sb.Append("\u001b]8;;").Append(_link).Append("\u001b\\");
                if (color && p.Value != Style.Plain) sb.Append(Code(p.Value)).Append(text).Append("\u001b[0m");
                else sb.Append(text);
                if (link) sb.Append("\u001b]8;;\u001b\\");
            }
            return sb.ToString();
        }

        static string Code(Style s)
        {
            switch (s)
            {
                case Style.Dim: return "\u001b[90m";
                case Style.Bold: return "\u001b[1m";
                case Style.Green: return "\u001b[32m";
                case Style.Yellow: return "\u001b[33m";
                case Style.Red: return "\u001b[31m";
                case Style.Cyan: return "\u001b[36m";
                default: return "";
            }
        }
    }

    public static List<string> Render(TerminalView v, int width, int height, string lang, bool color)
    {
        width = Math.Max(20, width);
        height = Math.Max(3, height);
        bool bars = width >= MinBarColumns;
        string T(string key, params object[] args) { return new Msg(key, args).In(lang); }

        var head = Header(v, width, lang);
        var body = new List<Line>();
        if (v.Problem != null) body.Add(new Line().Add(" " + v.Problem.In(lang), Style.Yellow));
        if (v.Temps.Count > 0 || v.Fans.Count > 0)
        {
            int nameWidth = Math.Min(16, Math.Max(6, v.Temps.Select(t => t.Title.Length).Concat(v.Fans.Select(f => f.Title.Length)).DefaultIfEmpty(0).Max()));
            body.Add(Section(T("tui.temps"), width));
            body.AddRange(v.Temps.Select(t => TempLine(t, nameWidth, width, bars, lang)));
            if (height >= MinRows)
            {
                body.Add(Section(T("tui.fans"), width));
                body.AddRange(v.Fans.Select(f => FanLine(f, nameWidth, width, bars, lang)));
                foreach (string alert in v.Alerts.Take(3)) body.Add(new Line().Add(" ⚠ " + alert, Style.Red));
            }
        }
        var foot = Footer(v, width, lang);

        int room = height - 1 - foot.Count;
        var lines = new List<Line> { head };
        lines.AddRange(body.Take(Math.Max(0, room)));
        while (lines.Count < height - foot.Count) lines.Add(new Line());
        lines.AddRange(foot);
        return lines.Take(height).Select(l => l.Render(width, color)).ToList();
    }

    // The log instead of the numbers (key l): its last lines, newest at the bottom.
    public static List<string> RenderLog(IList<string> log, int width, int height, string lang, bool color)
    {
        width = Math.Max(20, width);
        height = Math.Max(3, height);
        var lines = new List<Line> { new Line().Add(" FanPilot · log", Style.Bold).Add("   [l] " + new Msg("tui.key.back").In(lang), Style.Dim) };
        int room = height - 1;
        foreach (string l in log.Skip(Math.Max(0, log.Count - room))) lines.Add(new Line().Add(l, l.Contains("[ERROR]") ? Style.Red : l.Contains("[WARN]") ? Style.Yellow : Style.Plain));
        while (lines.Count < height) lines.Add(new Line());
        return lines.Select(l => l.Render(width, color)).ToList();
    }

    // Version, backend, profile and Turbo on the left, the panel's address on the right. Short of room, the backend goes
    // first and the address last: it is what the screen is for when the numbers aren't enough.
    static Line Header(TerminalView v, int width, string lang)
    {
        string open = "  [o] " + new Msg("tui.key.openShort").In(lang) + " ";
        foreach (bool backend in new[] { true, false })
        {
            var left = Left(v, backend);
            int gap = width - left.Width - v.Url.Length - open.Length;
            if (gap >= 2) return left.Add(new string(' ', gap)).Link(v.Url, Style.Cyan).Add(open, Style.Dim);
        }
        return Left(v, true);
    }

    static Line Left(TerminalView v, bool backend)
    {
        var left = new Line().Add(" FanPilot " + v.Version, Style.Bold);
        if (backend && v.Backend.Length > 0) left.Add(" · " + v.Backend);
        if (v.Profile.Length > 0) left.Add(" · " + v.Profile);
        if (v.Turbo) left.Add(" · TURBO", Style.Red);
        return left;
    }

    static Line Section(string title, int width)
    {
        string text = " ─ " + title + " ";
        return new Line().Add(text + new string('─', Math.Max(0, width - text.Length - 1)), Style.Dim);
    }

    static Line TempLine(TerminalView.Temp t, int nameWidth, int width, bool bars, string lang)
    {
        var style = TempStyle(t.Value, t.Limit);
        var line = new Line().Add("  " + Fit(t.Title, nameWidth) + " ")
            .Add(t.Value.HasValue ? Right(Math.Round(t.Value.Value).ToString(CultureInfo.InvariantCulture), 4) + " °C" : "   — °C", style);
        if (!bars) return line;
        int rest = width - line.Width - 2;
        var history = t.History.Where(h => h.HasValue).Select(h => h.Value).ToList();
        string max = history.Count > 0 ? new Msg("tui.max", "value", Math.Round(history.Max())).In(lang) : null;
        int sparkWidth = history.Count > 1 && rest >= 44 ? 12 : 0;
        bool showMax = max != null && rest - sparkWidth >= 30 + max.Length;
        int barWidth = Math.Max(10, Math.Min(30, rest - (sparkWidth > 0 ? sparkWidth + 2 : 0) - (showMax ? max.Length + 2 : 0)));
        line.Add("  ").Add(Bar(t.Value.HasValue ? t.Value.Value / 100 : 0, barWidth, out string empty), style).Add(empty, Style.Dim);
        if (sparkWidth > 0) line.Add("  ").Add(Sparkline(t.History, sparkWidth), style);
        if (showMax) line.Add("  " + max, Style.Dim);
        return line;
    }

    // Red from the alert's limit (item 16), yellow from 15 °C below it.
    static Style TempStyle(double? value, int limit)
    {
        if (!value.HasValue) return Style.Dim;
        if (value.Value >= limit) return Style.Red;
        return value.Value >= limit - 15 ? Style.Yellow : Style.Green;
    }

    static Line FanLine(TerminalView.Fan f, int nameWidth, int width, bool bars, string lang)
    {
        var culture = CultureInfo.GetCultureInfo(lang == I18n.Portuguese ? "pt-BR" : "en-US");
        string rpm = f.Rpm.HasValue ? f.Rpm.Value.ToString("N0", culture) + " RPM" : "—";
        string duty = f.Duty.HasValue ? (f.Estimated ? "~" : "") + Math.Round(f.Duty.Value).ToString(CultureInfo.InvariantCulture) + " %" : "— %";
        var line = new Line().Add("  " + Fit(f.Title, nameWidth) + " ").Add(Right(rpm, 10));
        if (bars)
        {
            int barWidth = Math.Max(10, Math.Min(30, width - line.Width - 2 - 7 - 2 - f.Mode.Length - 1));
            line.Add("  ").Add(Bar(f.Duty.HasValue ? f.Duty.Value / 100 : 0, barWidth, out string empty), Style.Cyan).Add(empty, Style.Dim);
        }
        return line.Add(" " + Right(duty, 6), Style.Bold).Add("  " + f.Mode, Style.Dim);
    }

    static List<Line> Footer(TerminalView v, int width, string lang)
    {
        string T(string key, params object[] args) { return new Msg(key, args).In(lang); }
        var keys = new StringBuilder(" [o] " + T("tui.key.open") + "  [t] " + T("tui.key.turbo"));
        if (v.HasLog) keys.Append("  [l] ").Append(T("tui.key.log"));
        keys.Append("  [q] ").Append(T(v.OwnsFanPilot ? "tui.key.close" : "tui.key.quit"));
        var status = new List<string>();
        if (v.Age.HasValue) status.Add(T("tui.updated", "seconds", (int)Math.Max(0, v.Age.Value.TotalSeconds)));
        status.Add(T("tui.alerts", "count", v.Alerts.Count));
        string right = string.Join(" · ", status) + " ";
        var line = new Line().Add(keys.ToString(), Style.Dim);
        int gap = width - line.Width - right.Length;
        if (gap >= 2) line.Add(new string(' ', gap)).Add(right, v.Alerts.Count > 0 ? Style.Red : Style.Dim);
        return new List<Line> { new Line().Add(new string('─', Math.Max(0, width - 1)), Style.Dim), line };
    }

    static string Bar(double fraction, int width, out string empty)
    {
        int full = (int)Math.Round(Math.Max(0, Math.Min(1, fraction)) * width);
        empty = new string('░', width - full);
        return new string('█', full);
    }

    // The last ten minutes in width columns, each the highest reading of its stretch, scaled to what the stretch spans
    // (at least 10 °C, so a steady temperature draws flat).
    internal static string Sparkline(IList<double?> history, int width)
    {
        if (history.Count == 0) return new string(' ', width);
        var buckets = new double?[width];
        for (int i = 0; i < width; i++)
        {
            int from = (int)((long)i * history.Count / width), to = (int)((long)(i + 1) * history.Count / width);
            var values = history.Skip(from).Take(Math.Max(1, to - from)).Where(h => h.HasValue).Select(h => h.Value).ToList();
            buckets[i] = values.Count > 0 ? values.Max() : (double?)null;
        }
        var known = buckets.Where(b => b.HasValue).Select(b => b.Value).ToList();
        if (known.Count == 0) return new string(' ', width);
        double low = known.Min(), span = Math.Max(10, known.Max() - low);
        return new string(buckets.Select(b => b.HasValue ? Spark[(int)Math.Min(Spark.Length - 1, Math.Round((b.Value - low) / span * (Spark.Length - 1)))] : ' ').ToArray());
    }

    static string Fit(string text, int width) { return text.Length > width ? text.Substring(0, width - 1) + "…" : text.PadRight(width); }

    static string Right(string text, int width) { return text.PadLeft(width); }
}
