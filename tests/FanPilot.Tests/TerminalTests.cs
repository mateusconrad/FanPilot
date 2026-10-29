// Item 29: the terminal. TerminalScreen draws a fixed view as text (widths, heights, the two languages, colors);
// TerminalFeed reads a FanPilot running on the fake board through the local API, as --tui does with the service.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Xunit;

public class TerminalScreenTests
{
    static TerminalView View()
    {
        return new TerminalView {
            Url = "http://localhost:8787/", Version = "0.1.0+abc1234", Backend = "Gigabyte Control Center", Profile = "Silencioso",
            Age = TimeSpan.FromSeconds(1), OwnsFanPilot = true, HasLog = true,
            Temps = {
                new TerminalView.Temp { Title = "CPU · pacote", Value = 62.4, Limit = 95, History = Enumerable.Range(0, 600).Select(i => (double?)(50 + i % 20)).ToList() },
                new TerminalView.Temp { Title = "GPU", Value = 88, Limit = 87 },
                new TerminalView.Temp { Title = "SYSTIN", Value = null, Limit = 95 },
            },
            Fans = {
                new TerminalView.Fan { Title = "CPU", Rpm = 1240, Duty = 55.4, Estimated = true, Mode = "curva" },
                new TerminalView.Fan { Title = "Bomba do water cooler", Rpm = 2900, Duty = 100, Mode = "bomba" },
                new TerminalView.Fan { Title = "GPU", Duty = 0, Mode = "automático" },
            },
        };
    }

    static string Visible(string line) { return Regex.Replace(line, "\u001b\\]8;;[^\u001b]*\u001b\\\\|\u001b\\[[0-9;?]*[A-Za-z]", ""); }

    [Fact]
    public void TheWholeScreenFitsAndSaysWhatMatters()
    {
        var lines = TerminalScreen.Render(View(), 100, 24, I18n.Portuguese, false);
        Assert.Equal(24, lines.Count);
        Assert.All(lines, l => Assert.True(l.Length <= 100, l));
        string all = string.Join("\n", lines);
        Assert.StartsWith(" FanPilot 0.1.0+abc1234 · Gigabyte Control Center · Silencioso", lines[0]);
        Assert.Contains("http://localhost:8787/", lines[0]);
        Assert.Contains("Temperaturas", all);
        Assert.Contains("Coolers", all);
        Assert.Matches("CPU · pacote +62 °C", all);
        Assert.Contains("SYSTIN", all);
        Assert.Contains("— °C", all);
        Assert.Contains("1.240 RPM", all);
        Assert.Contains("~55 %", all);   // estimated: the curve in the chip, not a speed FanPilot set
        Assert.Contains("100 %", all);
        Assert.Contains("Bomba do water …", all); // long names cut, the columns stay put
        Assert.Contains("█", all);
        Assert.Contains("máx 10 min 69 °C", all);
        Assert.Contains("[q] fechar o FanPilot", lines[23]);
        Assert.Contains("[l] log", lines[23]);
        Assert.Contains("atualizado há 1 s · alertas: 0", lines[23]);
    }

    [Fact]
    public void InEnglishWithTheEnglishNumbers()
    {
        var view = View();
        view.OwnsFanPilot = false;
        view.HasLog = false;
        string all = string.Join("\n", TerminalScreen.Render(view, 100, 24, I18n.English, false));
        Assert.Contains("Temperatures", all);
        Assert.Contains("1,240 RPM", all);
        Assert.Contains("[q] quit", all);
        Assert.DoesNotContain("[l]", all);
        Assert.Contains("10 min max 69 °C", all);
    }

    [Fact]
    public void ANarrowTerminalKeepsTheNumbersWithoutBars()
    {
        var lines = TerminalScreen.Render(View(), 50, 24, I18n.Portuguese, false);
        Assert.All(lines, l => Assert.True(l.Length <= 50, l));
        string all = string.Join("\n", lines);
        Assert.DoesNotContain("█", all);
        Assert.DoesNotContain("░", all);
        Assert.Contains("62 °C", all);
        Assert.Contains("1.240 RPM", all);
        Assert.Contains("~55 %", all);
    }

