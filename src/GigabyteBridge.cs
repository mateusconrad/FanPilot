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
    bool _supportLogged, _checked;
    DateTime _checkedAt;

    // How often a "not supported" is asked again (EnsureConnected); the tests ask every time.
    internal TimeSpan SupportRecheck = TimeSpan.FromSeconds(15);

    public int FanControlCount { get; private set; }
    public string[] ControlTitles { get; private set; }
    public string[] TempInputTitles { get; private set; }
    public string LastError { get; private set; }
    public bool FanControlSupported { get; private set; }

    // The cores' temperature next to GCC's socket sensor (item 19); a hint instead when PawnIO is missing.
    readonly CpuPackageMonitor _cpu;

    public GigabyteBridge(GigabyteApi api) : this(api, CpuPackageMonitor.Open()) { }

    // cpu: the tests pass one without a reading.
    internal GigabyteBridge(GigabyteApi api, CpuPackageMonitor cpu)
    {
        _api = api;
        _cpu = cpu;
        ControlTitles = new string[0];
        TempInputTitles = new string[0];
    }

    public string BackendName { get { return "Gigabyte Control Center"; } }
    public bool HardwareCurves { get { return true; } }
    // Only once the support check has answered: before it, "no fan control" would only mean "not asked yet".
    public Msg Notice { get { return _checked && !FanControlSupported ? new Msg("board.noGen3") : null; } }

    // The curves run in the chip: there is nothing FanPilot hands back on its own.
    public List<Msg> ControlProblems() { return new List<Msg>(); }
    // Curves live in the chip, nothing to hand back.
    public void Shutdown() { _cpu.Close(); }

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
            throw new Unavailable(new Msg("board.serviceDown", "service", "EasyTuneEngineService", "detail", last.Message), last);
        }
    }

    // A connection is kept only once the fan module has answered the support check: at boot, FanPilot's service can
    // start before EasyTuneEngineService listens, and a connection kept from such a try made the next call skip the
    // check and report no fans for good. A "not supported" is asked again every SupportRecheck, for the same reason.
    void EnsureConnected()
    {
        if (_hw != null)
        {
            if (!FanControlSupported && DateTime.UtcNow - _checkedAt >= SupportRecheck) CheckSupport(_fan);
            return;
        }
        object hw = null, fan = null;
        try
        {
            hw = _api.NewHardwareMonitor();
            fan = _api.NewFanModule();
            try { var warmup = _api.IsSupported(fan); } catch (System.Runtime.Remoting.RemotingException) { }
            CheckSupport(fan);
        }
        catch
        {
            try { _api.Dispose(hw); } catch { }
            try { _api.Dispose(fan); } catch { }
            throw;
        }
        _hw = hw;
        _fan = fan;
    }

    void CheckSupport(object fan)
    {
        _checkedAt = DateTime.UtcNow;
        bool supported = _api.IsSupported(fan);
        _checked = true;
        if (!supported)
        {
            FanControlSupported = false;
            FanControlCount = 0;
            if (!_supportLogged) Log.Warn("This motherboard doesn't expose the GCC's third-generation fan control; monitoring only.");
            _supportLogged = true;
            return;
        }
        int count = _api.FanControlCount(fan);
        ControlTitles = Enumerable.Range(0, count).Select(i => _api.SensorTitle(fan, "Control", i)).ToArray();
        TempInputTitles = Enumerable.Range(0, _api.TemperatureInputCount(fan)).Select(i => _api.SensorTitle(fan, "Temperature", i)).ToArray();
        FanControlCount = count;
        if (_supportLogged) Log.Info("The GCC's third-generation fan control answers now: " + count + " fan(s).");
        _supportLogged = false;
        FanControlSupported = true;
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
        var readings = Invoke(() => _api.ReadMonitoredData(_hw).Select(d => new SensorReading
        {
            Id = d.Type.ToLowerInvariant() + "-" + d.DeviceUuid,
            Type = d.Type,
            Title = d.Title,
            Value = Math.Round(d.Value, 3),
            Unit = UnitFor(d)
        }).ToList());
        CpuRoles.Assign(readings, _cpu.Read(), true, _cpu.Hint);
        return readings;
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
            throw new UserError("err.curve.shape");
        string mode = curve.Mode == "StairStepMode" ? "StairStepMode" : "SmartFanMode";

        Invoke(() =>
        {
            if (_api.GetTemperatureInput(_fan, curve.Index) != curve.TempInput && !_api.SetTemperatureInput(_fan, curve.Index, curve.TempInput))
                throw new Unavailable("board.tempInputRefused");
            _api.SetCurve(_fan, curve.Index, mode, curve.DeltaT, curve.Points);
            return 0;
        });
        return GetCurve(curve.Index);
    }
}
