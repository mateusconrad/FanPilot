// Motherboard access through LibreHardwareMonitorLib, for boards without Gigabyte Control Center (ASUS, MSI,
// ASRock, ... and Gigabyte without GCC). Needs the PawnIO driver (https://pawnio.eu) and Administrator.
//
// LibreHardwareMonitor can only set a fixed duty cycle on a header (IControl.SetSoftware) or hand it back to the
// BIOS (SetDefault); the chip keeps no curve. So this class runs the curves: one thread owns the library, and once
// a second it refreshes the sensors, asks SoftwareCurveEngine for each managed header's duty (smoothed by the curve's
// response) and writes it.
// Safety, in order of what can go wrong:
//  - reference temperature missing or implausible -> that header goes back to the BIOS until it reads again;
//  - the control loop throws repeatedly -> every header goes back to the BIOS;
//  - FanPilot exits (window closed, Ctrl+C, logoff/shutdown, unhandled exception) -> Shutdown() returns all to BIOS.
// A forced kill cannot be intercepted: the header keeps its last duty. That is documented in the README.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LibreHardwareMonitor.Hardware;

sealed class LhmBoard : IBoard
{
    const int FailuresBeforeRelease = 5;

    sealed class Header
    {
        public ISensor Control;
        public ISensor Tach;    // may be null: not every control has a tachometer
        public string Title;
        public string Key;      // the control sensor's identifier, e.g. /lpc/nct6798d/0/control/1: stable across runs
        public FanCurve Curve;  // null = BIOS
        public bool OnBiosFallback;
    }

    readonly object _sync = new object();
    readonly ManualResetEvent _stop = new ManualResetEvent(false);
    readonly SoftwareCurveEngine _engine = new SoftwareCurveEngine();
    // When the curves were last applied: the engine's averages and ramps advance by the time that really passed.
    readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    double _lastApply;
    Computer _pc;
    List<ISensor> _temps = new List<ISensor>();
    List<Header> _headers = new List<Header>();
    List<SensorReading> _readings = new List<SensorReading>();
    List<float> _tempValues = new List<float>(), _rpmValues = new List<float>();
    Thread _loop;
    int _failures;
    bool _released;

    public string BackendName { get { return "LibreHardwareMonitor"; } }
    public bool HardwareCurves { get { return false; } }
    public string Notice { get; private set; }
    public bool FanControlSupported { get { lock (_sync) return _headers.Count > 0; } }
    public string[] TempInputTitles { get; private set; }
    public string BoardName { get; private set; }

    public LhmBoard()
    {
        TempInputTitles = new string[0];
        BoardName = "";
        try { Open(); }
        catch (Exception ex)
        {
            // Monitoring of the GPU (NVML) and the panel keep working; the board just shows the reason.
            Log.Warn("LibreHardwareMonitor não iniciou: " + ex);
            lock (_sync) Notice = "Falha ao iniciar o LibreHardwareMonitor: " + ex.Message;
            _pc = null;
        }
        _loop = new Thread(Loop) { IsBackground = true, Name = "lhm" };
        _loop.Start();
    }

    void Open()
    {
        bool pawnIo = PawnIoSetup.IsInstalled;
        _pc = new Computer { IsMotherboardEnabled = true, IsCpuEnabled = true };
        _pc.Open();
        var all = new List<IHardware>();
        foreach (var hw in _pc.Hardware) Collect(hw, all);
        foreach (var hw in all) hw.Update();
        var board = _pc.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Motherboard);
        BoardName = board != null ? board.Name : "";

        var superIo = all.Where(h => h.HardwareType == HardwareType.SuperIO || h.HardwareType == HardwareType.EmbeddedController).ToList();
        var cpuTemp = CpuTemperature(all);

