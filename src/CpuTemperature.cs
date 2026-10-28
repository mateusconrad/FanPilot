// The CPU package temperature for Gigabyte boards (item 19; the roles are in CpuRoles.cs). Windows only.
using System;
using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;

// LibreHardwareMonitor with the CPU only: never the motherboard or its Super I/O chip, so it does not compete with
// Gigabyte Control Center for it. Without PawnIO there is no reading, and Hint says how to get one.
sealed class CpuPackageMonitor
{
    readonly object _sync = new object();
    Computer _pc;
    ISensor _sensor;

    public Msg Hint { get; private set; }

    // No reading, only the hint: the tests, which must not depend on the PC's drivers.
    internal static CpuPackageMonitor WithoutReading(Msg hint) { return new CpuPackageMonitor { Hint = hint }; }

    public static CpuPackageMonitor Open()
    {
        var m = new CpuPackageMonitor();
        if (!PawnIoSetup.IsInstalled)
        {
            m.Hint = new Msg("cpu.hint.pawnio");
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
            else Log.Info("CPU temperature (" + m._sensor.Name + ") from LibreHardwareMonitor, next to Gigabyte Control Center.");
        }
        catch (Exception ex)
        {
            Log.Warn("CPU cores' temperature unavailable: " + ex.Message);
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
