// Builds, at test time, two assemblies shaped like Gigabyte Control Center's client DLLs (same names, namespaces,
// types and member signatures as GCC 8.0.8.2) with an in-memory "service" behind them, so GigabyteApi and
// GigabyteBridge can be exercised without GCC (CI). Only for the tests; never shipped.
using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Threading;

static class GccStub
{
    static int _builds;

    const string CommonSource = @"
using System.Collections.Generic;
using System.Collections.ObjectModel;
[assembly: System.Reflection.AssemblyVersion(""$VERSION$"")]
namespace Gigabyte.EnvironmentControl.Common.HardwareMonitor
{
    public enum SensorTypes { None, Voltage, Frequency, Temperature, Fan, Control, Power, Current, Flow, Sound }
    public class HardwareMonitoredData
    {
        public SensorTypes Type { get; set; }
        public string Title { get; set; }
        public ushort DeviceUUID { get; set; }
        public float Value { get; set; }
        public string Unit { get; set; }
    }
    public class HardwareMonitoredDataCollection : ObservableCollection<HardwareMonitoredData> { }
}
namespace Gigabyte.EnvironmentControl.Common.CoolingDevice.Fan
{
    public enum FanConfigMode { Unknown, SmartFanMode, RpmFixedMode, LiquidMode, TachometerMode, StairStepMode, TachometerClosedLoopMode }
    public class EnvironmentControllerInfo
    {
        public int FanControlCount { get; set; }
        public int TemperatureInputCount { get; set; }
    }
}
namespace Gigabyte.EnvironmentControl.Common.CoolingDevice.Fan.CoordinateSystem
{
    public class SmartFanDataPoint { public double X { get; set; } public double Y { get; set; } }
    public class SmartFanDataPointSet
    {
        public Gigabyte.EnvironmentControl.Common.CoolingDevice.Fan.FanConfigMode Mode { get; set; }
        public int DeltaTemperature { get; set; }
        public int FanSpeed { get; set; }
        public List<SmartFanDataPoint> DataPoints { get; set; }
    }
}";