    [Fact]
    public void AShortTerminalKeepsTheTemperaturesAndTheKeys()
    {
        var lines = TerminalScreen.Render(View(), 100, 8, I18n.Portuguese, false);
        Assert.Equal(8, lines.Count);
        string all = string.Join("\n", lines);
        Assert.Contains("62 °C", all);
        Assert.DoesNotContain("Coolers", all);
        Assert.Contains("[o]", lines[7]);
    }

    [Fact]
    public void ColorsLinkAndAlertsDoNotChangeTheWidth()
    {
        var view = View();
        view.Turbo = true;
        view.Alerts.Add("CPU a 97 °C");
        var lines = TerminalScreen.Render(view, 100, 24, I18n.Portuguese, true);
        Assert.All(lines, l => Assert.True(Visible(l).Length <= 100, Visible(l)));
        Assert.Contains("\u001b]8;;http://localhost:8787/\u001b\\", lines[0]); // the address is a link
        Assert.Contains("TURBO", Visible(lines[0]));
        Assert.Contains("\u001b[31m", lines.First(l => l.Contains("GPU") && l.Contains("88"))); // at its alert limit: red
        Assert.Contains(lines, l => Visible(l).Contains("⚠ CPU a 97 °C"));
        Assert.Contains("alertas: 1", Visible(lines[23]));
    }

    [Fact]
    public void WithoutFanPilotTheScreenSaysSo()
    {
        var view = new TerminalView { Url = "http://localhost:8787/", Problem = new Msg("tui.notRunning", "url", "http://localhost:8787/") };
        string all = string.Join("\n", TerminalScreen.Render(view, 100, 24, I18n.Portuguese, false));
        Assert.Contains("Nenhum FanPilot respondendo em http://localhost:8787/", all);
    }

    [Fact]
    public void TheSparklineDrawsTheLastTenMinutes()
    {
        Assert.Equal(new string('▁', 12), TerminalScreen.Sparkline(Enumerable.Repeat((double?)45, 600).ToList(), 12));
        string rising = TerminalScreen.Sparkline(Enumerable.Range(0, 600).Select(i => (double?)(40 + i / 10.0)).ToList(), 12);
        Assert.Equal('▁', rising[0]);
        Assert.Equal('█', rising[11]);
        Assert.Equal(' ', TerminalScreen.Sparkline(new List<double?> { null, null, 50 }, 3)[0]); // no reading then
    }

    [Fact]
    public void TheLogShowsItsLastLines()
    {
        var log = Enumerable.Range(1, 50).Select(i => "2026-10-29 10:00:" + i.ToString("00") + " [INFO] line " + i).ToList();
        log.Add("2026-10-29 10:01:00 [ERROR] Board sensors unavailable");
        var lines = TerminalScreen.RenderLog(log, 80, 10, I18n.Portuguese, true);
        Assert.Equal(10, lines.Count);
        Assert.Contains("[l] voltar", Visible(lines[0]));
        Assert.Contains("line 43", Visible(lines[1]));
        Assert.Contains("\u001b[31m", lines[9]);
    }
}

public sealed class TerminalFeedTests : IDisposable
{
    readonly TempDir _dir = new TempDir();
    FanPilotHost _host;

    public void Dispose()
    {
        if (_host != null) _host.Stop();
        _dir.Dispose();
    }

    int Started(IBoard board)
    {
        for (int attempt = 1; ; attempt++)
        {
            var cfg = new AppConfig { Port = TestEnv.FreePort() };
            var host = new FanPilotHost(cfg, Path.Combine(TestEnv.OutputDir, "wwwroot"), _dir.Path,
                () => board, () => new FakeGpu(cfg.GpuSafetyTemp), false, TestEnv.IsAdministrator);
            int code = host.Start();
            if (code == 5 && attempt < 5) continue;
            Assert.Equal(0, code);
            _host = host;
            return cfg.Port;
        }
    }

