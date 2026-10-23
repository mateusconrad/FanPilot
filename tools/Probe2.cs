// Experiment: does SmartGuardian fail on its own, or only after another module's call?
using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Gigabyte.EnvironmentControl.Common.HardwareMonitor;
using Gigabyte.Engine.EnvironmentControl.HardwareMonitor;
using Gigabyte.Engine.EnvironmentControl.CoolingDevice.Fan;

static class Probe2 {
    const string GccLib = @"C:\Program Files\GIGABYTE\Control Center\Lib\MBEasyTune";
    static void Main() {
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) => {
            var p = Path.Combine(GccLib, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(p) ? Assembly.LoadFrom(p) : null;
        };
        Run();
    }
    static void Try(string label, Action a) {
        for (int i = 1; i <= 3; i++) {
            try { a(); Console.WriteLine(label + " attempt " + i + ": OK"); return; }
            catch (Exception ex) { Console.WriteLine(label + " attempt " + i + ": " + ex.GetType().Name + ": " + ex.Message.Trim()); }
        }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Run() {
        var sg = new SmartGuardianFanControlModule();
        Try("SG.IsSupported (first)", () => Console.WriteLine("  supported=" + sg.IsSupported + " ctl=" + sg.FanControlCount));
        var hw = new HardwareMonitorControlModule();
        Try("HW.Get", () => { var d = new HardwareMonitoredDataCollection(); hw.GetCurrentMonitoredData(ref d); Console.WriteLine("  items=" + d.Count); });
        Try("SG.IsSupported (after HW)", () => Console.WriteLine("  supported=" + sg.IsSupported));
        var fc = new FanControlModule();
        Try("FC.FanControlCount", () => Console.WriteLine("  ctl=" + fc.FanControlCount + " fans=" + fc.FanCount + " maxPwm=" + fc.MaxPwmValue));
        var cs = new SmartGuardianFanControlModuleEx();
        Try("CS.IsSupported", () => Console.WriteLine("  supported=" + cs.IsSupported));
    }
}
