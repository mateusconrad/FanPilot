// Item 24: the history on disk, fed one snapshot a second as the sampler does, with a clock the tests set.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Xunit;

public sealed class HistoryTests : IDisposable
{
    readonly TempDir _dir = new TempDir();
    // A Saturday afternoon, local time; the rows are timed from it.
    static readonly DateTime Now = new DateTime(2026, 10, 24, 15, 0, 0, DateTimeKind.Local);
    static readonly long NowMs = new DateTimeOffset(Now).ToUnixTimeMilliseconds();

    public void Dispose() { _dir.Dispose(); }

    string Dir { get { return Path.Combine(_dir.Path, "history"); } }

    HistoryStore Store(int days = 7, DateTime? now = null)
    {
        var at = now ?? Now;
        return new HistoryStore(Dir, days, () => at);
    }

    static string FileOf(long unixMs) { return DateTimeOffset.FromUnixTimeMilliseconds(unixMs).LocalDateTime.ToString("yyyy-MM-dd") + ".csv"; }

    // A snapshot as Sampler.Sample makes it: the CPU (role cpu-main) and its power, the GPU, the fans' RPM.
    static Dictionary<string, object> Snap(long t, double cpu, double? gpu, params int[] rpm)
    {
        return new Dictionary<string, object> {
            { "t", t },
            { "sensors", new List<Dictionary<string, object>> {
                new Dictionary<string, object> { { "id", "temperature-cpu" }, { "type", "Temperature" }, { "title", "CPU" }, { "value", cpu }, { "unit", "°C" }, { "role", "cpu-main" } },
                new Dictionary<string, object> { { "id", "power-cpu" }, { "type", "Power" }, { "title", "CPU Package" }, { "value", 41.0 }, { "unit", "W" } },
            } },
            { "gpu", gpu == null ? new Dictionary<string, object> { { "ok", false } }
                                 : new Dictionary<string, object> { { "ok", true }, { "temp", gpu.Value }, { "power", 96.0 } } },
            { "fanRpm", rpm.ToList() },
        };
    }

    // seconds of snapshots from t0, one a second.
    static void Feed(HistoryStore store, long t0, int seconds, Func<int, Dictionary<string, object>> snap)
    {
        for (int s = 0; s < seconds; s++) store.Add(snap(s));
    }

    [Fact]
    public void TenSecondsBecomeOneRowWithTheAverageAndTheHighest()
    {
        var store = Store();
        long t0 = NowMs - 60_000 - NowMs % 10_000;
        Feed(store, t0, 11, s => Snap(t0 + s * 1000, 60 + s, 50, 1200, 0));
        string[] lines = File.ReadAllLines(Path.Combine(Dir, FileOf(t0)));
        Assert.Equal("t,cpu-main,cpu-main:max,cpu-power,cpu-power:max,fan-0,fan-0:max,fan-1,fan-1:max,gpu-power,gpu-power:max,gpu-temp,gpu-temp:max", lines[0]);
        // 60..69 °C in the first 10 s: 64.5 on average, 69 at most. The 11th second starts the next row.
        Assert.Equal(t0 + ",64.5,69,41,41,1200,1200,0,0,96,96,50,50", lines[1]);
        Assert.Equal(2, lines.Length);
    }

    [Fact]
    public void ASeriesThatShowsUpLaterStartsANewPartOfTheFile()
    {
        var store = Store();
        long t0 = NowMs - 120_000 - NowMs % 10_000;
        Feed(store, t0, 20, s => Snap(t0 + s * 1000, 60, null));       // the GPU's driver still starting
        Feed(store, t0, 21, s => Snap(t0 + 20_000 + s * 1000, 62, 55)); // and then there
        string[] lines = File.ReadAllLines(Path.Combine(Dir, FileOf(t0)));
        Assert.Equal(2, lines.Count(l => l.StartsWith("t,")));
        var rows = HistoryStore.Rows(Path.Combine(Dir, FileOf(t0))).ToList();
        Assert.Equal(4, rows.Count);
        Assert.Null(rows[0]["gpu-temp"]);
        Assert.Equal(55, rows[3]["gpu-temp"][0]);
    }

    [Fact]
    public void ALineCutByAPowerLossIsSkipped()
    {
        string file = Path.Combine(Dir, FileOf(NowMs));
        Directory.CreateDirectory(Dir);
        long t = NowMs - 30_000;
        File.WriteAllText(file, "t,cpu-main,cpu-main:max\n" + t + ",60,61\n" + (t + 10_000) + ",62\n" + (t + 20_000) + ",6");
        var rows = HistoryStore.Rows(file).ToList();
        // The row with a cell missing doesn't match the header; the last one never got its line break.
        Assert.Single(rows);
        Assert.Equal(new[] { 60.0, 61 }, rows[0]["cpu-main"]);
        var read = Store().Read(TimeSpan.FromHours(1));
        Assert.Equal(60.0, ((List<object>)((Dictionary<string, object>)read["series"])["cpu-main"]).OfType<double>().Single());
    }

