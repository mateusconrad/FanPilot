// Item 10: the Gigabyte path through reflection, against stub assemblies shaped like GCC's (GccStub).
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

public sealed class GigabyteApiTests : IDisposable
{
    readonly TempDir _dir = new TempDir();

    public void Dispose() { _dir.Dispose(); }

    [Fact]
    public void ACompatibleGccIsAccepted()
    {
        var api = GigabyteApi.Load(GccStub.Build(_dir.Path));
        Assert.True(api.Compatible, api.Problem == null ? null : api.Problem.Text);
        Assert.Equal("8.0.8.2", api.Version);
    }

    [Fact]
    public void TheBridgeWorksThroughReflection()
    {
        var bridge = new GigabyteBridge(GigabyteApi.Load(GccStub.Build(_dir.Path)), CpuPackageMonitor.WithoutReading(new Msg("cpu.hint.pawnio")));
        Assert.Equal(2, bridge.CountFans());
        Assert.Equal(new[] { "Temp 1", "Temp 2", "Temp 3" }, bridge.TempInputTitles);

        var sensors = bridge.ReadSensors();
        var cpu = sensors.Single(s => s.Id == "temperature-514");
        // GCC's "CPU" is the socket's sensor (item 19); without a package reading it is still what the panel shows.
        Assert.Equal("CPU · socket", cpu.Title);
        Assert.Equal(CpuRoles.Main, cpu.Role);
        Assert.Equal("cpu.hint.pawnio", cpu.Hint.Key);
        Assert.Equal("°C", cpu.Unit);
        Assert.Equal(52.5, cpu.Value);
        Assert.Equal("MHz", sensors.Single(s => s.Type == "Frequency").Unit);

        List<float> temps, rpms;
        bridge.ReadFanTach(out temps, out rpms);
        Assert.Equal(3, temps.Count);
        Assert.Equal(new[] { 1200f, 900f }, rpms);

        var original = bridge.GetCurve(1);
        Assert.Equal("gcc:1:System 2", original.Key);
        Assert.Equal("StairStepMode", original.Mode);
        Assert.Equal(8, original.Points.Count);

        var mine = new FanCurve { Index = 1, TempInput = 2, Mode = "SmartFanMode", DeltaT = 2 };
        mine.Points.AddRange(new[] { new[] { 0.0, 102 }, new[] { 50.0, 128 }, new[] { 85.0, 255 } });
        var back = bridge.SetCurve(mine);
        Assert.Equal(mine.Signature(), back.Signature());
        Assert.Equal("SmartFanMode", back.Mode);
        Assert.Equal(2, back.TempInput);
    }

    [Fact]
    public void FanManagerRunsOnTheReflectedBridge()
    {
        var bridge = new GigabyteBridge(GigabyteApi.Load(GccStub.Build(_dir.Path)));
        using (var data = new TempDir())
        {
            var m = new FanManager(bridge, new AppConfig(), data.Path);
            m.Describe();
            m.Apply(0, new Dictionary<string, object> { { "mode", "fixed" }, { "percent", 40 } });
            Assert.Equal(102, bridge.GetCurve(0).Points[0][1]);
            m.Restore(0);
            Assert.All(bridge.GetCurve(0).Points, p => Assert.Equal(255, p[1]));
        }
    }

    // What a GCC update that renames a member looks like: refused up front, with the member's name.
    [Fact]
    public void ARenamedMemberIsReportedByName()
    {
        var api = GigabyteApi.Load(GccStub.Build(_dir.Path, "public bool SetTemperatureInputSelection=public bool SetTemperatureInput"));
        Assert.False(api.Compatible);
        Assert.Contains("SetTemperatureInputSelection(Int32, Int32)", api.Problem.Text);
    }

    // At boot FanPilot's service can start before EasyTuneEngineService listens: once it does, the fans are there (a
    // connection made while it didn't used to be kept, and the bridge reported no fans for the whole session).
    [Fact]
    public void FansAppearOnceTheServiceListens()
    {
        var bridge = new GigabyteBridge(GigabyteApi.Load(GccStub.Build(_dir.Path)), CpuPackageMonitor.WithoutReading(null));
        GccStub.Set(_dir.Path, "Unreachable", 100);
        Assert.Throws<Unavailable>(() => bridge.CountFans());
        Assert.Null(bridge.Notice); // not asked yet: no "no fan control" either
        GccStub.Set(_dir.Path, "Unreachable", 0);
        Assert.Equal(2, bridge.CountFans());
        Assert.True(bridge.FanControlSupported);
        Assert.Equal("gcc:1:System 2", bridge.GetCurve(1).Key);
    }

    // The service may answer "not supported" while it starts: the bridge asks again, and FanManager takes the fans and
    // the curves saved for them instead of keeping them as orphans.
    [Fact]
    public void ANotSupportedAtBootIsAskedAgain()
    {
        GccStub.Build(_dir.Path);
        using (var data = new TempDir())
        {
            var before = new FanManager(new GigabyteBridge(GigabyteApi.Load(_dir.Path)), new AppConfig(), data.Path);
            before.Apply(0, new Dictionary<string, object> { { "mode", "fixed" }, { "percent", 40 } });

            // The PC restarts.
            GccStub.Set(_dir.Path, "NotReady", true);
            var bridge = new GigabyteBridge(GigabyteApi.Load(_dir.Path), CpuPackageMonitor.WithoutReading(null)) { SupportRecheck = TimeSpan.Zero };
            var fans = new FanManager(bridge, new AppConfig(), data.Path);
            var starting = fans.Describe();
            Assert.False((bool)starting["supported"]);
            Assert.Equal("board.noGen3", bridge.Notice.Key);
            Assert.Empty((List<object>)starting["fans"]);

            GccStub.Set(_dir.Path, "NotReady", false);
            var ready = fans.Describe();
            Assert.True((bool)ready["supported"]);
            Assert.Null(bridge.Notice);
            Assert.Equal(2, ((List<object>)ready["fans"]).Count);
            Assert.Empty((List<object>)ready["orphans"]);
            Assert.Equal(102, bridge.GetCurve(0).Points[0][1]);
        }
    }

    [Fact]
    public void AMissingDllIsReported()
    {
        var api = GigabyteApi.Load(_dir.Path);
        Assert.False(api.Compatible);
        Assert.Contains(GigabyteApi.CommonDll, api.Problem.Text);
    }

    [Fact]
    public void AnIncompatibleGccFallsBackToLhmInAutoAndStopsWhenGigabyteIsRequired()
    {
        var broken = GigabyteApi.Load(GccStub.Build(_dir.Path, "public bool IsSupported=public bool Supported"));
        Assert.Equal("lhm", Boards.Choose("auto", true, broken));
        Assert.Contains(AppNotices.All(), n => n.Key == "notice.gccFallback" && n.In(I18n.Portuguese).Contains("não é compatível") && n.Text.Contains("IsSupported"));
        var ex = Assert.Throws<Unavailable>(() => Boards.Choose("gigabyte", true, broken));
        Assert.Contains("IsSupported", ex.Message);
    }

    [Fact]
    public void BackendChoiceFollowsTheConfig()
    {
        var api = GigabyteApi.Load(GccStub.Build(_dir.Path));
        Assert.Equal("gigabyte", Boards.Choose("auto", true, api));
        Assert.Equal("lhm", Boards.Choose("lhm", true, api));
        Assert.Equal("lhm", Boards.Choose("auto", false, null));
        Assert.Throws<Unavailable>(() => Boards.Choose("gigabyte", false, null));
    }
}
