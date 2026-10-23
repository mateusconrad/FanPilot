// Client for EasyTuneEngineService (ipc://GigabyteEngine/EnvironmentControl/...), using GCC's own client modules.
//  - HardwareMonitor                       -> all motherboard sensors (temps, fans, voltages, clocks, CPU power)
//  - SmartGuardianFanControlCoordinateSystem -> fan curves executed by the ITE IT86xx Super I/O ("3rd generation" path)
// The service's IPC port only admits Administrators.
using System;
using System.Collections.Generic;
using System.Linq;
using Gigabyte.EnvironmentControl.Common.HardwareMonitor;
using Gigabyte.EnvironmentControl.Common.CoolingDevice.Fan;
using Gigabyte.EnvironmentControl.Common.CoolingDevice.Fan.CoordinateSystem;
using Gigabyte.Engine.EnvironmentControl.HardwareMonitor;
using Gigabyte.Engine.EnvironmentControl.CoolingDevice.Fan;

sealed class GigabyteBridge : IBoard
{
    readonly object _sync = new object();
    HardwareMonitorControlModule _hw;
    SmartGuardianFanControlCoordinateSystemModule _fan;
    bool _supportLogged;

    public int FanControlCount { get; private set; }
    public string[] ControlTitles { get; private set; }
    public string[] TempInputTitles { get; private set; }
    public string LastError { get; private set; }
    public bool FanControlSupported { get; private set; }

    public GigabyteBridge()
    {
        ControlTitles = new string[0];
        TempInputTitles = new string[0];
    }

    public string BackendName { get { return "Gigabyte Control Center"; } }
    public bool HardwareCurves { get { return true; } }
    public string Notice { get { return FanControlSupported ? null : "Esta placa-mãe não expõe o controle de fans de 3ª geração do GCC."; } }
    // Curves live in the chip, nothing to hand back.
    public void Shutdown() { }

