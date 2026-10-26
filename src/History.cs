// Item 24: up to 7 days of history on disk, next to the 10 minutes the sampler keeps in memory. Every 10 s the readings
// of those 10 s become one row (the average and the highest of each series) of data\history\<date>.csv, a file only
// ever appended to, so a crash loses at most the line being written. The API reads them back for the last hour, day or
// week, on a grid of at most 600 points.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

sealed class HistoryStore
{
    public const int RowSeconds = 10;
    public const int MaxPoints = 600;
    // /api/history?range=…; "10m" is the sampler's own, in memory.
    public static readonly Dictionary<string, TimeSpan> Ranges = new Dictionary<string, TimeSpan> {
        { "1h", TimeSpan.FromHours(1) }, { "24h", TimeSpan.FromHours(24) }, { "7d", TimeSpan.FromDays(7) },
    };

    sealed class Acc { public double Sum, Max = double.MinValue; public int Count; }

    readonly string _dir;
    readonly int _days;
    readonly Func<DateTime> _now;
    readonly object _sync = new object();
    readonly Dictionary<string, Acc> _acc = new Dictionary<string, Acc>();
    long _bucket = -1;       // start of the 10 s being gathered (Unix ms)
    string _file;            // the file the last row went to
    List<string> _columns;   // the series of the file's current part; null: a header comes first
    bool _failing;           // the log tells a write failure once, and the recovery

    public HistoryStore(string dir, int days) : this(dir, days, () => DateTime.Now) { }

    // now: the tests move the clock (it decides what is old enough to delete and what "the last day" is).
    internal HistoryStore(string dir, int days, Func<DateTime> now)
    {
        _dir = dir;
        _days = days;
        _now = now;
        Directory.CreateDirectory(dir);
        Prune();
    }

    // What a row keeps from a snapshot (Sampler.Sample): the CPU's main temperature (item 19) and power, the GPU's
    // temperature and power, and each fan's RPM, by position as the panel's charts take them.
    internal static Dictionary<string, double> Values(Dictionary<string, object> snap)
    {
        var values = new Dictionary<string, double>();
        object sensors, gpuObj, rpm;
        if (snap.TryGetValue("sensors", out sensors) && sensors is IEnumerable<Dictionary<string, object>>)
            foreach (var s in (IEnumerable<Dictionary<string, object>>)sensors)
            {
                if (Json.GetString(s, "role", null) == CpuRoles.Main) Put(values, CpuRoles.Main, Json.Get(s, "value"));
                if (Json.GetString(s, "type", "") == "Power" && Json.GetString(s, "title", "") == "CPU Package") Put(values, "cpu-power", Json.Get(s, "value"));
            }
        var gpu = snap.TryGetValue("gpu", out gpuObj) ? gpuObj as Dictionary<string, object> : null;
        if (gpu != null && Json.Get(gpu, "ok") is bool && (bool)Json.Get(gpu, "ok"))
        {
            Put(values, "gpu-temp", Json.Get(gpu, "temp"));
            Put(values, "gpu-power", Json.Get(gpu, "power"));
        }
        if (snap.TryGetValue("fanRpm", out rpm) && rpm is IList<int>)
        {
            var list = (IList<int>)rpm;
            for (int k = 0; k < list.Count; k++) values["fan-" + k] = list[k];
        }
        return values;
    }

    static void Put(Dictionary<string, double> values, string key, object v)
    {
        if (v == null) return;
        double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
        if (!double.IsNaN(d) && !double.IsInfinity(d)) values[key] = d;
    }

    // From the sampler, once a second. A row is written when the next 10 s begin.
    public void Add(Dictionary<string, object> snap)
    {
        long t = Convert.ToInt64(snap["t"], CultureInfo.InvariantCulture);
        long bucket = t - t % (RowSeconds * 1000L);
        lock (_sync)
        {
            if (bucket != _bucket)
            {
                if (_bucket >= 0) Flush();
                _bucket = bucket;
                _acc.Clear();
            }
            foreach (var kv in Values(snap))
            {
                Acc a;
                if (!_acc.TryGetValue(kv.Key, out a)) _acc[kv.Key] = a = new Acc();
                a.Sum += kv.Value;
                a.Count++;
                a.Max = Math.Max(a.Max, kv.Value);
            }
        }
    }

    static string FileName(DateTime day) { return day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".csv"; }

    static DateTime LocalTime(long unixMs) { return DateTimeOffset.FromUnixTimeMilliseconds(unixMs).LocalDateTime; }

