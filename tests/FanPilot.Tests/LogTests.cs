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
        Assert.Contains("se repetiu", lines[1]);
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
        Assert.Contains("se repetiu 2 vezes", lines[1]);
        Assert.EndsWith("[INFO] B", lines[2]);
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
}
