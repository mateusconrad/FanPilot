// GPU stand-in for the tests and for --fake-board: plausible values, and never touches a real driver.
using System;
using System.Collections.Generic;

sealed class FakeGpu : GpuMonitor
{
    readonly DateTime _started = DateTime.UtcNow;
    public int Temp = 58;
    // Temperature drifts with time (the --fake-board demo); the tests keep it fixed.
    public bool Animated;

    public FakeGpu(int safetyTemp) : base(safetyTemp) { }

    public override Dictionary<string, object> Read()
    {
        lock (Sync)
        {
            int temp = Animated ? Temp + (int)Math.Round(5 * Math.Sin((DateTime.UtcNow - _started).TotalSeconds / 15)) : Temp;
            var r = new Dictionary<string, object> {
                { "ok", true }, { "name", "GPU de teste" }, { "temp", temp }, { "fans", new List<int> { ManualPercent ?? 42 } },
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
            ManualPercent = AppConfig.Clamp(percent, 30, 100);
            SafetyNote = null;
        }
    }

    public override void SetAuto() { lock (Sync) ManualPercent = null; }

    // For the tests: the manual setting the panel would read back.
    public int? Manual { get { lock (Sync) return ManualPercent; } }

    public override void Shutdown() { SetAuto(); }
}
