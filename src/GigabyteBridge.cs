// Client for EasyTuneEngineService (ipc://GigabyteEngine/EnvironmentControl/...), using GCC's own client modules,
// reached by reflection through GigabyteApi (FanPilot is built without them):
//  - HardwareMonitor                       -> all motherboard sensors (temps, fans, voltages, clocks, CPU power)
//  - SmartGuardianFanControlCoordinateSystem -> fan curves executed by the ITE IT86xx Super I/O ("3rd generation" path)
// The service's IPC port only admits Administrators.
using System;
using System.Collections.Generic;
using System.Linq;

sealed class GigabyteBridge : IBoard
{
    readonly GigabyteApi _api;
    readonly object _sync = new object();
    object _hw, _fan;
    bool _supportLogged;

    public int FanControlCount { get; private set; }
    public string[] ControlTitles { get; private set; }
    public string[] TempInputTitles { get; private set; }
    public string LastError { get; private set; }
    public bool FanControlSupported { get; private set; }

    public GigabyteBridge(GigabyteApi api)
    {
        _api = api;
        ControlTitles = new string[0];
        TempInputTitles = new string[0];
    }

    public string BackendName { get { return "Gigabyte Control Center"; } }
    public bool HardwareCurves { get { return true; } }
    public string Notice { get { return FanControlSupported ? null : "Esta placa-mãe não expõe o controle de fans de 3ª geração do GCC."; } }

    // The curves run in the chip: there is nothing FanPilot hands back on its own.
    public List<string> ControlProblems() { return new List<string>(); }
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
        _hw = _api.NewHardwareMonitor();
        _fan = _api.NewFanModule();
        try { var warmup = _api.IsSupported(_fan); } catch (System.Runtime.Remoting.RemotingException) { }

        FanControlSupported = _api.IsSupported(_fan);
        if (!FanControlSupported)
        {
            FanControlCount = 0;
            if (!_supportLogged) Log.Warn("Esta placa-mãe não expõe o controle de fans de 3ª geração do GCC; só monitoramento.");
            _supportLogged = true;
            return;
        }
        FanControlCount = _api.FanControlCount(_fan);
        ControlTitles = Enumerable.Range(0, FanControlCount).Select(i => _api.SensorTitle(_fan, "Control", i)).ToArray();
        TempInputTitles = Enumerable.Range(0, _api.TemperatureInputCount(_fan)).Select(i => _api.SensorTitle(_fan, "Temperature", i)).ToArray();
    }

    void Reset()
    {
        try { _api.Dispose(_hw); } catch { }
        try { _api.Dispose(_fan); } catch { }
        _hw = null;
        _fan = null;
    }

    public List<SensorReading> ReadSensors()
    {
        return Invoke(() => _api.ReadMonitoredData(_hw).Select(d => new SensorReading
        {
            Id = d.Type.ToLowerInvariant() + "-" + d.DeviceUuid,
            Type = d.Type,
            Title = d.Title,
            Value = Math.Round(d.Value, 3),
            Unit = UnitFor(d)
        }).ToList());
    }

    static string UnitFor(GigabyteApi.Reading d)
    {
        switch (d.Type)
        {
            case "Temperature": return "°C";
            case "Fan": return "RPM";
            case "Voltage": return "V";
            case "Power": return "W";
            case "Frequency": return string.IsNullOrEmpty(d.Unit) ? "" : "MHz";
            default: return d.Unit ?? "";
        }
    }

    // Index-aligned with fan controls / temperature inputs.
    public void ReadFanTach(out List<float> temps, out List<float> rpms)
    {
        List<float> t = null, f = null;
        Invoke(() => { _api.ReadHardwareMonitorDatas(_fan, out t, out f); return 0; });
        temps = t;
        rpms = f;
    }

    public FanCurve GetCurve(int index)
    {
        return Invoke(() =>
        {
            var set = _api.GetCurve(_fan, index);
            string title = index < ControlTitles.Length ? ControlTitles[index] : "Fan " + index;
            var c = new FanCurve
            {
                Index = index,
                // GCC exposes nothing steadier than position and name: a GCC update that renames or reorders the
                // headers makes the saved configuration an orphan instead of landing on another fan.
                Key = "gcc:" + index + ":" + title,
                Title = title,
                TempInput = _api.GetTemperatureInput(_fan, index),
                Mode = set.Mode,
                DeltaT = set.DeltaT
            };
            c.Points.AddRange(set.Points);
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
        string mode = curve.Mode == "StairStepMode" ? "StairStepMode" : "SmartFanMode";

        Invoke(() =>
        {
            if (_api.GetTemperatureInput(_fan, curve.Index) != curve.TempInput && !_api.SetTemperatureInput(_fan, curve.Index, curve.TempInput))
                throw new InvalidOperationException("O serviço recusou a troca do sensor de temperatura.");
            _api.SetCurve(_fan, curve.Index, mode, curve.DeltaT, curve.Points);
            return 0;
        });
        return GetCurve(curve.Index);
    }
}
