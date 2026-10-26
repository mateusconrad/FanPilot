// Item 23: the integration tokens, and the readings Prometheus (/metrics) and Home Assistant (/api/v1/sensors) get.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

public sealed class IntegrationTests : IDisposable
{
    readonly TempDir _dir = new TempDir();
    DateTime _now = new DateTime(2026, 10, 25, 18, 0, 0, DateTimeKind.Utc);

    public void Dispose() { _dir.Dispose(); }

    IntegrationTokens Tokens() { return new IntegrationTokens(_dir.Path, () => _now); }

    [Fact]
    public void ATokenIsShownOnceAndKeptOnlyAsItsHash()
    {
        var tokens = Tokens();
        var made = tokens.Create("  Prometheus ");
        string token = (string)made["token"];
        Assert.StartsWith("fp_", token);
        Assert.True(token.Length >= 40, token);
        Assert.Equal("Prometheus", made["name"]);
        Assert.Equal(made["id"], tokens.Validate(token).Id);
        // On disk, the SHA-256 only; in the list, not even that.
        string file = File.ReadAllText(_dir.File("integrations.json"));
        Assert.DoesNotContain(token.Substring(3), file);
        Assert.Contains(AuthManager.Hash(token), file);
        Assert.False(tokens.List().Single().ContainsKey("token"));
        Assert.False(tokens.List().Single().ContainsKey("hash"));
        Assert.Null(tokens.Validate(token + "x"));
        Assert.Null(tokens.Validate(token.Substring(3))); // without the prefix
        Assert.Null(tokens.Validate(null));
    }

    [Fact]
    public void ARevokedTokenStopsWorking()
    {
        var tokens = Tokens();
        string a = (string)tokens.Create("Grafana")["token"];
        var b = tokens.Create("Home Assistant");
        Assert.True(tokens.Revoke((string)b["id"]));
        Assert.Null(tokens.Validate((string)b["token"]));
        Assert.NotNull(tokens.Validate(a));
        Assert.False(tokens.Revoke((string)b["id"]));
        Assert.False(tokens.Revoke("nope"));
        // And after a restart too.
        Assert.Null(Tokens().Validate((string)b["token"]));
        Assert.Equal(new[] { "Grafana" }, Tokens().List().Select(t => (string)t["name"]));
    }

    // The last use goes to disk every few minutes, not at every scrape.
    [Fact]
    public void TheLastUseIsKept()
    {
        var tokens = Tokens();
        string token = (string)tokens.Create("Prometheus")["token"];
        Assert.Null(Tokens().List().Single()["lastUsed"]);
        _now = _now.AddMinutes(1);
        tokens.Validate(token);
        Assert.Equal(_now.ToString("o"), tokens.List().Single()["lastUsed"]);
        _now = _now.AddMinutes(10);
        tokens.Validate(token);
        Assert.Equal(_now.ToString("o"), Tokens().List().Single()["lastUsed"]);
    }

    [Fact]
    public void NamesAndHowManyAreChecked()
    {
        var tokens = Tokens();
        Assert.Equal("err.token.name", Assert.Throws<UserError>(() => tokens.Create(" ")).Msg.Key);
        Assert.Equal("err.token.name", Assert.Throws<UserError>(() => tokens.Create(new string('x', 41))).Msg.Key);
        Assert.Equal("err.token.name", Assert.Throws<UserError>(() => tokens.Create("a\nb")).Msg.Key);
        for (int i = 0; i < IntegrationTokens.MaxTokens; i++) tokens.Create("t" + i);
        Assert.Equal("err.token.tooMany", Assert.Throws<UserError>(() => tokens.Create("one more")).Msg.Key);
    }

    [Theory]
    [InlineData("Bearer fp_abc", "fp_abc")]
    [InlineData("bearer   fp_abc  ", "fp_abc")]
    [InlineData("Basic Zm9vOmJhcg==", null)]
    [InlineData("Bearer ", null)]
    [InlineData(null, null)]
    public void TheTokenComesFromTheAuthorizationHeader(string header, string token)
    {
        Assert.Equal(token, IntegrationTokens.FromHeader(header));
    }

