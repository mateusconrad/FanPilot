// Item 03: state files are written atomically (previous version in .bak) and read tolerantly.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

public sealed class DataFileTests : IDisposable
{
    readonly TempDir _dir = new TempDir();

    public void Dispose() { _dir.Dispose(); }

    static Dictionary<string, object> Value(int n) { return new Dictionary<string, object> { { "n", n } }; }

    [Fact]
    public void WriteKeepsThePreviousVersionAndLeavesNoTemporaryFile()
    {
        string path = _dir.File("desired-curves.json");
        DataFile.WriteAtomic(path, Value(1));
        DataFile.WriteAtomic(path, Value(2));
        Assert.Equal(2, Json.GetInt(DataFile.Read(path), "n", 0));
        Assert.Equal(1, Json.GetInt(Json.ParseObject(File.ReadAllText(path + ".bak")), "n", 0));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void MissingFileIsNotAProblemAndTheBakIsNotResurrected()
    {
        string path = _dir.File("turbo.json");
        DataFile.WriteAtomic(path, Value(1));
        DataFile.WriteAtomic(path, Value(2));
        File.Delete(path); // the user deleted it to start over
        Assert.Null(DataFile.Read(path));
    }

    [Fact]
    public void DamagedFileFallsBackToThePreviousVersionAndIsMovedAside()
    {
        string path = _dir.File("desired-curves.json");
        DataFile.WriteAtomic(path, Value(1));
        DataFile.WriteAtomic(path, Value(2));
        File.WriteAllText(path, "{\"n\": 3, "); // cut in the middle of a write
        Assert.Equal(1, Json.GetInt(DataFile.Read(path), "n", 0));
        Assert.False(File.Exists(path));
        Assert.Single(Directory.GetFiles(_dir.Path, "desired-curves.json.corrompido-*"));
        Assert.Contains(AppNotices.All(), n => n.Key == "notice.fileUnreadable.previous" && n.In(I18n.Portuguese).StartsWith("desired-curves.json estava ilegível"));

        // The next write must not push the damaged content into the .bak.
        DataFile.WriteAtomic(path, Value(4));
        Assert.Equal(1, Json.GetInt(Json.ParseObject(File.ReadAllText(path + ".bak")), "n", 0));
    }

    [Fact]
    public void DamagedFileWithoutAPreviousVersionStartsEmpty()
    {
        string path = _dir.File("sessions.json");
        File.WriteAllText(path, "");
        Assert.Null(DataFile.Read(path));
        Assert.Single(Directory.GetFiles(_dir.Path, "sessions.json.corrompido-*"));
    }
}

public sealed class AppConfigTests : IDisposable
{
    readonly TempDir _dir = new TempDir();

    public void Dispose() { _dir.Dispose(); }

    [Fact]
    public void FirstRunWritesTheDefaults()
    {
        string path = _dir.File("config.json");
        var cfg = AppConfig.Load(path);
        Assert.Equal(8787, cfg.Port);
        Assert.Equal(20, Json.GetInt(Json.ParseObject(File.ReadAllText(path)), "minDutyPercent", 0));
    }

    [Fact]
    public void ValuesAreReadAndClamped()
    {
        string path = _dir.File("config.json");
        File.WriteAllText(path, "{\"port\": 9000, \"minDutyPercent\": 150, \"gpuSafetyTemp\": 10, \"backend\": \" LHM \"}");
        var cfg = AppConfig.Load(path);
        Assert.Equal(9000, cfg.Port);
        Assert.Equal(100, cfg.MinDutyPercent);
        Assert.Equal(60, cfg.GpuSafetyTemp);
        Assert.Equal("lhm", cfg.Backend);
    }

    [Theory]
    [InlineData("{\"port\": 9000,}")]          // trailing comma, the classic hand-edit mistake
    [InlineData("{\"port\": \"nove mil\"}")]   // wrong type
    [InlineData("[1, 2]")]                     // not an object
    public void InvalidFileMeansDefaultsAndIsLeftUntouched(string content)
    {
        string path = _dir.File("config.json");
        File.WriteAllText(path, content);
        var cfg = AppConfig.Load(path);
        Assert.Equal(8787, cfg.Port);
        Assert.Equal(content, File.ReadAllText(path));
        Assert.Contains(AppNotices.All(), n => n.Key == "notice.configInvalid" && n.In(I18n.Portuguese).StartsWith("config.json inválido"));
    }

    [Fact]
    public void ImpossiblePortFallsBackToTheDefault()
    {
        string path = _dir.File("config.json");
        File.WriteAllText(path, "{\"port\": 70000}");
        Assert.Equal(8787, AppConfig.Load(path).Port);
    }
}
