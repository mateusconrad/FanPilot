// Gigabyte Control Center's client modules, reached by reflection: FanPilot is built without them (any machine, the
// CI) and loads them at runtime from the GCC install. Load() resolves every type and member GigabyteBridge needs, with
// the exact signatures, before anything uses them: a GCC update that changes one is reported by name ("falta …") and
// FanPilot falls back to LibreHardwareMonitor, instead of failing somewhere in the middle.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;

sealed class GigabyteApi
{
    public const string EngineDll = "Gigabyte.Engine.EnvironmentControl.dll", CommonDll = "Gigabyte.EnvironmentControl.Common.dll";
    const string Common = "Gigabyte.EnvironmentControl.Common.", Engine = "Gigabyte.Engine.EnvironmentControl.";

    public sealed class Reading
    {
        public string Type, Title, Unit;
        public int DeviceUuid;
        public double Value;
    }

    public sealed class CurveData
    {
        public string Mode;
        public int DeltaT;
        public List<double[]> Points = new List<double[]>();
    }

    readonly List<string> _missing = new List<string>();
    Type _sensorTypes, _fanConfigMode, _collection, _dataPoint, _dataPointSet, _hwModule, _fanModule;
    ConstructorInfo _newHw, _newFan, _newCollection, _newPoint, _newSet;
    MethodInfo _getMonitored, _getTitle, _getMonitorDatas, _get, _set, _getInput, _setInput, _hwDispose, _fanDispose;
    PropertyInfo _isSupported, _hardwareInfo, _fanCount, _tempCount, _dType, _dTitle, _dUuid, _dValue, _dUnit,
                 _setMode, _setDelta, _setSpeed, _setPoints, _pointX, _pointY;

    public string Version { get; private set; }
    public bool Compatible { get { return _missing.Count == 0; } }
    // What is missing, for the panel and the log: "falta SmartGuardian…Module.SetTemperatureInputSelection(Int32, Int32)".
    public string Problem { get { return Compatible ? null : "falta " + string.Join(", ", _missing); } }

    GigabyteApi() { }

    public static GigabyteApi Load(string dir)
    {
        var api = new GigabyteApi();
        string engine = Path.Combine(dir, EngineDll), common = Path.Combine(dir, CommonDll);
        api.Version = File.Exists(engine) ? FileVersionInfo.GetVersionInfo(engine).FileVersion ?? "?" : "?";
        Assembly engineAsm = null, commonAsm = null;
        try
        {
            commonAsm = Assembly.LoadFrom(common);
            engineAsm = Assembly.LoadFrom(engine);
        }
        catch (Exception ex) when (ex is IOException || ex is BadImageFormatException || ex is FileLoadException || ex is UnauthorizedAccessException)
        {
            api._missing.Add((commonAsm == null ? CommonDll : EngineDll) + " (" + ex.Message + ")");
            return api;
        }
        api.Bind(engineAsm, commonAsm);
        return api;
    }

    void Bind(Assembly engine, Assembly common)
    {
        _sensorTypes = TypeIn(common, Common + "HardwareMonitor.SensorTypes");
        _fanConfigMode = TypeIn(common, Common + "CoolingDevice.Fan.FanConfigMode");
        _collection = TypeIn(common, Common + "HardwareMonitor.HardwareMonitoredDataCollection");
        var data = TypeIn(common, Common + "HardwareMonitor.HardwareMonitoredData");
        _dataPoint = TypeIn(common, Common + "CoolingDevice.Fan.CoordinateSystem.SmartFanDataPoint");
        _dataPointSet = TypeIn(common, Common + "CoolingDevice.Fan.CoordinateSystem.SmartFanDataPointSet");
        _hwModule = TypeIn(engine, Engine + "HardwareMonitor.HardwareMonitorControlModule");
        _fanModule = TypeIn(engine, Engine + "CoolingDevice.Fan.SmartGuardianFanControlCoordinateSystemModule");
        if (!Compatible) return; // members cannot be looked up on types that are not there

        foreach (var name in new[] { "Temperature", "Fan", "Voltage", "Power", "Frequency", "Control" }) EnumValue(_sensorTypes, name);
        foreach (var name in new[] { "SmartFanMode", "StairStepMode" }) EnumValue(_fanConfigMode, name);

        _newHw = Ctor(_hwModule);
        _newFan = Ctor(_fanModule);
        _newCollection = Ctor(_collection);
        _newPoint = Ctor(_dataPoint);
        _newSet = Ctor(_dataPointSet);
        _getMonitored = Method(_hwModule, "GetCurrentMonitoredData", _collection.MakeByRefType());
        _getTitle = Method(_fanModule, "GetSensorTitle", _sensorTypes, typeof(int), typeof(string).MakeByRefType());
        _getMonitorDatas = Method(_fanModule, "GetHardwareMonitorDatas", typeof(List<float>).MakeByRefType(), typeof(List<float>).MakeByRefType());
        _get = Method(_fanModule, "Get", typeof(int), _dataPointSet.MakeByRefType());
        _set = Method(_fanModule, "Set", typeof(int), _dataPointSet);
        _getInput = Method(_fanModule, "GetTemperatureInputSelection", typeof(int), typeof(int).MakeByRefType());
        _setInput = Method(_fanModule, "SetTemperatureInputSelection", typeof(int), typeof(int));
        _isSupported = Property(_fanModule, "IsSupported");
        _hardwareInfo = Property(_fanModule, "HardwareInfo");
        if (_hardwareInfo != null)
        {
            _fanCount = Property(_hardwareInfo.PropertyType, "FanControlCount");
            _tempCount = Property(_hardwareInfo.PropertyType, "TemperatureInputCount");
        }
        _dType = Property(data, "Type");
        _dTitle = Property(data, "Title");
        _dUuid = Property(data, "DeviceUUID");
        _dValue = Property(data, "Value");
        _dUnit = Property(data, "Unit");
        _setMode = Property(_dataPointSet, "Mode");
        _setDelta = Property(_dataPointSet, "DeltaTemperature");
        _setSpeed = Property(_dataPointSet, "FanSpeed");
        _setPoints = Property(_dataPointSet, "DataPoints");
        _pointX = Property(_dataPoint, "X");
        _pointY = Property(_dataPoint, "Y");
        _hwDispose = Method(_hwModule, "Dispose");
        _fanDispose = Method(_fanModule, "Dispose");
    }