    // A snapshot as Sampler.Sample makes it, and the fans as FanManager.Describe lists them.
    internal static Dictionary<string, object> Snapshot()
    {
        Func<string, string, string, double, string, Dictionary<string, object>> sensor = (id, type, title, value, role) =>
        {
            var d = new Dictionary<string, object> { { "id", id }, { "type", type }, { "title", title }, { "value", value }, { "unit", "" } };
            if (role != null) d["role"] = role;
            return d;
        };
        return new Dictionary<string, object> {
            { "t", 1761415200000L }, // 2025-10-25 18:00 UTC
            { "service", new Dictionary<string, object> { { "ok", true } } },
            { "sensors", new List<Dictionary<string, object>> {
                sensor("temperature-cpu", "Temperature", "CPU · package", 62.44, CpuRoles.Main),
                sensor("temperature-0", "Temperature", "System \"1\"", 38.0, null),
                sensor("power-cpu", "Power", "CPU Package", 41.26, null),
                sensor("voltage-0", "Voltage", "Vcore", 1.2, null),
            } },
            { "fanRpm", new List<int> { 1200, 0 } },
            { "tempInputs", new List<double> { 38.0, 62.4 } },
            { "gpu", new Dictionary<string, object> {
                { "ok", true }, { "name", "Test GPU" }, { "temp", 58 }, { "power", 96.3 }, { "util", 34 }, { "fans", new List<int> { 42 } } } },
            { "alerts", new List<Dictionary<string, object>> {
                new Dictionary<string, object> { { "id", "fan-stopped:fake/1" }, { "severity", "critical" },
                    { "title", new Dictionary<string, object> { { "code", "alert.fanStopped.title" }, { "text", "Pump stopped" } } } } } },
        };
    }

    internal static List<Dictionary<string, object>> Fans()
    {
        var curve = new FanCurve { Index = 0, Key = "fake/0", Title = "System 1", TempInput = 0, Mode = "SmartFanMode", DeltaT = 1 };
        curve.Points.AddRange(new[] { new[] { 30.0, 76.5 }, new[] { 60.0, 153 }, new[] { 90.0, 255 } });
        var fan = curve.ToDictionary();
        fan["label"] = null; fan["role"] = "fan";
        var pump = new FanCurve { Index = 1, Key = "fake/1", Title = "Pump", TempInput = 1, Mode = FanCurve.BiosMode, DeltaT = 1 }.ToDictionary();
        pump["label"] = "AIO \\ front"; pump["role"] = "pump";
        return new List<Dictionary<string, object>> { fan, pump };
    }

    [Fact]
    public void TheStableJsonHasTheSnapshotsReadings()
    {
        var v1 = IntegrationFeed.Build(Snapshot(), Fans(), "Test board", "B760M", "PC");
        Assert.Equal(1, v1["version"]);
        Assert.Equal("2025-10-25T18:00:00Z", v1["time"]);
        Assert.Equal("PC", v1["pc"]);
        var cpu = (Dictionary<string, object>)v1["cpu"];
        Assert.Equal(62.4, cpu["temperature"]);
        Assert.Equal(41.3, cpu["power"]);
        var gpu = (Dictionary<string, object>)v1["gpu"];
        Assert.Equal(58.0, gpu["temperature"]);
        Assert.Equal(42, gpu["fan"]);
        var fans = ((List<object>)v1["fans"]).Cast<Dictionary<string, object>>().ToList();
        // 38 °C on a curve from 30% at 30 °C to 60% at 60 °C.
        Assert.Equal(38.0, fans[0]["duty"]);
        Assert.Equal(1200, fans[0]["rpm"]);
        Assert.Null(fans[0]["label"]);
        Assert.Null(fans[1]["duty"]); // the BIOS runs it
        Assert.Equal(true, fans[1]["pump"]);
        Assert.Equal("AIO \\ front", fans[1]["label"]);
        Assert.Equal(new[] { "temperature-cpu", "temperature-0" }, ((List<object>)v1["temperatures"]).Cast<Dictionary<string, object>>().Select(s => s["id"]));
        Assert.Equal("Pump stopped", ((Dictionary<string, object>)((List<object>)v1["alerts"]).Single())["title"]);
    }

