// FanPilot.exe --diagnose: read-only report for boards nobody has tested yet. Opens LibreHardwareMonitor, lists what
// it found and how FanPilot maps it (reference sensors, controllable headers), and saves it to data\ to be sent
// along with an issue. Never changes a fan speed.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using LibreHardwareMonitor.Hardware;

static class Diagnose
{
    public static int Run(string dataDir, AppConfig cfg)
    {
        string path = Path.Combine(dataDir, "diagnostico-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        var sb = new StringBuilder();
        sb.AppendLine("FanPilot --diagnose  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("Windows: " + Environment.OSVersion.VersionString + (Environment.Is64BitOperatingSystem ? " x64" : ""));
        sb.AppendLine("Placa-mãe (registro): " + MachineData.Board);
        sb.AppendLine("Gigabyte Control Center: " + (Directory.Exists(Program.GccLibDir) ? Program.GccLibDir : "não encontrado"));
        sb.AppendLine("backend no config.json: " + cfg.Backend);
        bool pawnIo = false;
        try { pawnIo = LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled; sb.AppendLine("PawnIO: " + (pawnIo ? "instalado, versão " + LibreHardwareMonitor.PawnIo.PawnIo.Version : "NÃO instalado")); }
        catch (Exception ex) { sb.AppendLine("PawnIO: erro ao verificar: " + ex.Message); }
        sb.AppendLine();

        // How FanPilot would see this board (the same class the app uses; no curve is set, so nothing is written).
        var board = new LhmBoard();
        try
        {
            sb.AppendLine("== Como o FanPilot enxerga esta placa (LibreHardwareMonitor)");
            sb.AppendLine("Aviso: " + (board.Notice ?? "nenhum"));
            sb.AppendLine("Sensores de referência para as curvas:");
            var titles = board.TempInputTitles;
            System.Collections.Generic.List<float> temps, rpms;
            board.ReadFanTach(out temps, out rpms);
            for (int i = 0; i < titles.Length; i++)
                sb.AppendLine("  [" + i + "] " + titles[i] + " = " + (i < temps.Count && !float.IsNaN(temps[i]) ? temps[i].ToString("0.0") + " °C" : "sem leitura"));
            sb.AppendLine("Fans controláveis:");
            var curves = board.GetCurves();
            for (int i = 0; i < curves.Count; i++)
                sb.AppendLine("  [" + i + "] " + curves[i].Title + " · " + (i < rpms.Count ? rpms[i].ToString("0") : "?") + " RPM");
            if (curves.Count == 0) sb.AppendLine("  nenhum");
            sb.AppendLine("Leituras para o painel:");
            foreach (var r in board.ReadSensors()) sb.AppendLine("  " + r.Type + " | " + r.Title + " = " + r.Value + " " + r.Unit + "   (" + r.Id + ")");
        }
        finally { board.Shutdown(); }
        sb.AppendLine();

        sb.AppendLine("== Relatório completo do LibreHardwareMonitor");
        var pc = new Computer { IsMotherboardEnabled = true, IsCpuEnabled = true, IsGpuEnabled = true, IsControllerEnabled = true };
        try
        {
            pc.Open();
            sb.AppendLine(pc.GetReport());
        }
        catch (Exception ex) { sb.AppendLine("Falhou: " + ex); }
        finally { try { pc.Close(); } catch { } }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        Console.WriteLine("Diagnóstico salvo em " + path);
        Console.WriteLine("Nenhuma velocidade de fan foi alterada. Envie esse arquivo junto com o modelo da placa-mãe.");
        try { Process.Start("notepad.exe", "\"" + path + "\""); } catch { }
        return 0;
    }
}
