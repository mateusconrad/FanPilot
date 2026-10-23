// Write test: temporarily set fan control 1 (System 1) to a flat ~40% curve, observe RPM, then restore.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Gigabyte.EnvironmentControl.Common.CoolingDevice.Fan;
using Gigabyte.EnvironmentControl.Common.CoolingDevice.Fan.CoordinateSystem;
using Gigabyte.Engine.EnvironmentControl.CoolingDevice.Fan;

static class Probe4 {
    const string GccLib = @"C:\Program Files\GIGABYTE\Control Center\Lib\MBEasyTune";
    const int Ctl = 1;
    static void Main() {
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) => {
            var p = Path.Combine(GccLib, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(p) ? Assembly.LoadFrom(p) : null;
        };
        Run();
    }
    static float Rpm(SmartGuardianFanControlCoordinateSystemModule cs) {
        var t = new List<float>(); var f = new List<float>();
        cs.GetHardwareMonitorDatas(ref t, ref f);
        return f[Ctl];
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Run() {
        var cs = new SmartGuardianFanControlCoordinateSystemModule();
        try { var _ = cs.IsSupported; } catch (System.Runtime.Remoting.RemotingException) { }
        var original = new SmartFanDataPointSet();
        cs.Get(Ctl, ref original);
        Console.WriteLine("original mode={0} pts={1}", original.Mode, string.Join(" ", original.DataPoints.Select(p => "(" + p.X + "," + p.Y + ")")));
        Console.WriteLine("rpm before: " + Rpm(cs));
        try {
            var test = (SmartFanDataPointSet)original.Clone();
            test.Mode = FanConfigMode.SmartFanMode;
            // The converter requires the last point to be (fullSpeedTemp, 255); otherwise it writes an empty config.
            double[] xs = { 0, 20, 30, 40, 50, 60, 70, 85 };
            test.DataPoints = xs.Select(x => new SmartFanDataPoint { X = x, Y = x >= 85 ? 255 : 102 }).ToList();
            cs.Set(Ctl, test);
            var back = new SmartFanDataPointSet(); cs.Get(Ctl, ref back);
            Console.WriteLine("written  mode={0} pts={1}", back.Mode, string.Join(" ", back.DataPoints.Select(p => "(" + p.X + "," + p.Y + ")")));
            for (int i = 1; i <= 8; i++) { Thread.Sleep(1000); Console.WriteLine("  t+{0}s rpm={1}", i, Rpm(cs)); }
        } finally {
            cs.Set(Ctl, original);
            Thread.Sleep(3000);
            Console.WriteLine("restored; rpm after 3s: " + Rpm(cs));
        }
    }
}
