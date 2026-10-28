// Item 23, part 2: FanPilot on a real MQTT broker (MQTTnet's own server, in the test, on a free port): the discovery
// Home Assistant reads, the state, online and offline, the last will, and the settings the panel saves.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Protocol;
using MQTTnet.Server;
using Xunit;

public sealed class MqttTests : IDisposable
{
    sealed class Received { public string Topic, Payload; public bool Retain; }

    readonly TempDir _dir = new TempDir();
    readonly List<Received> _received = new List<Received>();
    readonly List<MqttServer> _brokers = new List<MqttServer>();
    readonly List<MqttPublisher> _publishers = new List<MqttPublisher>();
    Dictionary<string, object> _readings = IntegrationFeed.Build(IntegrationTests.Snapshot(), IntegrationTests.Fans(), "Test board", "B760M", "PC-Test");

    public void Dispose()
    {
        foreach (var p in _publishers) p.Stop();
        foreach (var b in _brokers) { b.StopAsync().GetAwaiter().GetResult(); b.Dispose(); }
        _dir.Dispose();
    }

    // A broker on a free port that keeps what it receives; with a user, only that user and password get in.
    int Broker(string user = null, string password = null)
    {
        int port = TestEnv.FreePort();
        var factory = new MqttFactory();
        // On loopback only: no firewall prompt for the test runner.
        var server = factory.CreateMqttServer(factory.CreateServerOptionsBuilder().WithDefaultEndpoint().WithDefaultEndpointPort(port)
            .WithDefaultEndpointBoundIPAddress(System.Net.IPAddress.Loopback).WithDefaultEndpointBoundIPV6Address(System.Net.IPAddress.IPv6Loopback).Build());
        server.ValidatingConnectionAsync += e =>
        {
            if (user != null && (e.UserName != user || e.Password != password)) e.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
            return Task.CompletedTask;
        };
        server.InterceptingPublishAsync += e =>
        {
            lock (_received) _received.Add(new Received { Topic = e.ApplicationMessage.Topic, Payload = e.ApplicationMessage.ConvertPayloadToString(), Retain = e.ApplicationMessage.Retain });
            return Task.CompletedTask;
        };
        server.StartAsync().GetAwaiter().GetResult();
        _brokers.Add(server);
        return port;
    }

    MqttPublisher Publisher()
    {
        var p = new MqttPublisher(_dir.Path, () => _readings, "PC-Test");
        _publishers.Add(p);
        p.Start();
        return p;
    }

    static Dictionary<string, object> On(int port, string user = null, string password = null)
    {
        var body = new Dictionary<string, object> { { "enabled", true }, { "host", "127.0.0.1" }, { "port", port }, { "language", "en" } };
        if (user != null) { body["username"] = user; body["password"] = password; }
        return body;
    }

    Received Last(string topic) { lock (_received) return _received.LastOrDefault(m => m.Topic == topic); }

    // The broker drops its one client, as a network failure would; returns the client's id.
    static string Kick(MqttServer broker)
    {
        string id = broker.GetClientsAsync().GetAwaiter().GetResult().Single().Id;
        broker.DisconnectClientAsync(id, MqttDisconnectReasonCode.AdministrativeAction).GetAwaiter().GetResult();
        return id;
    }

    // What the broker keeps for a topic now (the retained message), or null.
    static string Retained(MqttServer broker, string topic)
    {
        var kept = broker.GetRetainedMessagesAsync().GetAwaiter().GetResult().FirstOrDefault(m => m.Topic == topic);
        return kept == null ? null : kept.ConvertPayloadToString();
    }

    int Count(string topic) { lock (_received) return _received.Count(m => m.Topic == topic); }

