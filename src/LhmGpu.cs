// GPU through LibreHardwareMonitorLib, for when NVML is not there: AMD (ADL), Intel Arc and NVIDIA without
// nvml.dll. Reports the same fields as NvmlGpu so the panel doesn't care which one is in use. Doesn't need PawnIO:
// GPU access goes through the vendor's own driver.
using System;
using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;

sealed class LhmGpu : GpuMonitor
{
    Computer _pc;
    IHardware _gpu;
    List<ISensor> _controls = new List<ISensor>();
    string _error;

    public bool Ok { get { return _gpu != null; } }
    public string Error { get { return _error; } }
    public string Name { get { return _gpu != null ? _gpu.Name : ""; } }

    public LhmGpu(int safetyTemp, string nvmlError) : base(safetyTemp)
    {
        try
        {
            _pc = new Computer { IsGpuEnabled = true };
            _pc.Open();
            var gpus = _pc.Hardware.Where(h => h.HardwareType == HardwareType.GpuNvidia || h.HardwareType == HardwareType.GpuAmd || h.HardwareType == HardwareType.GpuIntel).ToList();
            foreach (var g in gpus) g.Update();
            // The dedicated card: the one whose fan can be controlled, then the one with the most video memory
            // (an iGPU next to it has little or none).
            _gpu = gpus.OrderByDescending(g => g.Sensors.Count(IsFanControl))
                       .ThenByDescending(g => Value(Find(g, SensorType.SmallData, "GPU Memory Total", "D3D Dedicated Memory Total")) ?? 0)
                       .FirstOrDefault();
            if (_gpu == null) throw new InvalidOperationException("nenhuma GPU encontrada" + (nvmlError != null ? " (NVML: " + nvmlError + ")" : ""));
            _controls = _gpu.Sensors.Where(IsFanControl).OrderBy(s => s.Index).ToList();
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            _gpu = null;
            try { if (_pc != null) _pc.Close(); } catch { }
            _pc = null;
        }
    }

    static bool IsFanControl(ISensor s) { return s.SensorType == SensorType.Control && s.Control != null; }

    // First sensor of that type with one of the names, in order of preference.
    static ISensor Find(IHardware hw, SensorType type, params string[] names)
    {
        foreach (var n in names)
        {
            var s = hw.Sensors.FirstOrDefault(x => x.SensorType == type && x.Name == n && x.Value.HasValue);
            if (s != null) return s;
        }
        return null;
    }

    static float? Value(ISensor s) { return s != null ? s.Value : null; }

    public override Dictionary<string, object> Read()
    {
        lock (Sync)
        {
            var r = new Dictionary<string, object>();
            r["ok"] = Ok;
            if (!Ok) { r["error"] = _error; return r; }
            _gpu.Update();
            r["name"] = _gpu.Name;
            var temp = Find(_gpu, SensorType.Temperature, "GPU Core") ?? _gpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Value.HasValue);
            if (temp != null) r["temp"] = (int)Math.Round(temp.Value.Value);
            r["fans"] = _controls.Where(c => c.Value.HasValue).Select(c => (int)Math.Round(c.Value.Value)).ToList();
            var power = Find(_gpu, SensorType.Power, "GPU Package", "GPU PPT", "GPU Power", "GPU Board Power") ?? _gpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Power && s.Value.HasValue);
            if (power != null) r["power"] = Math.Round(power.Value.Value, 1);
            var load = Find(_gpu, SensorType.Load, "GPU Core");
            if (load != null) r["util"] = (int)Math.Round(load.Value.Value);
            var clock = Find(_gpu, SensorType.Clock, "GPU Core");
            if (clock != null) r["clock"] = (int)Math.Round(clock.Value.Value);
            var memClock = Find(_gpu, SensorType.Clock, "GPU Memory");
            if (memClock != null) r["memClock"] = (int)Math.Round(memClock.Value.Value);
            var used = Find(_gpu, SensorType.SmallData, "GPU Memory Used", "D3D Dedicated Memory Used");
            var total = Find(_gpu, SensorType.SmallData, "GPU Memory Total", "D3D Dedicated Memory Total");
            if (used != null && total != null) { r["memUsed"] = (long)used.Value.Value; r["memTotal"] = (long)total.Value.Value; }
            if (_controls.Count > 0)
            {
                r["fanMin"] = (int)Math.Ceiling(_controls.Max(c => c.Control.MinSoftwareValue));
                r["fanMax"] = (int)Math.Floor(_controls.Min(c => c.Control.MaxSoftwareValue));
            }
            AddFanState(r);
            return r;
        }
    }

    public override void SetFixed(int percent)
    {
        lock (Sync)
        {
            if (!Ok) throw new InvalidOperationException("GPU indisponível: " + _error);
            if (_controls.Count == 0) throw new InvalidOperationException("O LibreHardwareMonitor não consegue controlar o fan desta GPU.");
            percent = AppConfig.Clamp(percent, (int)Math.Ceiling(_controls.Max(c => c.Control.MinSoftwareValue)), (int)Math.Floor(_controls.Min(c => c.Control.MaxSoftwareValue)));
            try { foreach (var c in _controls) c.Control.SetSoftware(percent); }
            catch (Exception ex)
            {
                foreach (var c in _controls) TryDefault(c);
                throw new InvalidOperationException("O driver recusou o controle manual do fan: " + ex.Message);
            }
            ManualPercent = percent;
            SafetyNote = null;
        }
    }

    public override void SetAuto()
    {
        lock (Sync)
        {
            if (!Ok) return;
            foreach (var c in _controls) TryDefault(c);
            ManualPercent = null;
        }
    }

    static void TryDefault(ISensor c)
    {
        try { c.Control.SetDefault(); } catch (Exception ex) { Log.Warn("GPU: SetDefault falhou: " + ex.Message); }
    }

    public override void Shutdown()
    {
        // Only undo what FanPilot set: a fan nobody touched is left exactly as the driver has it.
        if (ManualPercent.HasValue) SetAuto();
        lock (Sync)
        {
            try { if (_pc != null) _pc.Close(); } catch { }
            _pc = null;
            _gpu = null;
            _error = "encerrado";
        }
    }
}
