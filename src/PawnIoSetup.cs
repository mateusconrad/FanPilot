// LibreHardwareMonitor reaches the motherboard chip through the PawnIO driver (https://pawnio.eu). Instead of sending
// the user to a website, FanPilot offers to install it with winget, Windows' package manager, which downloads the
// signed installer from the official release. Only asked in an interactive console; autostart just logs it.
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;

static class PawnIoSetup
{
    const string WingetId = "namazso.PawnIO";
    const string Site = "https://pawnio.eu";
    // winget/installer exit codes that still mean "the driver is there".
    const int RebootRequired = 3010, AlreadyInstalled = unchecked((int)0x8A15002B);

    public static bool IsInstalled
    {
        get { try { return LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled; } catch { return false; } }
    }

    public static void OfferInstall(bool interactive)
    {
        if (IsInstalled) return;
        if (!interactive || Console.IsInputRedirected)
        {
            Log.Warn(new Msg("console.pawnio.notInstalled", "site", Site));
            return;
        }
        Console.WriteLine();
        Console.WriteLine(new Msg("console.pawnio.why").In(I18n.SystemLanguage));
        Console.Write(new Msg("console.pawnio.ask", "id", WingetId, "site", Site).In(I18n.SystemLanguage) + " ");
        string answer = ReadLine(TimeSpan.FromMinutes(2));
        if (answer == null) { Console.WriteLine(); Log.Warn(new Msg("console.pawnio.noAnswer")); return; }
        answer = answer.Trim().ToLowerInvariant();
        // "s" (sim) or "y" (yes), whatever the language of the question.
        if (answer.Length > 0 && answer[0] != 's' && answer[0] != 'y') { Log.Info(new Msg("console.pawnio.declined")); return; }

        int code;
        try
        {
            var psi = new ProcessStartInfo("winget", "install --id " + WingetId + " --exact --silent --accept-package-agreements --accept-source-agreements")
            { UseShellExecute = false };
            using (var p = Process.Start(psi)) { p.WaitForExit(); code = p.ExitCode; }
        }
        catch (Win32Exception)
        {
            Log.Warn(new Msg("console.pawnio.noWinget", "site", Site));
            try { Process.Start(Site); } catch { }
            return;
        }
        if (IsInstalled)
        {
            Log.Info(new Msg(code == RebootRequired ? "console.pawnio.installedReboot" : "console.pawnio.installed"));
            return;
        }
        Log.Warn(new Msg(code == AlreadyInstalled ? "console.pawnio.unfinishedAlready" : "console.pawnio.unfinished", "code", code, "site", Site));
    }

    // Console.ReadLine with a timeout, so an unattended start doesn't wait forever.
    static string ReadLine(TimeSpan timeout)
    {
        var read = Task.Run(() => Console.ReadLine());
        return read.Wait(timeout) ? read.Result : null;
    }
}
