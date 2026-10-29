// The GPU on Linux through its driver's hwmon (item 28b), for the cards NVML doesn't cover: AMD (amdgpu: temperature,
// fan, power, clocks, load and memory) and Intel (i915, xe: what they expose, read only). The fan, where the driver
// lets it be set: pwm1_enable 1 (manual) with a duty in pwm1, and 2 (the driver's automatic curve) to give it back.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

sealed class HwmonGpu : GpuMonitor
{
    const int Manual = 1, Automatic = 2;
    readonly HwmonFiles.Chip _chip;
    readonly bool _canControl;
    readonly string _name;

    public string Name { get { return _name; } }

    HwmonGpu(HwmonFiles.Chip chip, int safetyTemp) : base(safetyTemp)
    {
        _chip = chip;
        _canControl = chip.Name == "amdgpu" && File.Exists(chip.File("pwm1")) && File.Exists(chip.File("pwm1_enable"));
        _name = HwmonFiles.ReadText(chip.File(Path.Combine("device", "product_name")))
                ?? (chip.Name == "amdgpu" ? "AMD Radeon" : "Intel GPU") + " (" + chip.Name + ")";
    }

    // AMD first (a dedicated card next to an Intel iGPU is the one that heats up); null without either.
    public static HwmonGpu Find(int safetyTemp, string root = "/")
    {
        var chips = HwmonFiles.Chips(root);
        var chip = chips.FirstOrDefault(c => c.Name == "amdgpu") ?? chips.FirstOrDefault(c => c.Name == "xe" || c.Name == "i915");
        return chip == null ? null : new HwmonGpu(chip, safetyTemp);
    }

    public override Dictionary<string, object> Read()
    {
        lock (Sync)
        {
            var r = new Dictionary<string, object>();
            r["ok"] = true;
            r["name"] = _name;
            long? v = HwmonFiles.ReadLong(_chip.File("temp1_input"));
            if (v.HasValue) r["temp"] = (int)Math.Round(v.Value / 1000.0);
            v = HwmonFiles.ReadLong(_chip.File("pwm1"));
            r["fans"] = _canControl && v.HasValue ? new List<int> { (int)Math.Round(v.Value * 100 / 255.0) } : new List<int>();
            v = HwmonFiles.ReadLong(_chip.File("power1_average")) ?? HwmonFiles.ReadLong(_chip.File("power1_input"));
            if (v.HasValue) r["power"] = Math.Round(v.Value / 1e6, 1);
            v = HwmonFiles.ReadLong(_chip.File("power1_cap"));
            if (v.HasValue) r["powerLimit"] = Math.Round(v.Value / 1e6, 0);
            v = HwmonFiles.ReadLong(_chip.File(Path.Combine("device", "gpu_busy_percent")));
            if (v.HasValue) r["util"] = (int)v.Value;
            v = HwmonFiles.ReadLong(_chip.File("freq1_input"));
            if (v.HasValue) r["clock"] = (int)(v.Value / 1000000);
            v = HwmonFiles.ReadLong(_chip.File("freq2_input"));
            if (v.HasValue) r["memClock"] = (int)(v.Value / 1000000);
            long? used = HwmonFiles.ReadLong(_chip.File(Path.Combine("device", "mem_info_vram_used")));
            long? total = HwmonFiles.ReadLong(_chip.File(Path.Combine("device", "mem_info_vram_total")));
            if (used.HasValue && total.HasValue) { r["memUsed"] = used.Value >> 20; r["memTotal"] = total.Value >> 20; }
            if (_canControl) { r["fanMin"] = 0; r["fanMax"] = 100; }
            AddFanState(r);
            return r;
        }
    }

    public override void SetFixed(int percent)
    {
        lock (Sync)
        {
            if (!_canControl) throw new Unavailable("gpu.noFans");
            percent = AppConfig.Clamp(percent, 0, 100);
            try
            {
                HwmonFiles.Write(_chip.File("pwm1_enable"), Manual);
                HwmonFiles.Write(_chip.File("pwm1"), (long)Math.Round(percent * 255 / 100.0));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                TryAutomatic();
                throw new Unavailable("gpu.refusedHwmon", "detail", ex.Message);
            }
            ManualPercent = percent;
            SafetyNote = null;
        }
    }

    public override void SetAuto()
    {
        lock (Sync)
        {
            if (!_canControl || !ManualPercent.HasValue) return;
            TryAutomatic();
            ManualPercent = null;
        }
    }

    void TryAutomatic()
    {
        try { HwmonFiles.Write(_chip.File("pwm1_enable"), Automatic); }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { Log.Warn("GPU: could not give the fan back to the driver: " + ex.Message); }
    }

    public override void Shutdown() { SetAuto(); }
}
