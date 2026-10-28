// Item 14a on Linux (item 28): where the data lives, and the service's folder readable and writable by its owner only
// (root, for the service). DataFolderTests.Windows.cs checks the same on Windows.
using System;
using System.IO;
using Xunit;

public sealed class DataFolderTests : IDisposable
{
    readonly TempDir _dir = new TempDir();

    public void Dispose() { _dir.Dispose(); }

    [Fact]
    public void WithoutDataTheStateLivesNextToTheExe()
    {
        Assert.Equal("/opt/fanpilot/data", DataDirectory.Resolve(null, "/opt/fanpilot/", false));
        Assert.Equal("/opt/fanpilot/data-fake", DataDirectory.Resolve("  ", "/opt/fanpilot/", true));
    }

    [Fact]
    public void DataIsExpandedAndMadeAbsolute()
    {
        string home = Environment.GetEnvironmentVariable("HOME");
        Assert.Equal(Path.Combine(home, "fanpilot"), DataDirectory.Resolve("%HOME%/fanpilot", "/opt/x/", false));
        Assert.True(Path.IsPathRooted(DataDirectory.Resolve("relativo/dados", "/opt/x/", false)));
    }

    [Fact]
    public void TheServiceFolderBelongsToItsOwnerOnly()
    {
        string dir = Path.Combine(_dir.Path, "servico");
        DataDirectory.Restrict(dir);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(dir));
    }
}