    [Fact]
    public void MetricsFollowThePrometheusTextFormat()
    {
        string text = IntegrationFeed.Prometheus(IntegrationFeed.Build(Snapshot(), Fans(), "Test board", "B760M \"X\"", "PC"));
        string expected = string.Join("\n",
            "# HELP fanpilot_up 1 while FanPilot reads the motherboard's sensors.",
            "# TYPE fanpilot_up gauge",
            "fanpilot_up 1",
            "# HELP fanpilot_info FanPilot's version, the motherboard and the way FanPilot reaches it.",
            "# TYPE fanpilot_info gauge",
            "fanpilot_info{version=\"" + AppVersion.Text + "\",board=\"B760M \\\"X\\\"\",backend=\"Test board\",pc=\"PC\"} 1",
            "# HELP fanpilot_cpu_temperature_celsius The CPU temperature the panel shows (the cores when there is a reading of them).",
            "# TYPE fanpilot_cpu_temperature_celsius gauge",
            "fanpilot_cpu_temperature_celsius 62.4",
            "# HELP fanpilot_cpu_power_watts The CPU package power.",
            "# TYPE fanpilot_cpu_power_watts gauge",
            "fanpilot_cpu_power_watts 41.3",
            "# HELP fanpilot_temperature_celsius Every temperature sensor FanPilot reads.",
            "# TYPE fanpilot_temperature_celsius gauge",
            "fanpilot_temperature_celsius{sensor=\"temperature-cpu\",name=\"CPU · package\"} 62.4",
            "fanpilot_temperature_celsius{sensor=\"temperature-0\",name=\"System \\\"1\\\"\"} 38",
            "# HELP fanpilot_power_watts Every power reading FanPilot has.",
            "# TYPE fanpilot_power_watts gauge",
            "fanpilot_power_watts{sensor=\"power-cpu\",name=\"CPU Package\"} 41.3",
            "# HELP fanpilot_fan_rpm Motherboard fan speed, from its tachometer.",
            "# TYPE fanpilot_fan_rpm gauge",
            "fanpilot_fan_rpm{fan=\"System 1\",name=\"System 1\"} 1200",
            "fanpilot_fan_rpm{fan=\"Pump\",name=\"AIO \\\\ front\"} 0",
            "# HELP fanpilot_fan_duty_percent The speed the fan's curve asks for at the current temperature; absent while the BIOS runs the fan.",
            "# TYPE fanpilot_fan_duty_percent gauge",
            "fanpilot_fan_duty_percent{fan=\"System 1\",name=\"System 1\"} 38",
            "# HELP fanpilot_gpu_temperature_celsius GPU temperature.",
            "# TYPE fanpilot_gpu_temperature_celsius gauge",
            "fanpilot_gpu_temperature_celsius{gpu=\"Test GPU\"} 58",
            "# HELP fanpilot_gpu_power_watts GPU power.",
            "# TYPE fanpilot_gpu_power_watts gauge",
            "fanpilot_gpu_power_watts{gpu=\"Test GPU\"} 96.3",
            "# HELP fanpilot_gpu_utilization_percent GPU utilization.",
            "# TYPE fanpilot_gpu_utilization_percent gauge",
            "fanpilot_gpu_utilization_percent{gpu=\"Test GPU\"} 34",
            "# HELP fanpilot_gpu_fan_percent GPU fan speed.",
            "# TYPE fanpilot_gpu_fan_percent gauge",
            "fanpilot_gpu_fan_percent{gpu=\"Test GPU\"} 42",
            "# HELP fanpilot_alerts_active Alerts active now, by severity.",
            "# TYPE fanpilot_alerts_active gauge",
            "fanpilot_alerts_active{severity=\"critical\"} 1",
            "fanpilot_alerts_active{severity=\"warning\"} 0",
            "");
        Assert.Equal(expected, text);
    }

    // Before the first snapshot (the board still opening): down, and nothing made up.
    [Fact]
    public void BeforeTheFirstReadingFanPilotIsDown()
    {
        var v1 = IntegrationFeed.Build(null, null, "Test board", "B760M", "PC");
        Assert.Equal(false, ((Dictionary<string, object>)v1["board"])["ok"]);
        Assert.Null(v1["time"]);
        Assert.Null(v1["gpu"]);
        Assert.Empty((List<object>)v1["fans"]);
        string text = IntegrationFeed.Prometheus(v1);
        Assert.Contains("\nfanpilot_up 0\n", "\n" + text);
        Assert.DoesNotContain("fanpilot_cpu_temperature_celsius", text);
        Assert.DoesNotContain("fanpilot_gpu", text);
    }

    // The board stopped answering: down, with the last GPU reading still there (it doesn't depend on the board).
    [Fact]
    public void ABoardThatStoppedAnsweringIsDown()
    {
        var snap = Snapshot();
        snap["service"] = new Dictionary<string, object> { { "ok", false }, { "error", "timeout" } };
        snap.Remove("sensors"); snap.Remove("fanRpm"); snap.Remove("tempInputs");
        var v1 = IntegrationFeed.Build(snap, Fans(), "Test board", "B760M", "PC");
        Assert.Equal(false, ((Dictionary<string, object>)v1["board"])["ok"]);
        var fans = ((List<object>)v1["fans"]).Cast<Dictionary<string, object>>().ToList();
        Assert.Null(fans[0]["rpm"]);
        Assert.Null(fans[0]["duty"]);
        Assert.Contains("fanpilot_up 0", IntegrationFeed.Prometheus(v1));
    }
}
