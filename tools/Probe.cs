// Read-only probe: talks to EasyTuneEngineService through GCC's own client modules.
// Must run elevated (the service's IPC port ACL only admits Administrators).
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Gigabyte.EnvironmentControl.Common.HardwareMonitor;
using Gigabyte.EnvironmentControl.Common.CoolingDevice.Fan;
using Gigabyte.EnvironmentControl.Common.CoolingDevice.Fan.CoordinateSystem;
using Gigabyte.Engine.EnvironmentControl.HardwareMonitor;
using Gigabyte.Engine.EnvironmentControl.CoolingDevice.Fan;

static class Probe {
    const string GccLib = @"C:\Program Files\GIGABYTE\Control Center\Lib\MBEasyTune";

    static int Main() {
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) => {
            var p = Path.Combine(GccLib, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(p) ? Assembly.LoadFrom(p) : null;
        };
        Console.WriteLine("Process x64: " + Environment.Is64BitProcess);
        try { Run(); return 0; }
        catch (Exception ex) { Console.WriteLine(ex); return 1; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Run() {
        { var hw = new HardwareMonitorControlModule();
            Console.WriteLine("HW source type: " + hw.SourceType);
            var data = new HardwareMonitoredDataCollection();
            hw.GetCurrentMonitoredData(ref data);
            Console.WriteLine("Monitored items: " + data.Count);
            foreach (var d in data)
                Console.WriteLine("  [{0}] uuid={1} '{2}' = {3} {4}", d.Type, d.DeviceUUID, d.Title, d.Value, d.Unit);
        }

        { var sg = new SmartGuardianFanControlModule();
            Console.WriteLine("SmartGuardian supported={0} fanCtl={1} tach={2} tempIn={3} group=0x{4:X}",
                sg.IsSupported, sg.FanControlCount, sg.FanTachometerInputCount, sg.TemperatureInputCount, sg.GroupNumber);
            var temps = new List<float>(); var fans = new List<float>();
            sg.GetHardwareMonitorDatas(ref temps, ref fans);
            for (int i = 0; i < sg.TemperatureInputCount; i++) { string t; sg.GetTemperatureInputTitle(i, out t); Console.WriteLine("  temp[{0}] {1} = {2}", i, t, i < temps.Count ? temps[i] : float.NaN); }
            for (int i = 0; i < sg.FanTachometerInputCount; i++) { string t; sg.GetFanTachometerInputTitle(i, out t); Console.WriteLine("  tach[{0}] {1} = {2}", i, t, i < fans.Count ? fans[i] : float.NaN); }
            for (int i = 0; i < sg.FanControlCount; i++) {
                string t; FanSpeedControlModes m; int tin;
                sg.GetFanControlTitle(i, out t); sg.GetFanSpeedControlMode(i, out m); sg.GetTemperatureInputSelection(i, out tin);
                Console.WriteLine("  ctl[{0}] {1} mode={2} tempInput={3} sw={4}", i, t, m, tin, sg.IsSoftwareOperationMode(i));
            }
        }

        { var cs = new SmartGuardianFanControlModuleEx();
            Console.WriteLine("CoordinateSystem supported=" + cs.IsSupported);
            if (cs.IsSupported) {
                var hi = cs.HardwareInfo;
                Console.WriteLine("  info: group=0x{0:X} platform={1} special={2} fanCtl={3} tempIn={4} tach={5}",
                    hi.GroupNumber, hi.PlatformNumber, hi.SpecialCaseNumber, hi.FanControlCount, hi.TemperatureInputCount, hi.FanTachometerInputCount);
                for (int i = 0; i < hi.FanControlCount; i++) {
                    var set = new SmartFanDataPointSet();
                    cs.Get(i, ref set);
                    Console.Write("  curve[{0}] mode={1} dT={2} speed={3} pts:", i, set.Mode, set.DeltaTemperature, set.FanSpeed);
                    if (set.DataPoints != null) foreach (var p in set.DataPoints) Console.Write(" ({0},{1})", p.X, p.Y);
                    Console.WriteLine();
                }
            }
        }
    }
}

