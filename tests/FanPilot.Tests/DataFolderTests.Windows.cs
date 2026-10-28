// Item 14a: where the data lives, and the service's folder locked to SYSTEM and Administrators. DataFolderTests.Linux.cs
// checks the same on Linux (item 28).
using System;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using Xunit;

public sealed class DataFolderTests : IDisposable
{
    readonly TempDir _dir = new TempDir();

    public void Dispose()
    {
        // Restrict() leaves only SYSTEM and Administrators; as the owner, put ourselves back to clean up.
        foreach (var d in Directory.GetDirectories(_dir.Path))
        {
            var acl = new DirectorySecurity();
            acl.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            try { Directory.SetAccessControl(d, acl); } catch (UnauthorizedAccessException) { }
        }
        _dir.Dispose();
    }

    [Fact]
    public void WithoutDataTheStateLivesNextToTheExe()
    {
        Assert.Equal(@"C:\FanPilot\data", DataDirectory.Resolve(null, @"C:\FanPilot\", false));
        Assert.Equal(@"C:\FanPilot\data-fake", DataDirectory.Resolve("  ", @"C:\FanPilot\", true));
    }

    [Fact]
    public void DataIsExpandedAndMadeAbsolute()
    {
        string expected = Path.Combine(Environment.GetEnvironmentVariable("ProgramData"), "FanPilot");
        Assert.Equal(expected, DataDirectory.Resolve(@"%ProgramData%\FanPilot", @"C:\x\", false));
        Assert.True(Path.IsPathRooted(DataDirectory.Resolve(@"relativo\dados", @"C:\x\", false)));
    }

    [Fact]
    public void TheServiceFolderBelongsToSystemAndAdministratorsOnly()
    {
        string dir = Path.Combine(_dir.Path, "servico");
        DataDirectory.Restrict(dir);
        var acl = Directory.GetAccessControl(dir);
        Assert.True(acl.AreAccessRulesProtected);
        var who = acl.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()
            .Select(r => ((SecurityIdentifier)r.IdentityReference)).ToList();
        Assert.Equal(2, who.Count);
        Assert.Contains(who, s => s.IsWellKnown(WellKnownSidType.LocalSystemSid));
        Assert.Contains(who, s => s.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid));
    }
}
