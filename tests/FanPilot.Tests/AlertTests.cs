// Item 16: the alert rules fed one second at a time, the notification limits, the settings on disk and what goes
// out to ntfy and Telegram (against a local server).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Xunit;

public class AlertRulesTests
{
    static readonly DateTime T0 = new DateTime(2026, 10, 25, 12, 0, 0);

    // Runs the rules for `seconds` seconds from `from`, one input a second; returns every change.
    static List<Alert> Run(AlertRules rules, ref DateTime from, int seconds, Func<DateTime, AlertInput> input)
    {
        var changes = new List<Alert>();
        for (int i = 0; i < seconds; i++)
        {
            var x = input(from);
            x.Now = from;
            changes.AddRange(rules.Step(x));
            from = from.AddSeconds(1);
        }
        return changes;
    }

    static AlertInput Fan(double rpm, double? duty = 50, bool pump = false, string name = "System 2")
    {
        return new AlertInput { Fans = { new FanReading { Key = "fake/2", Name = name, Rpm = rpm, Duty = duty, IsPump = pump } } };
    }

    [Fact]
    public void AFanThatStopsIsAnAlertAfterFiveSecondsAndEndsWhenItSpinsAgain()
    {
        var rules = new AlertRules();
        var t = T0;
        Assert.Empty(Run(rules, ref t, 40, _ => Fan(1680)));
        Assert.Empty(Run(rules, ref t, 4, _ => Fan(0)));
        var raised = Run(rules, ref t, 2, _ => Fan(0));
        var a = Assert.Single(raised);
        Assert.Equal("warning", a.Severity);
        Assert.Equal("Fan parado: System 2", a.Title);
        Assert.Single(rules.Active);
        var resolved = Run(rules, ref t, 1, _ => Fan(1500));
        Assert.NotNull(Assert.Single(resolved).Resolved);
        Assert.Empty(rules.Active);
        Assert.Single(rules.Recent);
    }

    [Fact]
    public void AStoppedPumpIsCritical()
    {
        var rules = new AlertRules();
        var t = T0;
        Run(rules, ref t, 40, _ => Fan(2400, 100, true, "Bomba"));
        var a = Assert.Single(Run(rules, ref t, 6, _ => Fan(0, 100, true, "Bomba")));
        Assert.Equal("critical", a.Severity);
        Assert.Equal("Bomba parada: Bomba", a.Title);
    }

    [Fact]
    public void AHeaderWithoutATachometerNeverAlerts()
    {
        var rules = new AlertRules();
        var t = T0;
        Assert.Empty(Run(rules, ref t, 300, _ => Fan(0, 100)));
    }

    [Theory]
    [InlineData(null)]  // left to the BIOS: nobody knows what it was told
    [InlineData(20.0)]  // told to go slow
    public void ASlowOrBiosFanDoesNotAlert(double? duty)
    {
        var rules = new AlertRules();
        var t = T0;
        Run(rules, ref t, 40, _ => Fan(1680, 50));
        Assert.Empty(Run(rules, ref t, 30, _ => Fan(0, duty)));
    }

    [Fact]
    public void AFanThatOnlyBrieflySpunDoesNotCount()
    {
        var rules = new AlertRules();
        var t = T0;
        Run(rules, ref t, 20, _ => Fan(1680));
        Assert.Empty(Run(rules, ref t, 30, _ => Fan(0)));
    }

    [Fact]
    public void ACriticalTemperatureNeedsTenSecondsAndEndsOnlyFiveDegreesBelowForThirtySeconds()
    {
        var rules = new AlertRules();
        var t = T0;
        Func<double, Func<DateTime, AlertInput>> cpu = c => _ => new AlertInput { CpuTemp = c };
        Assert.Empty(Run(rules, ref t, 9, cpu(97)));
        var a = Assert.Single(Run(rules, ref t, 2, cpu(97)));
        Assert.Equal("critical", a.Severity);
        Assert.Equal("CPU a 97 °C", a.Title);
        Assert.Empty(Run(rules, ref t, 120, cpu(92))); // below the limit, not 5 °C below: still on
        Assert.Empty(Run(rules, ref t, 29, cpu(89)));
        Assert.NotNull(Assert.Single(Run(rules, ref t, 2, cpu(89))).Resolved);
    }

    [Fact]
    public void TheLimitsCanBeChanged()
    {
        var rules = new AlertRules { GpuLimit = 80 };
        var t = T0;
        Assert.Single(Run(rules, ref t, 12, _ => new AlertInput { GpuTemp = 81 }));
    }