    static TerminalView WaitFor(TerminalFeed feed, Func<TerminalView, bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        TerminalView v;
        while (!ready(v = feed.View(I18n.Portuguese)) && DateTime.UtcNow < deadline) Thread.Sleep(100);
        return v;
    }

    [Fact]
    public void ReadsTheRunningFanPilotAndTogglesTurbo()
    {
        int port = Started(FakeBoard.Scenario("demo"));
        using (var feed = new TerminalFeed(port))
        {
            Assert.True(feed.Connect());
            feed.Start();
            var v = WaitFor(feed, x => x.Age.HasValue && x.Fans.Count > 0);
            Assert.Null(v.Problem);
            Assert.StartsWith("0.1.0", v.Version);
            Assert.Equal("Personalizado", v.Profile);
            Assert.True(v.Temps[0].Value.HasValue);
            Assert.Contains(v.Temps, t => t.Title == "GPU");
            var boardFans = v.Fans.Where(f => f.Title != "GPU").ToList();
            Assert.NotEmpty(boardFans);
            Assert.All(boardFans, f => Assert.True(f.Rpm.HasValue));
            // The demo keeps its curves in the chip: the speed is the curve's, estimated.
            Assert.Contains(boardFans, f => f.Duty.HasValue && f.Estimated);
            Assert.Contains(v.Fans, f => f.Title == "GPU");

            feed.ToggleTurbo();
            v = WaitFor(feed, x => x.Turbo);
            Assert.True(v.Turbo);
            Assert.All(v.Fans.Where(f => f.Title != "GPU" && f.Mode != "bomba"), f => Assert.Equal("Turbo", f.Mode));
            feed.ToggleTurbo();
            Assert.False(WaitFor(feed, x => !x.Turbo).Turbo);
        }
    }

    [Fact]
    public void TuiFindsThePortWithoutWritingAnything()
    {
        string config = Path.Combine(_dir.Path, "nowhere", "config.json");
        Assert.Equal(8787, AppConfig.ReadPort(config));
        Assert.False(Directory.Exists(Path.GetDirectoryName(config)));
        File.WriteAllText(_dir.File("config.json"), "{\"port\": 9123}");
        Assert.Equal(9123, AppConfig.ReadPort(_dir.File("config.json")));
        File.WriteAllText(_dir.File("config.json"), "{port");
        Assert.Equal(8787, AppConfig.ReadPort(_dir.File("config.json")));
    }

    [Fact]
    public void NoFanPilotIsSaidAsSuch()
    {
        using (var feed = new TerminalFeed(TestEnv.FreePort()))
        {
            Assert.False(feed.Connect());
            Assert.Equal("tui.notRunning", feed.Problem.Key);
        }
    }

    [Fact]
    public void ASnapshotWithExactSpeedsIsNotEstimated()
    {
        int port = Started(FakeBoard.Scenario("demo"));
        using (var feed = new TerminalFeed(port))
        {
            Assert.True(feed.Connect());
            feed.Take(new Dictionary<string, object> {
                { "t", 0L }, { "sensors", new List<object>() }, { "fanRpm", new List<object> { 900, 1000, 1100, 1200 } },
                { "fanDuty", new List<object> { 42.5, null, 60, 70 } }, { "fanDutyEstimated", false },
                { "gpu", new Dictionary<string, object> { { "ok", false } } } });
            var v = feed.View(I18n.Portuguese);
            Assert.Equal(42.5, v.Fans[0].Duty);
            Assert.False(v.Fans[0].Estimated);
            Assert.Null(v.Fans[1].Duty);
            Assert.DoesNotContain(v.Temps, t => t.Title == "GPU");
        }
    }
}
