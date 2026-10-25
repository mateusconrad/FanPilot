// Item 19: which reading is "the CPU temperature" on each kind of board, and that the snapshot and the history carry it.
using System.Collections.Generic;
using System.Linq;
using Xunit;

public class CpuRolesTests
{
    static List<SensorReading> Board()
    {
        return new List<SensorReading> {
            new SensorReading { Id = "temperature-512", Type = "Temperature", Title = "System 1", Value = 38, Unit = "°C" },
            new SensorReading { Id = "temperature-514", Type = "Temperature", Title = "CPU", Value = 52, Unit = "°C" },
            new SensorReading { Id = "frequency-256", Type = "Frequency", Title = "CPU", Value = 4600, Unit = "MHz" },
        };
    }

    [Fact]
    public void WithThePackageTheCardShowsTheCoresAndTheTableBoth()
    {
        var r = Board();
        CpuRoles.Assign(r, 66.04, true, "dica");
        var main = CpuRoles.MainOf(r);
        Assert.Equal("CPU · pacote", main.Title);
        Assert.Equal(66.0, main.Value);
        var socket = r.Single(s => s.Id == "temperature-514");
        Assert.Equal("CPU · socket", socket.Title);
        Assert.Equal(CpuRoles.Socket, socket.Role);
        Assert.Null(socket.Hint);
        Assert.Equal("CPU", r.Single(s => s.Type == "Frequency").Title); // only the temperature is renamed
    }

    [Fact]
    public void WithoutThePackageAGigabyteBoardShowsTheSocketAndSaysHowToGetTheCores()
    {
        var r = Board();
        CpuRoles.Assign(r, null, true, "Instale o PawnIO.");
        var main = CpuRoles.MainOf(r);
        Assert.Equal("temperature-514", main.Id);
        Assert.Equal("CPU · socket", main.Title);
        Assert.Equal("Instale o PawnIO.", main.Hint);
    }

    [Fact]
    public void OnLibreHardwareMonitorNothingVisibleChanges()
    {
        var r = Board();
        CpuRoles.Assign(r, null, false, "nunca mostrada");
        var main = CpuRoles.MainOf(r);
        Assert.Equal("CPU", main.Title);
        Assert.Equal(CpuRoles.Main, main.Role);
        Assert.Null(main.Hint);
    }

    [Fact]
    public void ABoardThatSetsNoRoleStillHasACpu()
    {
        Assert.Equal("temperature-514", CpuRoles.MainOf(Board()).Id);
    }
}

public sealed class CpuSnapshotTests
{
    [Fact]
    public void TheSnapshotCarriesTheRoleAndTheHint()
    {
        var withPackage = FakeBoard.Demo();
        var json = Json.Serialize(withPackage.ReadSensors().Select(s => s.Role).Where(r => r != null).ToList());
        Assert.Contains("cpu-main", json);
        Assert.Contains("cpu-socket", json);

        var worst = FakeBoard.Scenario("worst");
        var main = CpuRoles.MainOf(worst.ReadSensors());
        Assert.Equal("CPU · socket", main.Title);
        Assert.Contains("PawnIO", main.Hint);

        var lhm = FakeBoard.Scenario("lhm");
        Assert.Equal("CPU", CpuRoles.MainOf(lhm.ReadSensors()).Title);
    }
}