    [Fact]
    public void ABoardThatStopsAnsweringIsAnAlertAfterAMinute()
    {
        var rules = new AlertRules();
        var t = T0;
        Assert.Empty(Run(rules, ref t, 59, _ => new AlertInput { BoardOk = false }));
        Assert.Equal("Sensores da placa indisponíveis", Assert.Single(Run(rules, ref t, 2, _ => new AlertInput { BoardOk = false })).Title);
        Assert.NotNull(Assert.Single(Run(rules, ref t, 1, _ => new AlertInput())).Resolved);
    }

    [Fact]
    public void WhatTheBoardGaveBackToTheBiosIsAnAlertWhileItLasts()
    {
        var rules = new AlertRules();
        var t = T0;
        var problem = new List<string> { "System 2: sem leitura válida do sensor de referência; com a BIOS até ela voltar." };
        var a = Assert.Single(Run(rules, ref t, 1, _ => new AlertInput { ControlProblems = problem }));
        Assert.Equal(problem[0], a.Detail);
        Assert.Empty(Run(rules, ref t, 10, _ => new AlertInput { ControlProblems = problem }));
        Assert.NotNull(Assert.Single(Run(rules, ref t, 1, _ => new AlertInput())).Resolved);
    }

    [Fact]
    public void AnAcknowledgedAlertStaysActive()
    {
        var rules = new AlertRules();
        var t = T0;
        var a = Run(rules, ref t, 11, _ => new AlertInput { CpuTemp = 99 }).Single();
        Assert.True(rules.Acknowledge(a.Id));
        Assert.True(rules.Active.Single().Acknowledged);
        Assert.False(rules.Acknowledge("nada"));
    }
}

public sealed class AlertMonitorTests : IDisposable
{
    readonly TempDir _dir = new TempDir();
    DateTime _now = new DateTime(2026, 10, 25, 12, 0, 0);
    readonly List<string> _sent = new List<string>();

    public void Dispose() { _dir.Dispose(); }

    AlertMonitor Monitor()
    {
        var board = new FakeBoard(4);
        var fans = new FanManager(board, new AppConfig(), _dir.Path);
        return new AlertMonitor(new Sampler(board, new FakeGpu(83)), fans, board, _dir.Path, () => _now,
                                (s, title, detail, critical) => _sent.Add(title));
    }

    void Tick(AlertMonitor m, int seconds, double cpu)
    {
        for (int i = 0; i < seconds; i++)
        {
            m.Tick(new AlertInput { Now = _now, CpuTemp = cpu });
            _now = _now.AddSeconds(1);
        }
    }

    [Fact]
    public void WithNoDestinationNothingIsSent()
    {
        var m = Monitor();
        Tick(m, 20, 99);
        Assert.Single((List<Dictionary<string, object>>)m.Describe()["active"]);
        Assert.Empty(_sent);
    }

    [Fact]
    public void AnOscillatingTemperatureSendsAtMostOneAlertEveryTenMinutes()
    {
        var m = Monitor();
        m.UpdateSettings(new Dictionary<string, object> { { "ntfyUrl", "https://ntfy.example/fanpilot-teste" } });
        // 30 minutes of 20 s above the limit, then 40 s well below it: the alert starts and ends every minute.
        for (int minute = 0; minute < 30; minute++)
        {
            Tick(m, 20, 97);
            Tick(m, 40, 85);
        }
        int alerts = _sent.Count(s => !s.StartsWith("Resolvido"));
        Assert.InRange(alerts, 1, 3);
        Assert.All(_sent.Where(s => s.StartsWith("Resolvido")), s => Assert.Equal("Resolvido: CPU a 97 °C", s));
        Assert.True(_sent.Count(s => s.StartsWith("Resolvido")) <= alerts);
    }
}

public sealed class AlertSettingsTests : IDisposable
{
    readonly TempDir _dir = new TempDir();

    public void Dispose() { _dir.Dispose(); }

    [Fact]
    public void SecretsAreEncryptedOnDiskAndNeverShownBack()
    {
        var s = new AlertSettings().With(new Dictionary<string, object> {
            { "ntfyUrl", "https://ntfy.sh/fanpilot-segredo1234" }, { "telegramToken", "123456:ABCDEF-token" }, { "telegramChatId", "42" } });
        s.Save(_dir.File("alerts.json"));
        string onDisk = File.ReadAllText(_dir.File("alerts.json"));
        Assert.DoesNotContain("segredo1234", onDisk);
        Assert.DoesNotContain("ABCDEF-token", onDisk);
        var loaded = AlertSettings.Load(_dir.File("alerts.json"));
        Assert.Equal("https://ntfy.sh/fanpilot-segredo1234", loaded.NtfyUrl);
        Assert.Equal("123456:ABCDEF-token", loaded.TelegramToken);
        var shown = Json.Serialize(loaded.Describe());
        Assert.DoesNotContain("segredo", shown);
        Assert.Contains("…1234", shown);
    }

