// data\ holds state that only makes sense on the PC that wrote it: the original fan curves, the curves the watchdog
// keeps applying, turbo state and paired phones. If the folder is copied to another PC (or the motherboard is
// swapped), applying that state would write one board's curves onto another, so it is moved aside instead.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Win32;

static class MachineData
{
    static readonly string[] MachineFiles = { "backup-curves.json", "desired-curves.json", "turbo.json", "sessions.json", "fan-meta.json", "alerts.json" };

    public static string Board { get; private set; }

    public static void Check(string dataDir)
    {
        string machineId = ReadHklm(@"SOFTWARE\Microsoft\Cryptography", "MachineGuid");
        Board = (ReadHklm(@"HARDWARE\DESCRIPTION\System\BIOS", "BaseBoardManufacturer") + " " +
                 ReadHklm(@"HARDWARE\DESCRIPTION\System\BIOS", "BaseBoardProduct")).Trim();
        string path = Path.Combine(dataDir, "machine.json");
        var current = new Dictionary<string, object> { { "machineId", machineId }, { "board", Board } };

        if (File.Exists(path))
        {
            // Unreadable (damaged) is treated like data from another PC: nothing is applied without knowing its origin.
            var saved = DataFile.Read(path);
            if (saved != null && Json.GetString(saved, "machineId", "") == machineId && Json.GetString(saved, "board", "") == Board) return;
            string archive = Path.Combine(dataDir, "outra-maquina-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(archive);
            // The .bak copies too, or the next read would bring the other machine's state back from them.
            foreach (string f in MachineFiles)
                foreach (string name in new[] { f, f + ".bak" })
                {
                    string src = Path.Combine(dataDir, name);
                    if (File.Exists(src)) File.Move(src, Path.Combine(archive, name));
                }
            Log.Warn((saved == null ? "machine.json ilegível" : "data\\ veio de outro PC ou de outra placa-mãe (" + Json.GetString(saved, "board", "?") + ")") +
                     "; curvas e pareamentos antigos movidos para " + archive + " e não serão aplicados.");
        }
        // No machine.json yet: a fresh install, or data written by a version without this check on this same PC.
        DataFile.WriteAtomic(path, current);
        Log.Info("Placa-mãe: " + (Board.Length > 0 ? Board : "desconhecida"));
    }

    static string ReadHklm(string subKey, string name)
    {
        try
        {
            using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var key = hklm.OpenSubKey(subKey))
                return key == null ? "" : Convert.ToString(key.GetValue(name) ?? "");
        }
        catch { return ""; }
    }
}
