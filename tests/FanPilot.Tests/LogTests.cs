// Item 06: the log stays small (rotated by size) and readable (a line repeated in a row is summarized).
using System;
using System.IO;
using System.Linq;
using Xunit;

public sealed class LogTests : IDisposable
{
    readonly TempDir _dir = new TempDir();
    DateTime _now = new DateTime(2026, 10, 24, 12, 0, 0);

    public void Dispose() { _dir.Dispose(); }

    LogWriter Writer(long maxBytes = 5 * 1024 * 1024)
    {
        return new LogWriter(() => _now, maxBytes) { FilePath = _dir.File("fanpilot.log"), ToConsole = false };
    }

    string[] Lines() { return File.ReadAllLines(_dir.File("fanpilot.log")); }

    [Fact]
    public void AnErrorEverySecondForTenMinutesCostsAFewLines()
    {
        var log = Writer();
        for (int i = 0; i < 600; i++)
        {
            log.Write("WARN", "LibreHardwareMonitor: falha ao ler o chip");
            _now = _now.AddSeconds(1);
        }
        var lines = Lines();
        Assert.InRange(lines.Length, 2, 20);
        Assert.Equal("2026-10-24 12:00:00 [WARN] LibreHardwareMonitor: falha ao ler o chip", lines[0]);
        Assert.Contains("repeated", lines[1]);
    }

    [Fact]
    public void ADifferentLineFirstReportsTheRepeats()
    {
        var log = Writer();
        log.Write("WARN", "A");
        log.Write("WARN", "A");
        log.Write("WARN", "A");
        log.Write("INFO", "B");
        var lines = Lines();
        Assert.Equal(3, lines.Length);
        Assert.EndsWith("[WARN] A", lines[0]);
        Assert.Contains("repeated 2 times", lines[1]);
        Assert.EndsWith("[INFO] B", lines[2]);
    }

    // A line for the person at the console (Log.Info(Msg)) goes to the file in English (item 21).
    [Fact]
    public void TheFileKeepsTheEnglishOfAConsoleLine()
    {
        var log = Writer();
        log.Write("INFO", "To pair a phone, open the page.", "Para parear o celular, abra a página.");
        Assert.EndsWith("[INFO] To pair a phone, open the page.", Assert.Single(Lines()));
    }

    [Fact]
    public void SameTextAtAnotherLevelIsNotARepeat()
    {
        var log = Writer();
        log.Write("WARN", "A");
        log.Write("ERROR", "A");
        Assert.Equal(2, Lines().Length);
    }

    [Fact]
    public void FilesRotateBySizeKeepingThreeGenerations()
    {
        var log = Writer(maxBytes: 1000);
        // Lines of varying length (with accents: bytes, not characters, count) so no size lines up by luck.
        for (int i = 0; i < 200; i++) log.Write("INFO", "linha " + i.ToString("000") + " ação " + new string('x', i % 37));
        var files = Directory.GetFiles(_dir.Path).Select(Path.GetFileName).OrderBy(f => f).ToArray();
        Assert.Equal(new[] { "fanpilot.1.log", "fanpilot.2.log", "fanpilot.3.log", "fanpilot.log" }, files);
        Assert.All(Directory.GetFiles(_dir.Path), f => Assert.True(new FileInfo(f).Length <= 1000, f));
        Assert.Contains("linha 199", Lines().Last());
        Assert.DoesNotContain(File.ReadAllLines(_dir.File("fanpilot.1.log")), l => l.Contains("linha 199"));
    }

    // Item 14b: the tray's "Ver log" gets the end of the file, from a whole line.
    [Fact]
    public void TheTailStartsAtAWholeLine()
    {
        var log = Writer();
        Assert.Equal("", log.Tail(1024));
        for (int i = 0; i < 100; i++) { log.Write("INFO", "linha " + i); _now = _now.AddSeconds(1); }
        string all = File.ReadAllText(_dir.File("fanpilot.log"));
        Assert.Equal(all, log.Tail(1024 * 1024));
        string tail = log.Tail(200);
        Assert.True(tail.Length <= 200, tail);
        Assert.StartsWith("2026-10-24 ", tail);
        Assert.EndsWith("[INFO] linha 99" + Environment.NewLine, tail);
    }
}