    // "t,cpu-main,cpu-main:max,…" then "1761400000000,62.4,66,…". A series that shows up later (the GPU's driver
    // starting late) starts a new part of the file, with its own header; a series missing from a row stays empty.
    void Flush()
    {
        if (_acc.Count == 0) return;
        try
        {
            string file = Path.Combine(_dir, FileName(LocalTime(_bucket)));
            if (file != _file)
            {
                _file = file;
                _columns = null;
                Prune(); // a new day: what fell out of the retention goes
            }
            var sb = new StringBuilder();
            var keys = _acc.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
            if (_columns == null || keys.Any(k => !_columns.Contains(k)))
            {
                _columns = (_columns ?? new List<string>()).Concat(keys.Where(k => _columns == null || !_columns.Contains(k))).ToList();
                sb.Append('t');
                foreach (string c in _columns) sb.Append(',').Append(c).Append(',').Append(c).Append(":max");
                sb.Append('\n');
            }
            sb.Append(_bucket.ToString(CultureInfo.InvariantCulture));
            foreach (string c in _columns)
            {
                Acc a;
                if (_acc.TryGetValue(c, out a) && a.Count > 0) sb.Append(',').Append(Num(a.Sum / a.Count)).Append(',').Append(Num(a.Max));
                else sb.Append(",,");
            }
            sb.Append('\n');
            File.AppendAllText(file, sb.ToString(), Encoding.ASCII);
            if (_failing) { _failing = false; Log.Info("History: writing again."); }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            if (!_failing) { _failing = true; Log.Warn("History: could not write to " + _dir + ": " + ex.Message); }
            _columns = null; // the header goes again with the next row that does get written
        }
    }

    static string Num(double v) { return Math.Round(v, 1).ToString("0.#", CultureInfo.InvariantCulture); }

    // Days beyond the retention are deleted: today and the _days before it stay, enough for a whole week back.
    public void Prune()
    {
        DateTime oldest = _now().Date.AddDays(-_days);
        foreach (string f in Directory.GetFiles(_dir, "*.csv"))
        {
            DateTime day;
            if (!DateTime.TryParseExact(Path.GetFileNameWithoutExtension(f), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day) || day >= oldest) continue;
            try { File.Delete(f); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { Log.Warn("History: could not delete " + f + ": " + ex.Message); }
        }
    }

    // One series on the grid of Read: per point, the sum and count of the rows' averages, the highest of their maximums
    // and the lowest of their averages.
    sealed class Grid
    {
        public readonly double[] Sum, Max, Min;
        public readonly int[] Count;

        public Grid(int points)
        {
            Sum = new double[points];
            Count = new int[points];
            Max = Enumerable.Repeat(double.NaN, points).ToArray();
            Min = Enumerable.Repeat(double.NaN, points).ToArray();
        }

        public void Add(int i, double avg, double max)
        {
            Sum[i] += avg;
            Count[i]++;
            Max[i] = double.IsNaN(Max[i]) ? max : Math.Max(Max[i], max);
            Min[i] = double.IsNaN(Min[i]) ? avg : Math.Min(Min[i], avg);
        }
    }

    // The last span, on a grid of at most maxPoints from then to now: for each point, the average of the averages of
    // its rows, the highest of their maximums and the lowest of their averages; null where there is no row (FanPilot
    // was closed), so the chart breaks the line there.
    public Dictionary<string, object> Read(TimeSpan span, int maxPoints = MaxPoints)
    {
        long now = new DateTimeOffset(_now()).ToUnixTimeMilliseconds();
        long from = now - (long)span.TotalMilliseconds;
        long step = Math.Max(1, (long)Math.Ceiling(span.TotalMilliseconds / maxPoints / (RowSeconds * 1000.0))) * RowSeconds * 1000L;
        int points = (int)Math.Ceiling((now - from) / (double)step);
        var grids = new Dictionary<string, Grid>();
        string[] part = null;
        Grid[] columns = null; // the grids of the part's series, in its order
        // No lock: the files are only appended to, and a row being written is the last line, which Rows skips.
        for (var day = LocalTime(from).Date; day <= LocalTime(now).Date; day = day.AddDays(1))
            foreach (var row in Rows(Path.Combine(_dir, FileName(day))))
            {
                if (row.T < from || row.T > now) continue;
                if (!ReferenceEquals(row.Series, part))
                {
                    part = row.Series;
                    columns = part.Select(name => { Grid g; if (!grids.TryGetValue(name, out g)) grids[name] = g = new Grid(points); return g; }).ToArray();
                }
                int i = Math.Min(points - 1, (int)((row.T - from) / step));
                for (int c = 0; c < columns.Length; c++)
                    if (!double.IsNaN(row.Avg[c])) columns[c].Add(i, row.Avg[c], row.Max[c]);
            }
        Func<double, object> cell = d => double.IsNaN(d) ? null : (object)Math.Round(d, 1);
        return new Dictionary<string, object> {
            { "t", Enumerable.Range(0, points).Select(i => from + i * step).ToList() },
            { "step", step },
            { "now", now },
            { "series", grids.ToDictionary(kv => kv.Key, kv => (object)kv.Value.Sum.Select((s, i) => kv.Value.Count[i] == 0 ? null : (object)Math.Round(s / kv.Value.Count[i], 1)).ToList()) },
            { "max", grids.ToDictionary(kv => kv.Key, kv => (object)kv.Value.Max.Select(cell).ToList()) },
            { "min", grids.ToDictionary(kv => kv.Key, kv => (object)kv.Value.Min.Select(cell).ToList()) },
        };
    }

    // A row of a file: its time and, for each series of its part's header, the average and the maximum (NaN where the
    // series has no reading in the row).
    internal sealed class Row
    {
        public long T;
        public string[] Series; // the same array for every row of a part
        public double[] Avg, Max;

        // [average, maximum] of a series, or null.
        public double[] this[string series]
        {
            get
            {
                int c = Array.IndexOf(Series, series);
                return c < 0 || double.IsNaN(Avg[c]) ? null : new[] { Avg[c], Max[c] };
            }
        }
    }

    // The rows of one file. A header line starts a new part. Only lines that end in a line break count: the last one,
    // cut by a power loss or still being written, never does. A line that doesn't match its header is skipped too.
    // A week is ~60,000 rows: they are read in place, without splitting the text into strings.
    internal static IEnumerable<Row> Rows(string file)
    {
        if (!File.Exists(file)) yield break;
        string text;
        try
        {
            // FileShare.ReadWrite: the store may be appending to it right now.
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream, Encoding.ASCII))
                text = reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { yield break; }
        string[] series = null;
        for (int start = 0, end; (end = text.IndexOf('\n', start)) >= 0; start = end + 1)
        {
            int stop = end > start && text[end - 1] == '\r' ? end - 1 : end;
            if (string.CompareOrdinal(text, start, "t,", 0, 2) == 0)
            {
                // "t,cpu-main,cpu-main:max,…": a series every other column.
                var header = text.Substring(start, stop - start).Split(',');
                series = header.Length % 2 == 1 ? header.Where((h, k) => k % 2 == 1).ToArray() : null;
                continue;
            }
            if (series == null) continue;
            var row = ParseRow(text, start, stop, series);
            if (row != null) yield return row;
        }
    }