    // Every call goes through here: serialized (the modules are not thread-safe) and retried, because the
    // first call on a fresh IPC connection reliably fails with "pipe is being closed" and a retry succeeds.
    T Invoke<T>(Func<T> op)
    {
        lock (_sync)
        {
            Exception last = null;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    EnsureConnected();
                    T result = op();
                    LastError = null;
                    return result;
                }
                catch (ArgumentException) { throw; }
                catch (Exception ex)
                {
                    last = ex;
                    if (attempt == 1) Reset();
                }
            }
            LastError = last.Message;
            throw new InvalidOperationException("EasyTuneEngineService indisponível: " + last.Message, last);
        }
    }

    void EnsureConnected()
    {
        if (_hw != null) return;
        _hw = new HardwareMonitorControlModule();
        _fan = new SmartGuardianFanControlCoordinateSystemModule();
        try { var warmup = _fan.IsSupported; } catch (System.Runtime.Remoting.RemotingException) { }

        FanControlSupported = _fan.IsSupported;
        if (!FanControlSupported)
        {
            FanControlCount = 0;
            if (!_supportLogged) Log.Warn("Esta placa-mãe não expõe o controle de fans de 3ª geração do GCC; só monitoramento.");
            _supportLogged = true;
            return;
        }
        var info = _fan.HardwareInfo;
        FanControlCount = info.FanControlCount;
        var controls = new string[info.FanControlCount];
        for (int i = 0; i < controls.Length; i++) { string t; _fan.GetSensorTitle(SensorTypes.Control, i, out t); controls[i] = t; }
        var temps = new string[info.TemperatureInputCount];
        for (int i = 0; i < temps.Length; i++) { string t; _fan.GetSensorTitle(SensorTypes.Temperature, i, out t); temps[i] = t; }
        ControlTitles = controls;
        TempInputTitles = temps;
    }

    void Reset()
    {
        try { if (_hw != null) _hw.Dispose(); } catch { }
        try { if (_fan != null) _fan.Dispose(); } catch { }
        _hw = null;
        _fan = null;
    }

    public List<SensorReading> ReadSensors()
    {
        return Invoke(() =>
        {
            var data = new HardwareMonitoredDataCollection();
            _hw.GetCurrentMonitoredData(ref data);
            return data.Select(d => new SensorReading
            {
                Id = d.Type.ToString().ToLowerInvariant() + "-" + d.DeviceUUID,
                Type = d.Type.ToString(),
                Title = d.Title,
                Value = Math.Round(d.Value, 3),
                Unit = UnitFor(d)
            }).ToList();
        });
    }

    static string UnitFor(HardwareMonitoredData d)
    {
        switch (d.Type)
        {
            case SensorTypes.Temperature: return "°C";
            case SensorTypes.Fan: return "RPM";
            case SensorTypes.Voltage: return "V";
            case SensorTypes.Power: return "W";
            case SensorTypes.Frequency: return string.IsNullOrEmpty(d.Unit) ? "" : "MHz";
            default: return d.Unit ?? "";
        }
    }

    // Index-aligned with fan controls / temperature inputs.
    public void ReadFanTach(out List<float> temps, out List<float> rpms)
    {
        var t = new List<float>();
        var f = new List<float>();
        Invoke(() => { _fan.GetHardwareMonitorDatas(ref t, ref f); return 0; });
        temps = t;
        rpms = f;
    }

    public FanCurve GetCurve(int index)
    {
        return Invoke(() =>
        {
            var set = new SmartFanDataPointSet();
            _fan.Get(index, ref set);
            int tempInput;
            _fan.GetTemperatureInputSelection(index, out tempInput);
            var c = new FanCurve
            {
                Index = index,
                Title = index < ControlTitles.Length ? ControlTitles[index] : "Fan " + index,
                TempInput = tempInput,
                Mode = set.Mode.ToString(),
                DeltaT = set.DeltaTemperature
            };
            if (set.DataPoints != null)
                foreach (var p in set.DataPoints) c.Points.Add(new[] { p.X, p.Y });
            return c;
        });
    }

    // Connects first, so the count is valid even before any other call.
    public int CountFans() { return Invoke(() => FanControlCount); }

    public List<FanCurve> GetCurves()
    {
        var list = new List<FanCurve>();
        int n = CountFans();
        for (int i = 0; i < n; i++) list.Add(GetCurve(i));
        return list;
    }

    // Writes a curve and returns what the hardware reports afterwards.
    // Hardware constraint (SmartGuardianFanConfigConverter.ToPhysicalLayerFormat): >= 3 points and the last point
    // must be PWM 255, otherwise the service silently writes an all-zero config (which the chip treats as full speed).
    public FanCurve SetCurve(FanCurve curve)
    {
        if (curve.Points.Count < 3 || Math.Round(curve.Points[curve.Points.Count - 1][1]) != 255)
            throw new ArgumentException("A curva precisa de >= 3 pontos e o último ponto em 255.");
        FanConfigMode mode;
        if (!Enum.TryParse(curve.Mode, out mode) || (mode != FanConfigMode.SmartFanMode && mode != FanConfigMode.StairStepMode))
            mode = FanConfigMode.SmartFanMode;

        Invoke(() =>
        {
            int currentInput;
            _fan.GetTemperatureInputSelection(curve.Index, out currentInput);
            if (currentInput != curve.TempInput && !_fan.SetTemperatureInputSelection(curve.Index, curve.TempInput))
                throw new InvalidOperationException("O serviço recusou a troca do sensor de temperatura.");
            var set = new SmartFanDataPointSet
            {
                Mode = mode,
                DeltaTemperature = curve.DeltaT,
                FanSpeed = 0,
                DataPoints = curve.Points.Select(p => new SmartFanDataPoint { X = p[0], Y = p[1] }).ToList()
            };
            _fan.Set(curve.Index, set);
            return 0;
        });
        return GetCurve(curve.Index);
    }
}