    const string EngineSource = @"
using System.Collections.Generic;
using Gigabyte.EnvironmentControl.Common.HardwareMonitor;
using Gigabyte.EnvironmentControl.Common.CoolingDevice.Fan;
using Gigabyte.EnvironmentControl.Common.CoolingDevice.Fan.CoordinateSystem;
[assembly: System.Reflection.AssemblyVersion(""$VERSION$"")]
[assembly: System.Reflection.AssemblyFileVersion(""8.0.8.2"")]
namespace Gigabyte.Engine.EnvironmentControl.HardwareMonitor
{
    public class HardwareMonitorControlModule
    {
        public void GetCurrentMonitoredData(SensorTypes sensorType, ref HardwareMonitoredDataCollection pHwMonitoredDatas) { }
        public void GetCurrentMonitoredData(ref HardwareMonitoredDataCollection pHwMonitoredDatas)
        {
            pHwMonitoredDatas.Add(new HardwareMonitoredData { Type = SensorTypes.Temperature, Title = ""CPU"", DeviceUUID = 514, Value = 52.5f, Unit = """" });
            pHwMonitoredDatas.Add(new HardwareMonitoredData { Type = SensorTypes.Fan, Title = ""System 1"", DeviceUUID = 769, Value = 2350f, Unit = """" });
            pHwMonitoredDatas.Add(new HardwareMonitoredData { Type = SensorTypes.Frequency, Title = ""CPU"", DeviceUUID = 256, Value = 4600f, Unit = ""MHz"" });
        }
        public void Dispose() { }
    }
}
namespace Gigabyte.Engine.EnvironmentControl.CoolingDevice.Fan
{
    public class SmartGuardianFanControlCoordinateSystemModule
    {
        // Shared by every instance, like the service behind the IPC.
        static readonly Dictionary<int, SmartFanDataPointSet> Curves = new Dictionary<int, SmartFanDataPointSet>();
        static readonly Dictionary<int, int> Inputs = new Dictionary<int, int>();
        public bool IsSupported { get { return true; } }
        public EnvironmentControllerInfo HardwareInfo { get { return new EnvironmentControllerInfo { FanControlCount = 2, TemperatureInputCount = 3 }; } }
        public void GetSensorTitle(SensorTypes sensorType, int sensorIndex, out string pTitle)
        {
            pTitle = (sensorType == SensorTypes.Control ? ""System "" : ""Temp "") + (sensorIndex + 1);
        }
        public void GetHardwareMonitorDatas(ref List<float> pTemperatureDatas, ref List<float> pFanSpeedDatas)
        {
            pTemperatureDatas.AddRange(new[] { 40f, 52f, 45f });
            pFanSpeedDatas.AddRange(new[] { 1200f, 900f });
        }
        public void Get(int fanControlIndex, ref SmartFanDataPointSet pCurrentSmartFanControlConfig)
        {
            SmartFanDataPointSet saved;
            if (Curves.TryGetValue(fanControlIndex, out saved)) { pCurrentSmartFanControlConfig = saved; return; }
            var set = new SmartFanDataPointSet { Mode = FanConfigMode.StairStepMode, DeltaTemperature = 1, DataPoints = new List<SmartFanDataPoint>() };
            foreach (var t in new[] { 0, 20, 30, 40, 45, 50, 55, 60 }) set.DataPoints.Add(new SmartFanDataPoint { X = t, Y = 255 });
            pCurrentSmartFanControlConfig = set;
        }
        public void Set(int fanControlIndex, SmartFanDataPointSet newSmartFanControlConfig) { Curves[fanControlIndex] = newSmartFanControlConfig; }
        public void GetTemperatureInputSelection(int fanControlIndex, out int pCurrentTemperatureInputSelection)
        {
            int input;
            pCurrentTemperatureInputSelection = Inputs.TryGetValue(fanControlIndex, out input) ? input : 0;
        }
        public bool $SETINPUT$(int fanControlIndex, int newTemperatureInputSelection)
        {
            Inputs[fanControlIndex] = newTemperatureInputSelection;
            return true;
        }
        public void Dispose() { }
    }
}";

    // A folder with both DLLs, under GCC's file names. rename: "Old=New" applied to the engine's source, to play a GCC
    // update that changed a member. Assemblies without a strong name bind by simple name only (the version is
    // ignored), so each build gets its own assembly name inside the files: a second build loaded in the same test
    // process would otherwise come back as the first one.
    public static string Build(string dir, string rename = null)
    {
        string id = "GccStub" + Interlocked.Increment(ref _builds);
        string engine = EngineSource.Replace("$SETINPUT$", "SetTemperatureInputSelection");
        if (rename != null) engine = engine.Replace(rename.Split('=')[0], rename.Split('=')[1]);
        string common = Path.Combine(dir, id + ".Common.dll"), engineDll = Path.Combine(dir, id + ".Engine.dll");
        Compile(CommonSource.Replace("$VERSION$", "1.0.0.0"), common);
        Compile(engine.Replace("$VERSION$", "1.0.0.0"), engineDll, common);
        File.Move(common, Path.Combine(dir, GigabyteApi.CommonDll));
        File.Move(engineDll, Path.Combine(dir, GigabyteApi.EngineDll));
        return dir;
    }

    static void Compile(string source, string output, params string[] references)
    {
        using (var csc = new Microsoft.CSharp.CSharpCodeProvider())
        {
            var options = new CompilerParameters(new[] { "System.dll", "System.Core.dll" }.Concat(references).ToArray(), output);
            var result = csc.CompileAssemblyFromSource(options, source);
            if (result.Errors.HasErrors)
                throw new InvalidOperationException(string.Join(Environment.NewLine, result.Errors.Cast<CompilerError>().Select(e => e.ToString())));
        }
    }
}