    // null when the line doesn't match its header: a cell missing or extra, or one that isn't a number.
    static Row ParseRow(string text, int start, int stop, string[] series)
    {
        var row = new Row { Series = series, Avg = new double[series.Length], Max = new double[series.Length] };
        int comma = text.IndexOf(',', start, stop - start);
        double t;
        if (comma < 0 || !Number(text, start, comma, out t) || t != Math.Floor(t)) return null;
        row.T = (long)t;
        for (int c = 0; c < series.Length; c++)
        {
            // The average up to the next comma; the maximum up to the one after it, or to the end of the line.
            int avgStart = comma + 1;
            int avgEnd = text.IndexOf(',', avgStart, stop - avgStart);
            if (avgEnd < 0) return null;
            int maxStart = avgEnd + 1;
            comma = text.IndexOf(',', maxStart, stop - maxStart);
            bool last = c == series.Length - 1;
            if (last != (comma < 0)) return null; // a cell more or less than the header
            if (avgEnd == avgStart) { row.Avg[c] = row.Max[c] = double.NaN; continue; } // no reading in this row
            if (!Number(text, avgStart, avgEnd, out row.Avg[c]) || !Number(text, maxStart, last ? stop : comma, out row.Max[c])) return null;
        }
        return row;
    }

    // The numbers Flush writes ("62.4", "1250", "-3"), parsed in place; anything else goes through double.TryParse.
    static bool Number(string s, int start, int end, out double v)
    {
        int i = start;
        bool negative = i < end && s[i] == '-';
        if (negative) i++;
        long whole = 0, fraction = 0, scale = 1;
        int digits = 0;
        for (; i < end && s[i] >= '0' && s[i] <= '9'; i++, digits++) whole = whole * 10 + (s[i] - '0');
        if (i < end && s[i] == '.')
            for (i++; i < end && s[i] >= '0' && s[i] <= '9'; i++, digits++) { fraction = fraction * 10 + (s[i] - '0'); scale *= 10; }
        if (i == end && digits > 0 && digits <= 15)
        {
            v = whole + (double)fraction / scale;
            if (negative) v = -v;
            return true;
        }
        return double.TryParse(s.Substring(start, end - start), NumberStyles.Float, CultureInfo.InvariantCulture, out v) &&
               !double.IsNaN(v) && !double.IsInfinity(v);
    }
}