    Type TypeIn(Assembly asm, string fullName)
    {
        var t = asm.GetType(fullName, false);
        if (t == null) _missing.Add(fullName.Substring(fullName.LastIndexOf('.') + 1));
        return t;
    }

    void EnumValue(Type enumType, string name)
    {
        if (!Enum.GetNames(enumType).Contains(name)) _missing.Add(enumType.Name + "." + name);
    }

    ConstructorInfo Ctor(Type t)
    {
        var c = t.GetConstructor(Type.EmptyTypes);
        if (c == null) _missing.Add(t.Name + "()");
        return c;
    }

    MethodInfo Method(Type t, string name, params Type[] parameters)
    {
        var m = t.GetMethod(name, BindingFlags.Public | BindingFlags.Instance, null, parameters, null);
        if (m == null) _missing.Add(t.Name + "." + name + "(" + string.Join(", ", parameters.Select(p => p.IsByRef ? "ref " + p.GetElementType().Name : p.Name)) + ")");
        return m;
    }

    PropertyInfo Property(Type t, string name)
    {
        var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        if (p == null) _missing.Add(t.Name + "." + name);
        return p;
    }

    // Exceptions from Gigabyte's code come out as they are (RemotingException…), not wrapped by reflection.
    static object Call(MethodBase method, object target, object[] args)
    {
        try { return method is ConstructorInfo c ? c.Invoke(args) : method.Invoke(target, args); }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    static object Get(PropertyInfo p, object target)
    {
        try { return p.GetValue(target, null); }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    object Sensor(string name) { return Enum.Parse(_sensorTypes, name); }

    public object NewHardwareMonitor() { return Call(_newHw, null, new object[0]); }
    public object NewFanModule() { return Call(_newFan, null, new object[0]); }

    public void Dispose(object module)
    {
        if (module == null) return;
        Call(_hwModule.IsInstanceOfType(module) ? _hwDispose : _fanDispose, module, new object[0]);
    }

    public List<Reading> ReadMonitoredData(object hw)
    {
        var args = new[] { Call(_newCollection, null, new object[0]) };
        Call(_getMonitored, hw, args);
        return ((IEnumerable)args[0]).Cast<object>().Select(d => new Reading
        {
            Type = Get(_dType, d).ToString(),
            Title = (string)Get(_dTitle, d),
            DeviceUuid = Convert.ToInt32(Get(_dUuid, d)),
            Value = Convert.ToDouble(Get(_dValue, d)),
            Unit = (string)Get(_dUnit, d)
        }).ToList();
    }

    public bool IsSupported(object fan) { return (bool)Get(_isSupported, fan); }
    public int FanControlCount(object fan) { return Convert.ToInt32(Get(_fanCount, Get(_hardwareInfo, fan))); }
    public int TemperatureInputCount(object fan) { return Convert.ToInt32(Get(_tempCount, Get(_hardwareInfo, fan))); }

    // sensorType: "Control" or "Temperature".
    public string SensorTitle(object fan, string sensorType, int index)
    {
        var args = new object[] { Sensor(sensorType), index, null };
        Call(_getTitle, fan, args);
        return (string)args[2];
    }

    public void ReadHardwareMonitorDatas(object fan, out List<float> temps, out List<float> rpms)
    {
        var args = new object[] { new List<float>(), new List<float>() };
        Call(_getMonitorDatas, fan, args);
        temps = (List<float>)args[0];
        rpms = (List<float>)args[1];
    }

    public CurveData GetCurve(object fan, int index)
    {
        var args = new[] { index, Call(_newSet, null, new object[0]) };
        Call(_get, fan, args);
        var set = args[1];
        var curve = new CurveData { Mode = Get(_setMode, set).ToString(), DeltaT = Convert.ToInt32(Get(_setDelta, set)) };
        var points = Get(_setPoints, set) as IEnumerable;
        if (points != null)
            foreach (var p in points) curve.Points.Add(new[] { Convert.ToDouble(Get(_pointX, p)), Convert.ToDouble(Get(_pointY, p)) });
        return curve;
    }

    // mode: "SmartFanMode" or "StairStepMode".
    public void SetCurve(object fan, int index, string mode, int deltaT, IEnumerable<double[]> points)
    {
        var set = Call(_newSet, null, new object[0]);
        _setMode.SetValue(set, Enum.Parse(_fanConfigMode, mode), null);
        _setDelta.SetValue(set, deltaT, null);
        _setSpeed.SetValue(set, 0, null);
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(_dataPoint));
        foreach (var p in points)
        {
            var point = Call(_newPoint, null, new object[0]);
            _pointX.SetValue(point, p[0], null);
            _pointY.SetValue(point, p[1], null);
            list.Add(point);
        }
        _setPoints.SetValue(set, list, null);
        Call(_set, fan, new[] { index, set });
    }

    public int GetTemperatureInput(object fan, int index)
    {
        var args = new object[] { index, 0 };
        Call(_getInput, fan, args);
        return (int)args[1];
    }

    public bool SetTemperatureInput(object fan, int index, int input) { return (bool)Call(_setInput, fan, new object[] { index, input }); }
}