    [Fact]
    public void AnEmptyFieldRemovesADestinationAndAMissingOneKeepsIt()
    {
        var s = new AlertSettings().With(new Dictionary<string, object> { { "ntfyUrl", "https://ntfy.sh/fanpilot-abc" } });
        Assert.Equal("https://ntfy.sh/fanpilot-abc", s.With(new Dictionary<string, object> { { "cpuTemp", 90 } }).NtfyUrl);
        Assert.Null(s.With(new Dictionary<string, object> { { "ntfyUrl", "" } }).NtfyUrl);
        Assert.False(new AlertSettings().HasDestination);
    }

    [Theory]
    [InlineData("cpuTemp", 50)]
    [InlineData("gpuTemp", 120)]
    [InlineData("ntfyUrl", "ntfy.sh/sem-esquema")]
    [InlineData("ntfyUrl", "https://ntfy.sh/")]
    [InlineData("telegramToken", "123:abc")] // without a chat id
    [InlineData("pcName", "")]
    public void InvalidSettingsAreRefused(string key, object value)
    {
        Assert.Throws<ArgumentException>(() => new AlertSettings().With(new Dictionary<string, object> { { key, value } }));
    }
}

public sealed class AlertSenderTests
{
    // One request to a local server: what ntfy or Telegram would have received.
    static (string Url, string Body, string ContentType) Capture(Action<int> send)
    {
        int port = TestEnv.FreePort();
        var listener = new HttpListener();
        listener.Prefixes.Add("http://localhost:" + port + "/");
        listener.Start();
        try
        {
            var got = Task.Run(() =>
            {
                var ctx = listener.GetContext();
                string body = new StreamReader(ctx.Request.InputStream, Encoding.UTF8).ReadToEnd();
                var result = (ctx.Request.RawUrl, body, ctx.Request.ContentType);
                ctx.Response.StatusCode = 200;
                ctx.Response.Close();
                return result;
            });
            send(port);
            Assert.True(got.Wait(10000), "nada chegou ao servidor local");
            return got.Result;
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public void NtfyGetsTheTitleThePriorityAndTheDetail()
    {
        var r = Capture(port =>
        {
            var s = new AlertSettings { PcName = "PC da sala", NtfyUrl = "http://localhost:" + port + "/fanpilot-teste" };
            Assert.Empty(AlertSender.Send(s, "Bomba parada: SYS_FAN5_PUMP", "Lê 0 RPM com 100% comandado.", true));
        });
        Assert.StartsWith("/fanpilot-teste?title=", r.Url);
        Assert.Contains(Uri.EscapeDataString("PC da sala: Bomba parada: SYS_FAN5_PUMP"), r.Url);
        Assert.Contains("priority=high", r.Url);
        Assert.Equal("Lê 0 RPM com 100% comandado.", r.Body);
    }

    [Fact]
    public void TelegramGetsTheChatAndTheText()
    {
        string api = AlertSender.TelegramApi;
        try
        {
            var r = Capture(port =>
            {
                AlertSender.TelegramApi = "http://localhost:" + port;
                var s = new AlertSettings { PcName = "PC", TelegramToken = "123:abc", TelegramChatId = "42" };
                Assert.Empty(AlertSender.Send(s, "CPU a 97 °C", "Acima do limite.", true));
            });
            Assert.Equal("/bot123:abc/sendMessage", r.Url);
            var json = Json.ParseObject(r.Body);
            Assert.Equal("42", Json.GetString(json, "chat_id", null));
            Assert.Equal("PC: CPU a 97 °C\nAcima do limite.", Json.GetString(json, "text", null));
        }
        finally { AlertSender.TelegramApi = api; }
    }

    [Fact]
    public void AnUnreachableDestinationIsReportedNotThrown()
    {
        var s = new AlertSettings { NtfyUrl = "http://localhost:" + TestEnv.FreePort() + "/fanpilot-teste" };
        var errors = AlertSender.Send(s, "Teste", "Teste", false);
        Assert.StartsWith("ntfy: ", Assert.Single(errors));
    }
}
