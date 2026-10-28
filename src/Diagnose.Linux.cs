// --diagnose on Linux: what it reads (each hwmon chip, the kernel modules, an ACPI conflict) comes with the hwmon
// support (item 28b). Until then it says so and writes nothing.
using System;

static partial class Diagnose
{
    public static int Run(string dataDir, AppConfig cfg, bool otherInstance, bool interactive)
    {
        Console.WriteLine(new Msg("diag.linuxPending").In(I18n.SystemLanguage));
        return 8;
    }
}