    static void WaitFor(Func<bool> condition, string what, int seconds = 15)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(seconds)) Assert.Fail("esperando: " + what);
            Thread.Sleep(50);
        }
    }

    static string State(MqttPublisher p) { return (string)((Dictionary<string, object>)p.Describe()["status"])["state"]; }

    static Dictionary<string, object> Error(MqttPublisher p)
    {
        return ((Dictionary<string, object>)p.Describe()["status"])["error"] as Dictionary<string, object>;
    }

    const string Config = "homeassistant/sensor/fanpilot_pc-test_";

    [Fact]
    public void TheEntitiesShowUpInHomeAssistantByThemselves()
    {
        int port = Broker();
        var p = Publisher();
        p.Update(On(port));
        WaitFor(() => Last("fanpilot/pc-test/state") != null, "o estado");

        var online = Last("fanpilot/pc-test/availability");
        Assert.Equal("online", online.Payload);
        Assert.True(online.Retain);
        var cpu = Json.ParseObject(Last(Config + "cpu_temperature/config").Payload);
        Assert.True(Last(Config + "cpu_temperature/config").Retain);
        Assert.Equal("CPU temperature", cpu["name"]);
        Assert.Equal("fanpilot_pc-test_cpu_temperature", cpu["unique_id"]);
        Assert.Equal("fanpilot/pc-test/state", cpu["state_topic"]);
        Assert.Equal("{{ value_json.cpu.temperature }}", cpu["value_template"]);
        Assert.Equal("°C", cpu["unit_of_measurement"]);
        Assert.Equal("temperature", cpu["device_class"]);
        Assert.Equal("measurement", cpu["state_class"]);
        Assert.Equal("fanpilot/pc-test/availability", cpu["availability_topic"]);
        var device = (Dictionary<string, object>)cpu["device"];
        Assert.Equal(new object[] { "fanpilot_pc-test" }, (object[])device["identifiers"]);
        Assert.Equal("PC-Test", device["name"]);
        Assert.Equal("B760M", device["model"]);
        // A fan by its own name, the user's one when there is one (item 17), and the board as a problem sensor.
        Assert.Equal("System 1 fan", Json.ParseObject(Last(Config + "fan0_rpm/config").Payload)["name"]);
        Assert.Equal("AIO \\ front fan · requested speed", Json.ParseObject(Last(Config + "fan1_duty/config").Payload)["name"]);
        Assert.Equal("{{ value_json.fans[1].rpm }}", Json.ParseObject(Last(Config + "fan1_rpm/config").Payload)["value_template"]);
        var board = Json.ParseObject(Last("homeassistant/binary_sensor/fanpilot_pc-test_board_problem/config").Payload);
        Assert.Equal("problem", board["device_class"]);
        Assert.NotNull(Last(Config + "gpu_temperature/config"));
        // The state is /api/v1/sensors' JSON, not kept by the broker.
        var state = Last("fanpilot/pc-test/state");
        Assert.False(state.Retain);
        Assert.Equal(1, Json.ParseObject(state.Payload)["version"]);
        WaitFor(() => State(p) == "connected", "conectado");
        // CPU (2), alerts, board, GPU (4), two fans (2 each).
        Assert.Equal(12, ((Dictionary<string, object>)p.Describe()["status"])["entities"]);
        // The last will: when FanPilot's connection drops (the broker cuts it here), the broker itself says offline...
        var broker = _brokers.Single();
        Assert.Equal("online", Retained(broker, "fanpilot/pc-test/availability"));
        Assert.Equal("fanpilot_pc-test", Kick(broker));
        WaitFor(() => Retained(broker, "fanpilot/pc-test/availability") == "offline", "a última vontade");
        // ...and FanPilot comes back on its own, 5 s later.
        WaitFor(() => Retained(broker, "fanpilot/pc-test/availability") == "online", "de volta", 20);

        // Stopping says offline itself.
        p.Stop();
        Assert.Equal("offline", Retained(broker, "fanpilot/pc-test/availability"));
    }

    // Every 5 s the state; the discovery only again when something in it changes (a fan renamed in the panel).
    [Fact]
    public void TheDiscoveryGoesAgainOnlyWhenItChanges()
    {
        int port = Broker();
        var p = Publisher();
        p.Update(On(port));
        WaitFor(() => Count("fanpilot/pc-test/state") >= 2, "o segundo estado", 20);
        Assert.Equal(1, Count(Config + "fan0_rpm/config"));
        var fans = IntegrationTests.Fans();
        fans[0]["label"] = "Front";
        _readings = IntegrationFeed.Build(IntegrationTests.Snapshot(), fans, "Test board", "B760M", "PC-Test");
        WaitFor(() => Count(Config + "fan0_rpm/config") == 2, "a descoberta de novo", 20);
        Assert.Equal("Front fan", Json.ParseObject(Last(Config + "fan0_rpm/config").Payload)["name"]);
        Assert.Equal(1, Count(Config + "cpu_temperature/config"));
    }

    [Fact]
    public void AWrongPasswordIsSaidAsSuch()
    {
        int port = Broker("ha", "certa");
        var p = Publisher();
        p.Update(On(port, "ha", "errada"));
        WaitFor(() => State(p) == "error", "o erro");
        Assert.Equal("mqtt.error.auth", Error(p)["code"]);
        Assert.Null(Last("fanpilot/pc-test/state"));
        // The right one, and it connects right away (no waiting for the next retry).
        p.Update(new Dictionary<string, object> { { "password", "certa" } });
        WaitFor(() => State(p) == "connected", "conectado");
        Assert.NotNull(Last("fanpilot/pc-test/state"));
    }

    [Fact]
    public void NoBrokerThereSaysWhere()
    {
        var p = Publisher();
        p.Update(On(TestEnv.FreePort()));
        WaitFor(() => State(p) == "error", "o erro");
        var error = Error(p);
        Assert.Equal("mqtt.error.connect", error["code"]);
        Assert.Contains("127.0.0.1:", (string)error["text"]);
        // Off: nothing more is tried, nothing to report.
        p.Update(new Dictionary<string, object> { { "enabled", false } });
        Assert.Equal("off", State(p));
        Assert.Null(Error(p));
    }

    [Fact]
    public void TheSettingsAreCheckedAndThePasswordStaysOnThisPc()
    {
        var s = new MqttSettings();
        Func<Dictionary<string, object>, string> refused = body => Assert.Throws<UserError>(() => s.With(body)).Msg.Key;
        Assert.Equal("err.mqtt.host", refused(new Dictionary<string, object> { { "enabled", true } }));
        Assert.Equal("err.mqtt.host", refused(new Dictionary<string, object> { { "host", "http://broker" } }));
        Assert.Equal("err.mqtt.port", refused(new Dictionary<string, object> { { "host", "broker" }, { "port", 0 } }));
        Assert.Equal("err.mqtt.prefix", refused(new Dictionary<string, object> { { "host", "broker" }, { "discoveryPrefix", "home assistant" } }));
        Assert.Equal("err.mqtt.user", refused(new Dictionary<string, object> { { "host", "broker" }, { "username", new string('u', 101) } }));

        string path = _dir.File("mqtt.json");
        var saved = s.With(new Dictionary<string, object> { { "enabled", true }, { "host", " homeassistant.local " }, { "username", "ha" }, { "password", "segredo" } });
        Assert.Equal("homeassistant.local", saved.Host);
        saved.Save(path);
        string file = File.ReadAllText(path);
        Assert.DoesNotContain("segredo", file);
        Assert.Contains("\"password\":\"" + Secrets.Prefix, file);
        var loaded = MqttSettings.Load(path);
        Assert.Equal("segredo", loaded.Password);
        Assert.Equal(true, loaded.Describe()["hasPassword"]);
        Assert.False(loaded.Describe().ContainsKey("password"));
        // Left out, the password stays; "" removes it.
        Assert.Equal("segredo", loaded.With(new Dictionary<string, object> { { "port", 1884 } }).Password);
        Assert.Null(loaded.With(new Dictionary<string, object> { { "password", "" } }).Password);
    }

    [Theory]
    [InlineData("PC-CONRAD", "pc-conrad")]
    [InlineData("Escritório 2", "escrit_rio_2")]
    [InlineData("", "pc")]
    public void TheTopicsTakeThePcsNameSafely(string pc, string node)
    {
        Assert.Equal(node, MqttPublisher.Node(pc));
    }
}
