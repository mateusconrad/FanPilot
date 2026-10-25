// The version every surface shows (--version, the log, --diagnose, --hardware-test, /api/info, the panel): the
// project's semantic version (Directory.Build.props) and the commit it was built from, e.g. "0.1.0+1a2b3c4".
using System.Reflection;

static class AppVersion
{
    public static readonly string Text = Shorten(Informational());

    static string Informational()
    {
        var asm = typeof(AppVersion).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        return info != null ? info.InformationalVersion : asm.GetName().Version.ToString();
    }

    // The SDK appends the full commit hash ("+<40 hex>"); 7 characters identify it. A build without git has none.
    internal static string Shorten(string version)
    {
        int plus = version.IndexOf('+');
        return plus >= 0 && version.Length > plus + 8 ? version.Substring(0, plus + 8) : version;
    }
}
