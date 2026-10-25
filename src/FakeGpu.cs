// GPU stand-in for the tests and for --fake-board: plausible values, and never touches a real driver.
using System;
using System.Collections.Generic;

sealed class FakeGpu : GpuMonitor
{
    readonly DateTime _started = DateTime.UtcNow;
    public int Temp = 58;
    public string Name = "GPU de teste";
    // Set: no GPU FanPilot can read (no driver), reported as the real monitors do.
    public string Error;
    // Temperature drifts with time (the --fake-board demo); the tests keep it fixed.
    public bool Animated;

    public FakeGpu(int safetyTemp) : base(safetyTemp) { }

    // The GPU of each FakeBoard.Scenarios entry.
    public static FakeGpu Scenario(string name, int safetyTemp)
    {
        switch (name)
        {
            case "worst": return new FakeGpu(safetyTemp) { Animated = true, Temp = 80, Name = "NVIDIA GeForce RTX 5090 D v2 Laptop GPU" };
            case "unsupported": return new FakeGpu(safetyTemp) { Error = "NVML não encontrado (nvml.dll): instale o driver da NVIDIA." };
            default: return new FakeGpu(safetyTemp) { Animated = true };
        }
    }

    public override Dictionary<string, object> Read()
    {
        lock (Sync)
        {
            if (Error != null) return new Dictionary<string, object> { { "ok", false }, { "error", Error } };
            int temp = Animated ? Temp + (int)Math.Round(5 * Math.Sin((DateTime.UtcNow - _started).TotalSeconds / 15)) : Temp;
            var r = new Dictionary<string, object> {
                { "ok", true }, { "name", Name }, { "temp", temp }, { "fans", new List<int> { ManualPercent ?? 42 } },
                { "power", 96.0 }, { "powerLimit", 200.0 }, { "util", 34 }, { "memUtil", 20 }, { "clock", 2475 },
                { "memClock", 10501 }, { "memUsed", 4200L }, { "memTotal", 12282L }, { "fanMin", 30 }, { "fanMax", 100 }
            };
            AddFanState(r);
            return r;
        }
    }

    public override void SetFixed(int percent)
    {
        lock (Sync)
        {
            if (Error != null) throw new InvalidOperationException("GPU indisponível: " + Error);
            ManualPercent = AppConfig.Clamp(percent, 30, 100);
            SafetyNote = null;
        }
    }

    public override void SetAuto() { lock (Sync) ManualPercent = null; }

    // For the tests: the manual setting the panel would read back.
    public int? Manual { get { lock (Sync) return ManualPercent; } }

    public override void Shutdown() { SetAuto(); }
}
