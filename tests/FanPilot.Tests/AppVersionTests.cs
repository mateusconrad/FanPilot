// Item 11: one version everywhere, the one in Directory.Build.props, plus the commit the build came from.
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

public class AppVersionTests
{
    [Fact]
    public void IsTheProjectVersionPlusTheCommit()
    {
        var dir = new DirectoryInfo(TestEnv.OutputDir);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        Assert.NotNull(dir);
        string prefix = XDocument.Load(Path.Combine(dir.FullName, "Directory.Build.props")).Descendants("VersionPrefix").Single().Value;
        // Built from a git checkout (here, the CI) the commit is there; from a source zip there is none.
        Assert.Matches("^" + System.Text.RegularExpressions.Regex.Escape(prefix) + @"(\+[0-9a-f]{7})?$", AppVersion.Text);
    }

    [Fact]
    public void CommitIsShortenedToSevenCharacters()
    {
        Assert.Equal("0.1.0+1a2b3c4", AppVersion.Shorten("0.1.0+1a2b3c4d5e6f708192a3b4c5d6e7f8091a2b3c4"));
        Assert.Equal("0.1.0+1a2b3c4", AppVersion.Shorten("0.1.0+1a2b3c4"));
        Assert.Equal("0.1.0", AppVersion.Shorten("0.1.0"));
    }
}