        // Reference sensors for curves: the CPU first (what most curves follow), then the board's own inputs.
        var temps = new List<ISensor>();
        if (cpuTemp != null) temps.Add(cpuTemp);
        foreach (var hw in superIo) temps.AddRange(hw.Sensors.Where(s => s.SensorType == SensorType.Temperature).OrderBy(s => s.Index));
        var titles = new List<string>();
        foreach (var s in temps)
        {
            string t = s == cpuTemp ? "CPU" : s.Name;
            string unique = t;
            for (int n = 2; titles.Contains(unique); n++) unique = t + " (" + n + ")";
            titles.Add(unique);
        }

        var headers = new List<Header>();
        foreach (var hw in superIo)
        {
            var tachs = hw.Sensors.Where(s => s.SensorType == SensorType.Fan).ToList();
            foreach (var c in hw.Sensors.Where(s => s.SensorType == SensorType.Control && s.Control != null).OrderBy(s => s.Index))
                headers.Add(new Header { Control = c, Tach = tachs.FirstOrDefault(f => f.Index == c.Index), Title = c.Name, Key = c.Identifier.ToString() });
        }
        // A clean start: a FanPilot that was killed (or crashed) left its last duty on the headers. Everything goes
        // back to the BIOS, and FanManager then puts the saved curves back.
        foreach (var h in headers) TryDefault(h);

