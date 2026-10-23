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
            Log.Warn("Driver PawnIO não instalado: abra o FanPilot pela janela (sem --no-browser) para instalá-lo, ou baixe em " + Site + ".");
            return;
        }
        Console.WriteLine();
        Console.WriteLine("O driver PawnIO não está instalado. Sem ele o FanPilot não lê os sensores nem controla os fans");
        Console.WriteLine("da placa-mãe (é o mesmo driver usado pelo LibreHardwareMonitor e pelo FanControl).");
        Console.Write("Instalar agora pelo winget (pacote " + WingetId + ", do site oficial " + Site + ")? [S/n] ");
        string answer = ReadLine(TimeSpan.FromMinutes(2));
        if (answer == null) { Console.WriteLine(); Log.Warn("Sem resposta: PawnIO não instalado."); return; }
        answer = answer.Trim().ToLowerInvariant();
        if (answer.Length > 0 && answer[0] != 's' && answer[0] != 'y') { Log.Info("Instalação do PawnIO recusada."); return; }

        int code;
        try
        {
            var psi = new ProcessStartInfo("winget", "install --id " + WingetId + " --exact --silent --accept-package-agreements --accept-source-agreements")
            { UseShellExecute = false };
            using (var p = Process.Start(psi)) { p.WaitForExit(); code = p.ExitCode; }
        }
        catch (Win32Exception)
        {
            Log.Warn("winget não encontrado neste Windows. Baixe o PawnIO em " + Site + ", instale e abra o FanPilot de novo.");
            try { Process.Start(Site); } catch { }
            return;
        }
        if (IsInstalled)
        {
            Log.Info("PawnIO instalado." + (code == RebootRequired ? " O instalador pediu para reiniciar o PC; se os fans não aparecerem, reinicie." : ""));
            return;
        }
        Log.Warn("A instalação do PawnIO não terminou (código " + code + (code == AlreadyInstalled ? ", o winget diz que já está instalado" : "") +
                 "). Reinicie o PC ou instale manualmente por " + Site + ".");
    }

    // Console.ReadLine with a timeout, so an unattended start doesn't wait forever.
    static string ReadLine(TimeSpan timeout)
    {
        var read = Task.Run(() => Console.ReadLine());
        return read.Wait(timeout) ? read.Result : null;
    }
}