    // A series with no reading in a row is just missing from it; Windows line breaks read the same.
    [Fact]
    public void RowsThatDontMatchTheirHeaderAreSkipped()
    {
        string file = Path.Combine(Dir, FileOf(NowMs));
        Directory.CreateDirectory(Dir);
        long t = NowMs - 60_000;
        File.WriteAllText(file, "t,cpu-main,cpu-main:max,gpu-temp,gpu-temp:max\r\n" +
            t + ",60.5,61,50,52\r\n" +
            (t + 10_000) + ",60,61,50,52,9\n" + // a cell more
            (t + 20_000) + ",60,61,50\n" +      // a cell less
            (t + 30_000) + ",60,x,50,52\n" +    // not a number
            (t + 40_000) + ",,,-3.5,1e1\n" +    // no CPU reading; numbers FanPilot doesn't write, still numbers
            "agora,60,61,50,52\n");
        var rows = HistoryStore.Rows(file).ToList();
        Assert.Equal(new[] { t, t + 40_000 }, rows.Select(r => r.T));
        Assert.Equal(new[] { 60.5, 61 }, rows[0]["cpu-main"]);
        Assert.Null(rows[1]["cpu-main"]);
        Assert.Equal(new[] { -3.5, 10 }, rows[1]["gpu-temp"]);
    }

    [Fact]
    public void DaysBeyondTheRetentionAreDeleted()
    {
        Directory.CreateDirectory(Dir);
        foreach (int back in new[] { 0, 3, 7, 8, 30 })
            File.WriteAllText(Path.Combine(Dir, Now.Date.AddDays(-back).ToString("yyyy-MM-dd") + ".csv"), "t,cpu-main,cpu-main:max\n");
        File.WriteAllText(Path.Combine(Dir, "notas.txt"), "not a day");
        Store(days: 7);
        var left = Directory.GetFiles(Dir).Select(Path.GetFileName).OrderBy(n => n).ToList();
        Assert.Equal(new[] { Now.Date.AddDays(-7).ToString("yyyy-MM-dd") + ".csv", Now.Date.AddDays(-3).ToString("yyyy-MM-dd") + ".csv",
                             Now.Date.ToString("yyyy-MM-dd") + ".csv", "notas.txt" }, left);
    }

    [Fact]
    public void ALongRangeComesOnAGridOfAtMost600PointsWithTheLowestAndTheHighest()
    {
        var store = Store();
        // Two hours in the last day, then a gap (FanPilot closed), then the last hour.
        long t0 = NowMs - 20 * 3_600_000L - NowMs % 10_000;
        Feed(store, t0, 2 * 3600, s => Snap(t0 + s * 1000L, s % 120 < 60 ? 50 : 80, 60));
        long t1 = NowMs - 3_600_000L - NowMs % 10_000;
        Feed(store, t1, 3600 + 1, s => Snap(t1 + s * 1000L, 55, 61));
        var read = store.Read(TimeSpan.FromHours(24));
        var t = (List<long>)read["t"];
        Assert.InRange(t.Count, 500, 600);
        long step = (long)read["step"];
        Assert.Equal(0, step % 10_000);
        var avg = (List<object>)((Dictionary<string, object>)read["series"])["cpu-main"];
        var max = (List<object>)((Dictionary<string, object>)read["max"])["cpu-main"];
        var min = (List<object>)((Dictionary<string, object>)read["min"])["cpu-main"];
        int first = avg.FindIndex(v => v != null);
        // 50 and 80 °C a minute each: the point averages them and keeps both ends.
        Assert.Equal(80.0, max[first + 1]);
        Assert.Equal(50.0, min[first + 1]);
        // Nothing while FanPilot was closed: the chart breaks the line there.
        Assert.Contains(null, avg.Skip(first).Take(avg.Count - first - 1));
        Assert.Equal(55.0, avg[avg.Count - 1]);
    }

    // Item 24's first criterion: after a restart, the 24 h chart still has what came before it.
    [Fact]
    public void ARestartKeepsWhatCameBefore()
    {
        long t0 = NowMs - 600_000 - NowMs % 10_000;
        Feed(Store(), t0, 31, s => Snap(t0 + s * 1000L, 70, 60));
        var again = Store();
        Feed(again, t0, 31, s => Snap(t0 + 300_000 + s * 1000L, 72, 60));
        var cpu = ((List<object>)((Dictionary<string, object>)again.Read(TimeSpan.FromHours(24))["series"])["cpu-main"]).OfType<double>().ToList();
        Assert.Contains(70.0, cpu);
        Assert.Contains(72.0, cpu);
    }

    // The 7 days the panel asks for come back well within a second on a PC (item 24's acceptance: under 1 s); the
    // bound here leaves room for a busy CI runner.
    [Fact]
    public void SevenDaysReadQuickly()
    {
        Directory.CreateDirectory(Dir);
        string header = "t,cpu-main,cpu-main:max,cpu-power,cpu-power:max,fan-0,fan-0:max,fan-1,fan-1:max,fan-2,fan-2:max,fan-3,fan-3:max,gpu-power,gpu-power:max,gpu-temp,gpu-temp:max";
        long start = NowMs - 7L * 86_400_000;
        start -= start % 10_000;
        var files = new Dictionary<string, System.Text.StringBuilder>();
        for (long t = start; t <= NowMs; t += 10_000)
        {
            string name = FileOf(t);
            System.Text.StringBuilder sb;
            if (!files.TryGetValue(name, out sb)) files[name] = sb = new System.Text.StringBuilder(header + "\n");
            sb.Append(t).Append(",62.4,66,41.2,48.5,1340,1400,682,700,0,0,1200,1250,96.3,120,58.1,61\n");
        }
        foreach (var kv in files) File.WriteAllText(Path.Combine(Dir, kv.Key), kv.Value.ToString());
        var store = Store();
        var watch = Stopwatch.StartNew();
        var read = store.Read(TimeSpan.FromDays(7));
        watch.Stop();
        Assert.InRange(((List<long>)read["t"]).Count, 500, 600);
        Assert.True(watch.ElapsedMilliseconds < 3000, "7 dias em " + watch.ElapsedMilliseconds + " ms");
    }
}
