// The curve presets, in one place (item 18): the editor's chips (the panel reads them from /api/fans) and the global
// profiles use the same ones. [°C, %], 8 points each, resampled to each header's own point count.
using System;
using System.Collections.Generic;
using System.Linq;

static class FanPresets
{
    sealed class Preset
    {
        public string Name;
        public int[][] Points;
    }

    static readonly Dictionary<string, Preset> All = new Dictionary<string, Preset> {
        { "quiet", new Preset { Name = "Silencioso", Points = new[] { new[] { 0, 25 }, new[] { 40, 25 }, new[] { 50, 30 }, new[] { 60, 40 }, new[] { 65, 50 }, new[] { 70, 65 }, new[] { 75, 80 }, new[] { 85, 100 } } } },
        { "standard", new Preset { Name = "Padrão", Points = new[] { new[] { 0, 30 }, new[] { 35, 30 }, new[] { 45, 40 }, new[] { 55, 55 }, new[] { 62, 65 }, new[] { 68, 75 }, new[] { 74, 85 }, new[] { 82, 100 } } } },
        { "performance", new Preset { Name = "Performance", Points = new[] { new[] { 0, 45 }, new[] { 30, 45 }, new[] { 40, 55 }, new[] { 50, 70 }, new[] { 58, 80 }, new[] { 64, 90 }, new[] { 70, 95 }, new[] { 78, 100 } } } },
        { "max", new Preset { Name = "Máximo", Points = new[] { new[] { 0, 100 }, new[] { 20, 100 }, new[] { 30, 100 }, new[] { 40, 100 }, new[] { 50, 100 }, new[] { 60, 100 }, new[] { 70, 100 }, new[] { 80, 100 } } } },
    };

    // The global modes that are profiles ("Máximo" is the Turbo's job).
    static readonly string[] Profiles = { "quiet", "standard", "performance" };

    public static bool IsProfile(string mode)
    {
        return mode != null && mode.StartsWith("profile:") && Profiles.Contains(mode.Substring("profile:".Length));
    }

    public static string NameOf(string preset) { return All[preset].Name; }

    // For /api/fans: { "quiet": { "name": "Silencioso", "points": [[0,25], ...] }, ... }, in the panel's order.
    public static Dictionary<string, object> Describe()
    {
        return All.ToDictionary(kv => kv.Key, kv => (object)new Dictionary<string, object> {
            { "name", kv.Value.Name }, { "points", kv.Value.Points.Select(p => new[] { p[0], p[1] }).ToList() } });
    }

    // The preset with n points, as the editor builds it (wwwroot/js/curve.mjs resample and normalize, same rounding):
    // the full-speed point no hotter than maxFullSpeedTemp, every point at least minPercent, never slower when hotter,
    // 100% at the last one, and temperatures that keep increasing even when the limit squeezes them.
    public static List<int[]> Points(string preset, int n, int maxFullSpeedTemp, int minPercent)
    {
        var src = All[preset].Points.Select(p => new double[] { Math.Min(p[0], maxFullSpeedTemp), p[1] }).ToList();
        var curve = new FanCurve { Mode = "SmartFanMode", Points = src };
        double t0 = src[0][0], t1 = src[src.Count - 1][0];
        var pts = src.Count == n
            ? src.Select(p => new[] { (int)p[0], (int)p[1] }).ToList()
            : Enumerable.Range(0, n).Select(k =>
            {
                int t = RoundJs(t0 + (t1 - t0) * k / (n - 1));
                return new[] { t, RoundJs(curve.PwmAt(t)) };
            }).ToList();
        for (int i = pts.Count - 2; i >= 0; i--) pts[i][0] = Math.Min(pts[i][0], pts[i + 1][0] - 1);
        pts[0][0] = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            pts[i][1] = Math.Max(minPercent, Math.Min(100, pts[i][1]));
            if (i > 0) pts[i][1] = Math.Max(pts[i][1], pts[i - 1][1]);
        }
        pts[pts.Count - 1][1] = 100;
        return pts;
    }

    // JavaScript's Math.round for the values here (non-negative): .5 goes up, unlike Math.Round's default.
    static int RoundJs(double v) { return (int)Math.Floor(v + 0.5); }
}
