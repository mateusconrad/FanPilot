// Read-only probe of the 3rd-generation fan path used by GCC on this board.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Gigabyte.EnvironmentControl.Common.HardwareMonitor;
using Gigabyte.EnvironmentControl.Common.CoolingDevice.Fan.CoordinateSystem;
using Gigabyte.Engine.EnvironmentControl.CoolingDevice.Fan;

static class Probe3 {
    const string GccLib = @"C:\Program Files\GIGABYTE\Control Center\Lib\MBEasyTune";
    static void Main() {
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) => {
            var p = Path.Combine(GccLib, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(p) ? Assembly.LoadFrom(p) : null;
        };
        Run();
    }
    static T Retry<T>(Func<T> f) {
        for (int i = 0; ; i++) {
            try { return f(); }
            catch (System.Runtime.Remoting.RemotingException) { if (i >= 2) throw; }
        }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Run() {
        var cs = new SmartGuardianFanControlCoordinateSystemModule();
        bool sup = Retry(() => cs.IsSupported);
        Console.WriteLine("CoordinateSystemModule supported=" + sup);
        if (!sup) return;
        var hi = cs.HardwareInfo;
        Console.WriteLine("group=0x{0:X} platform={1} special={2} fanCtl={3} tempIn={4} tach={5} volt={6}",
            hi.GroupNumber, hi.PlatformNumber, hi.SpecialCaseNumber, hi.FanControlCount, hi.TemperatureInputCount, hi.FanTachometerInputCount, hi.VoltageInputCount);
        var temps = new List<float>(); var fans = new List<float>();
        cs.GetHardwareMonitorDatas(ref temps, ref fans);
        for (int i = 0; i < temps.Count; i++) { string t; cs.GetSensorTitle(SensorTypes.Temperature, i, out t); Console.WriteLine("  temp[{0}] {1} = {2}", i, t, temps[i]); }
        for (int i = 0; i < fans.Count; i++) { string t; cs.GetSensorTitle(SensorTypes.Fan, i, out t); Console.WriteLine("  fan[{0}] {1} = {2}", i, t, fans[i]); }
        for (int i = 0; i < hi.FanControlCount; i++) {
            string t; int tin; cs.GetSensorTitle(SensorTypes.Control, i, out t); cs.GetTemperatureInputSelection(i, out tin);
            var set = new SmartFanDataPointSet();
            cs.Get(i, ref set);
            Console.Write("  ctl[{0}] '{1}' tempIn={2} mode={3} dT={4} speed={5} pts:", i, t, tin, set.Mode, set.DeltaTemperature, set.FanSpeed);
            if (set.DataPoints != null) foreach (var p in set.DataPoints) Console.Write(" ({0},{1})", p.X, p.Y);
            Console.WriteLine();
        }
        var sw = new SmartFanEmbeddedControlSwitchModule();
        int a, b; sw.GetFirmwareVersion(out a, out b);
        Console.WriteLine("EmbeddedControlSwitch enabled={0} fw={1}.{2}", Retry(() => sw.IsEnable()), a, b);
    }
}
