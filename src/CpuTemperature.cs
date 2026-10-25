// Which reading is "the CPU temperature" (item 19). On a Gigabyte board, Gigabyte Control Center's "CPU" sensor is the
// socket's, read on the motherboard: well below what HWiNFO, Task Manager or a game show. The package temperature (the
// cores) comes from LibreHardwareMonitor when PawnIO is installed; the panel's card and chart and the alerts follow the
// reading marked "cpu-main".
using System;
using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;

static class CpuRoles
{
    public const string Main = "cpu-main", Socket = "cpu-socket";

    // The CPU reading to show or act on: "cpu-main", or the one titled "CPU" from a board that sets no role.
    public static SensorReading MainOf(IEnumerable<SensorReading> readings)
    {
        var list = readings.ToList();
        return list.FirstOrDefault(r => r.Role == Main) ?? list.FirstOrDefault(r => r.Type == "Temperature" && r.Title == "CPU");
    }

    // package: the cores' temperature, if a CPU monitor gives one. boardSensorIsSocket: the board's "CPU" sensor is the
    // socket's (a board that keeps its curves in the chip, Gigabyte) and not already the package (LibreHardwareMonitor).
    // hint: what the panel says when there is no package reading on such a board.
    public static void Assign(List<SensorReading> readings, double? package, bool boardSensorIsSocket, string hint)
    {
        var board = readings.FirstOrDefault(r => r.Type == "Temperature" && r.Title == "CPU");
        if (package.HasValue)
        {
            if (board != null) { board.Title = "CPU · socket"; board.Role = Socket; }
            readings.Insert(0, new SensorReading {
                Id = "temperature-cpu-package", Type = "Temperature", Title = "CPU · pacote", Value = Math.Round(package.Value, 1), Unit = "°C", Role = Main });
            return;
        }
        if (board == null) return;
        board.Role = Main;
        if (!boardSensorIsSocket) return;
        board.Title = "CPU · socket";
        board.Hint = hint;
    }
}

// LibreHardwareMonitor with the CPU only: never the motherboard or its Super I/O chip, so it does not compete with
// Gigabyte Control Center for it. Without PawnIO there is no reading, and Hint says how to get one.
sealed class CpuPackageMonitor
{
    readonly object _sync = new object();
    Computer _pc;
    ISensor _sensor;

    public string Hint { get; private set; }

    // No reading, only the hint: the tests, which must not depend on the PC's drivers.
    internal static CpuPackageMonitor Unavailable(string hint) { return new CpuPackageMonitor { Hint = hint }; }

    public static CpuPackageMonitor Open()
    {
        var m = new CpuPackageMonitor();
        if (!PawnIoSetup.IsInstalled)
        {
            m.Hint = "Instale o PawnIO (pawnio.eu) para ver a temperatura dos núcleos.";
            return m;
        }
        try
        {
            m._pc = new Computer { IsCpuEnabled = true };
            m._pc.Open();
            var all = new List<IHardware>();
            foreach (var hw in m._pc.Hardware) LhmBoard.Collect(hw, all);
            foreach (var hw in all) hw.Update();
            m._sensor = LhmBoard.CpuTemperature(all);
            if (m._sensor == null) m.Close();
            else Log.Info("Temperatura da CPU (" + m._sensor.Name + ") pelo LibreHardwareMonitor, junto com o Gigabyte Control Center.");
        }
        catch (Exception ex)
        {
            Log.Warn("Temperatura dos núcleos da CPU indisponível: " + ex.Message);
            m.Close();
        }
        return m;
    }

    // The package temperature now, or null when there is none (or it reads something impossible).
    public double? Read()
    {
        lock (_sync)
        {
            if (_sensor == null) return null;
            try { _sensor.Hardware.Update(); }
            catch (Exception) { return null; }
            var v = _sensor.Value;
            return v.HasValue && v.Value > 0 && v.Value < 125 ? v.Value : (double?)null;
        }
    }

    public void Close()
    {
        lock (_sync)
        {
            try { if (_pc != null) _pc.Close(); } catch { }
            _pc = null;
            _sensor = null;
        }
    }
}
