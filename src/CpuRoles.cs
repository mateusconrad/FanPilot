// Which reading is "the CPU temperature" (item 19). On a Gigabyte board, Gigabyte Control Center's "CPU" sensor is the
// socket's, read on the motherboard: well below what HWiNFO, Task Manager or a game show. The package temperature (the
// cores) comes from LibreHardwareMonitor when PawnIO is installed; the panel's card and chart and the alerts follow the
// reading marked "cpu-main".
using System;
using System.Collections.Generic;
using System.Linq;

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
    public static void Assign(List<SensorReading> readings, double? package, bool boardSensorIsSocket, Msg hint)
    {
        var board = readings.FirstOrDefault(r => r.Type == "Temperature" && r.Title == "CPU");
        if (package.HasValue)
        {
            if (board != null) { Name(board, "cpu.socket"); board.Role = Socket; }
            var main = new SensorReading { Id = "temperature-cpu-package", Type = "Temperature", Value = Math.Round(package.Value, 1), Unit = "°C", Role = Main };
            Name(main, "cpu.package");
            readings.Insert(0, main);
            return;
        }
        if (board == null) return;
        board.Role = Main;
        if (!boardSensorIsSocket) return;
        Name(board, "cpu.socket");
        board.Hint = hint;
    }

    // "CPU · pacote" in the panel, "CPU · package" in the API and the log.
    static void Name(SensorReading r, string key)
    {
        r.TitleKey = key;
        r.Title = I18n.T(I18n.English, key);
    }
}