        lock (_sync)
        {
            _temps = temps;
            _headers = headers;
            TempInputTitles = titles.ToArray();
            if (!pawnIo)
                Notice = "Falta o driver PawnIO para ler os sensores e controlar os fans da placa-mãe. Feche e abra o FanPilot para instalá-lo (ou baixe em https://pawnio.eu).";
            else if (superIo.Count == 0)
                Notice = "O LibreHardwareMonitor não reconheceu o chip de sensores desta placa-mãe" + (BoardName.Length > 0 ? " (" + BoardName + ")" : "") +
                         ". Em notebooks isso é comum: o fabricante controla os fans pelo próprio firmware.";
            else if (headers.Count == 0)
                Notice = "O chip " + string.Join(", ", superIo.Select(h => h.Name)) + " foi reconhecido, mas sem controle de fans pelo LibreHardwareMonitor.";
            else
                Notice = null;
            Refresh(); // so the first curve applied already sees real temperatures
        }
        Log.Info("LibreHardwareMonitor: placa " + (BoardName.Length > 0 ? BoardName : "?") + "; chips: " +
                 (superIo.Count > 0 ? string.Join(", ", superIo.Select(h => h.Name)) : "nenhum") + "; " + headers.Count + " fans controláveis; " +
                 temps.Count + " sensores de temperatura" + (pawnIo ? "" : "; PawnIO NÃO instalado"));
    }

    static void Collect(IHardware hw, List<IHardware> into)
    {
        into.Add(hw);
        foreach (var sub in hw.SubHardware) Collect(sub, into);
    }

    // Intel: "CPU Package"; AMD: "Core (Tctl/Tdie)" or "Core (Tctl)"; otherwise the first temperature the CPU has.
    static ISensor CpuTemperature(List<IHardware> all)
    {
        var temps = all.Where(h => h.HardwareType == HardwareType.Cpu).SelectMany(h => h.Sensors).Where(s => s.SensorType == SensorType.Temperature).ToList();
        return temps.FirstOrDefault(s => s.Name == "CPU Package")
            ?? temps.FirstOrDefault(s => s.Name.StartsWith("Core (Tctl", StringComparison.Ordinal))
            ?? temps.FirstOrDefault(s => s.Name.IndexOf("Distance", StringComparison.OrdinalIgnoreCase) < 0);
    }

    void Loop()
    {
        while (!_stop.WaitOne(0))
        {
            try
            {
                lock (_sync)
                {
                    if (_released || _pc == null) return;
                    Refresh();
                    ApplyCurves();
                }
                if (_failures >= FailuresBeforeRelease) Log.Info("LibreHardwareMonitor voltou a responder: curvas do FanPilot reaplicadas.");
                _failures = 0;
            }
            catch (Exception ex)
            {
                // The log records the transitions (first failure, hand-back, recovery), not every failed second.
                if (++_failures == 1) Log.Warn("LibreHardwareMonitor: " + ex.Message);
                if (_failures == FailuresBeforeRelease)
                    Log.Error("LibreHardwareMonitor falhou " + _failures + " vezes seguidas: fans devolvidos à BIOS até ele voltar a responder. Último erro: " + ex.Message);
                if (_failures >= FailuresBeforeRelease) lock (_sync) foreach (var h in _headers) TryDefault(h);
            }
            _stop.WaitOne(1000);
        }
    }

    void Refresh()
    {
        var all = new List<IHardware>();
        foreach (var hw in _pc.Hardware) Collect(hw, all);
        foreach (var hw in all) hw.Update();

        var readings = new List<SensorReading>();
        var cpu = all.Where(h => h.HardwareType == HardwareType.Cpu).ToList();
        // The panel's tiles look for these three by title (see wwwroot/app.js renderTiles).
        var cpuTemp = _temps.Count > 0 && _temps[0].Hardware.HardwareType == HardwareType.Cpu ? _temps[0] : null;
        if (cpuTemp != null && cpuTemp.Value.HasValue) readings.Add(Reading("Temperature", "cpu-temp", "CPU", cpuTemp.Value.Value, "°C"));
        var package = cpu.SelectMany(h => h.Sensors).FirstOrDefault(s => s.SensorType == SensorType.Power && (s.Name == "CPU Package" || s.Name == "Package"));
        if (package != null && package.Value.HasValue) readings.Add(Reading("Power", "cpu-power", "CPU Package", package.Value.Value, "W"));
        var clocks = cpu.SelectMany(h => h.Sensors).Where(s => s.SensorType == SensorType.Clock && s.Name.IndexOf("Core", StringComparison.Ordinal) >= 0 && s.Value.HasValue).ToList();
        if (clocks.Count > 0) readings.Add(Reading("Frequency", "cpu-clock", "CPU", clocks.Max(s => s.Value.Value), "MHz"));

        foreach (var hw in all.Where(h => h.HardwareType == HardwareType.SuperIO || h.HardwareType == HardwareType.EmbeddedController))
        {
            foreach (var s in hw.Sensors)
            {
                if (!s.Value.HasValue) continue;
                switch (s.SensorType)
                {
                    case SensorType.Temperature: readings.Add(Reading("Temperature", s.Identifier.ToString(), s.Name, s.Value.Value, "°C")); break;
                    case SensorType.Fan: readings.Add(Reading("Fan", s.Identifier.ToString(), s.Name, s.Value.Value, "RPM")); break;
                    case SensorType.Voltage: readings.Add(Reading("Voltage", s.Identifier.ToString(), s.Name, s.Value.Value, "V")); break;
                }
            }
        }
        _readings = readings;
        _tempValues = _temps.Select(s => s.Value.HasValue ? s.Value.Value : float.NaN).ToList();
        _rpmValues = _headers.Select(h => h.Tach != null && h.Tach.Value.HasValue ? h.Tach.Value.Value : 0f).ToList();
    }

    static SensorReading Reading(string type, string id, string title, double value, string unit)
    {
        return new SensorReading { Id = type.ToLowerInvariant() + "-" + id, Type = type, Title = title, Value = Math.Round(value, 3), Unit = unit };
    }

    // Re-asserted every second, which also undoes a BIOS/vendor-tool takeover (e.g. after sleep).
    void ApplyCurves()
    {
        double now = _clock.Elapsed.TotalSeconds, dt = now - _lastApply;
        _lastApply = now;
        for (int i = 0; i < _headers.Count; i++)
        {
            var h = _headers[i];
            if (h.Curve == null) continue;
            double t = h.Curve.TempInput < _tempValues.Count ? _tempValues[h.Curve.TempInput] : double.NaN;
            double? duty = _engine.Next(i, h.Curve, t, dt);
            if (duty == null)
            {
                if (!h.OnBiosFallback) Log.Warn("Fan " + h.Title + ": sem leitura válida do sensor de referência; devolvido à BIOS até voltar.");
                h.OnBiosFallback = true;
                TryDefault(h);
                continue;
            }
            if (h.OnBiosFallback) Log.Info("Fan " + h.Title + ": sensor de referência voltou; curva do FanPilot reaplicada.");
            h.OnBiosFallback = false;
            float percent = (float)Math.Max(h.Control.Control.MinSoftwareValue, Math.Min(h.Control.Control.MaxSoftwareValue, duty.Value));
            h.Control.Control.SetSoftware(percent);
        }
    }

    static void TryDefault(Header h)
    {
        try { h.Control.Control.SetDefault(); } catch (Exception ex) { Log.Warn("Fan " + h.Title + ": SetDefault falhou: " + ex.Message); }
    }

    public int CountFans() { lock (_sync) return _headers.Count; }

    public List<SensorReading> ReadSensors() { lock (_sync) return _readings.ToList(); }

    public void ReadFanTach(out List<float> temps, out List<float> rpms)
    {
        lock (_sync) { temps = _tempValues.ToList(); rpms = _rpmValues.ToList(); }
    }

    public FanCurve GetCurve(int index)
    {
        lock (_sync)
        {
            if (index < 0 || index >= _headers.Count) throw new ArgumentException("Fan inexistente.");
            var h = _headers[index];
            if (h.Curve == null)
                return new FanCurve { Index = index, Key = h.Key, Title = h.Title, Mode = FanCurve.BiosMode, TempInput = 0, DeltaT = 1 };
            var c = FanCurve.FromDictionary(h.Curve.ToDictionary());
            c.Title = h.Title;
            c.Key = h.Key;
            return c;
        }
    }

    public List<FanCurve> GetCurves()
    {
        int n = CountFans();
        var list = new List<FanCurve>();
        for (int i = 0; i < n; i++) list.Add(GetCurve(i));
        return list;
    }

    public FanCurve SetCurve(FanCurve curve)
    {
        lock (_sync)
        {
            if (_released) throw new InvalidOperationException("FanPilot está encerrando.");
            if (curve.Index < 0 || curve.Index >= _headers.Count) throw new ArgumentException("Fan inexistente.");
            var h = _headers[curve.Index];
            _engine.Reset(curve.Index);
            if (curve.IsBios || curve.Points.Count == 0)
            {
                h.Curve = null;
                h.OnBiosFallback = false;
                TryDefault(h);
            }
            else
            {
                if (curve.Points.Count < 3 || Math.Round(curve.Points[curve.Points.Count - 1][1]) != 255)
                    throw new ArgumentException("A curva precisa de >= 3 pontos e o último ponto em 255.");
                if (curve.TempInput < 0 || curve.TempInput >= _temps.Count) throw new ArgumentException("Sensor de temperatura inválido.");
                h.Curve = FanCurve.FromDictionary(curve.ToDictionary());
                h.Curve.Index = curve.Index;
                ApplyCurves(); // take effect now, not on the next tick
            }
        }
        return GetCurve(curve.Index);
    }

    // Every exit path FanPilot can see ends here: stop the loop and give each header back to the BIOS.
    public void Shutdown()
    {
        lock (_sync)
        {
            if (_released) return;
            _released = true;
            _stop.Set();
            int handed = 0;
            foreach (var h in _headers) if (h.Curve != null || h.OnBiosFallback) { TryDefault(h); handed++; }
            try { if (_pc != null) _pc.Close(); } catch { }
            if (handed > 0) Log.Info("LibreHardwareMonitor: " + handed + " fan(s) devolvido(s) à BIOS.");
        }
    }
}
