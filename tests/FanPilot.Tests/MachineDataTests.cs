// data\ copied from another PC (or a damaged machine.json) must not apply that machine's curves or pairings.
using System;
using System.IO;
using System.Linq;
using Xunit;

public sealed class MachineDataTests : IDisposable
{
    readonly TempDir _dir = new TempDir();

    public void Dispose() { _dir.Dispose(); }

    void StateFromAnotherRun()
    {
        DataFile.WriteAtomic(_dir.File("desired-curves.json"), new System.Collections.Generic.Dictionary<string, object> { { "1", 1 } });
        DataFile.WriteAtomic(_dir.File("desired-curves.json"), new System.Collections.Generic.Dictionary<string, object> { { "1", 2 } }); // + .bak
        File.WriteAllText(_dir.File("sessions.json"), "{\"sessions\": []}");
    }

    [Fact]
    public void SameMachineKeepsItsState()
    {
        MachineData.Check(_dir.Path); // records this PC
        StateFromAnotherRun();
        MachineData.Check(_dir.Path);
        Assert.True(File.Exists(_dir.File("desired-curves.json")));
        Assert.Empty(Directory.GetDirectories(_dir.Path, "outra-maquina-*"));
    }

    [Fact]
    public void OtherMachinesStateIsArchivedWithItsBackups()
    {
        File.WriteAllText(_dir.File("machine.json"), "{\"machineId\": \"outro-pc\", \"board\": \"Outra placa\"}");
        StateFromAnotherRun();
        MachineData.Check(_dir.Path);
        AssertArchived();
    }

    [Fact]
    public void DamagedMachineFileIsTreatedAsAnotherMachine()
    {
        File.WriteAllText(_dir.File("machine.json"), "{\"machineId\": ");
        StateFromAnotherRun();
        MachineData.Check(_dir.Path);
        AssertArchived();
    }

    void AssertArchived()
    {
        var archive = Directory.GetDirectories(_dir.Path, "outra-maquina-*").Single();
        Assert.True(File.Exists(Path.Combine(archive, "desired-curves.json")));
        Assert.True(File.Exists(Path.Combine(archive, "desired-curves.json.bak")));
        Assert.True(File.Exists(Path.Combine(archive, "sessions.json")));
        Assert.False(File.Exists(_dir.File("desired-curves.json")));
        Assert.False(File.Exists(_dir.File("desired-curves.json.bak")));
        Assert.Null(DataFile.Read(_dir.File("desired-curves.json")));
        Assert.True(File.Exists(_dir.File("machine.json"))); // rewritten for this PC
    }
}
