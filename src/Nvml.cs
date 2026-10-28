// NVIDIA GPU monitoring/fan control through NVML (nvml.dll ships with the driver in System32).
// GCC does not cover the GPU, so this is independent of the Gigabyte service. Which GPU monitor FanPilot uses on each
// system: GpuMonitor.Windows.cs (other GPUs through LibreHardwareMonitor: LhmGpu.cs) and GpuMonitor.Linux.cs.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

static class NvmlNative
{
    const string Dll = "nvml.dll";
    public const int Success = 0;

    [StructLayout(LayoutKind.Sequential)] public struct Utilization { public uint Gpu; public uint Memory; }
    [StructLayout(LayoutKind.Sequential)] public struct Memory { public ulong Total; public ulong Free; public ulong Used; }

    [DllImport(Dll)] public static extern int nvmlInit_v2();
    [DllImport(Dll)] public static extern int nvmlShutdown();
    [DllImport(Dll)] public static extern int nvmlDeviceGetCount_v2(out uint count);
    [DllImport(Dll)] public static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);
    [DllImport(Dll, CharSet = CharSet.Ansi)] public static extern int nvmlDeviceGetName(IntPtr device, StringBuilder name, uint length);
    [DllImport(Dll)] public static extern int nvmlDeviceGetTemperature(IntPtr device, int sensor, out uint temp);
    [DllImport(Dll)] public static extern int nvmlDeviceGetNumFans(IntPtr device, out uint count);
    [DllImport(Dll)] public static extern int nvmlDeviceGetFanSpeed_v2(IntPtr device, uint fan, out uint percent);
    [DllImport(Dll)] public static extern int nvmlDeviceGetMinMaxFanSpeed(IntPtr device, out uint min, out uint max);
    [DllImport(Dll)] public static extern int nvmlDeviceSetFanSpeed_v2(IntPtr device, uint fan, uint percent);
    [DllImport(Dll)] public static extern int nvmlDeviceSetDefaultFanSpeed_v2(IntPtr device, uint fan);
    [DllImport(Dll)] public static extern int nvmlDeviceGetPowerUsage(IntPtr device, out uint milliwatts);
    [DllImport(Dll)] public static extern int nvmlDeviceGetEnforcedPowerLimit(IntPtr device, out uint milliwatts);
    [DllImport(Dll)] public static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out Utilization util);
    [DllImport(Dll)] public static extern int nvmlDeviceGetClockInfo(IntPtr device, int clockType, out uint mhz);
    [DllImport(Dll)] public static extern int nvmlDeviceGetMemoryInfo(IntPtr device, out Memory mem);
}

// What the panel and the sampler use, whichever library reaches the GPU. The safety rule lives here: a manual fan
// setting never outlives an overheating GPU, and every way out puts the fan back on the driver's automatic curve.
abstract partial class GpuMonitor
{
    protected readonly object Sync = new object();
    protected readonly int SafetyTemp;
    protected int? ManualPercent;
    protected Msg SafetyNote;
    volatile Dictionary<string, object> _latest;
    volatile bool _stopped;

    protected GpuMonitor(int safetyTemp) { SafetyTemp = safetyTemp; }

    // The GPU's own loop, once a second: its safety rule must not wait for the motherboard's IPC (the sampler) or for a
    // slow web client.
    public void Start()
    {
        Tick();
        new Thread(() =>
        {
            while (!_stopped)
            {
                Thread.Sleep(1000);
                try { Tick(); } catch (Exception ex) { Log.Warn("GPU: " + ex.Message); }
            }
        }) { IsBackground = true, Name = "gpu" }.Start();
    }

    public void Stop() { _stopped = true; }

    // What the loop read last (the sampler's snapshot uses it); a direct read before Start().
    public Dictionary<string, object> Latest { get { return _latest ?? Read(); } }

    // The temperature in the last reading, or null when the GPU can't be read (curves that follow it, item 25).
    public double? Temperature
    {
        get
        {
            var r = Latest;
            object ok, temp;
            return r != null && r.TryGetValue("ok", out ok) && ok is bool && (bool)ok && r.TryGetValue("temp", out temp) && temp != null
                ? Convert.ToDouble(temp) : (double?)null;
        }
    }

    internal void Tick()
    {
        var r = Read();
        object temp;
        if (r.TryGetValue("temp", out temp) && EnforceSafety(temp)) r = Read(); // the snapshot must show the fan back on auto
        _latest = r;
    }

    public abstract Dictionary<string, object> Read();
    public abstract void SetFixed(int percent);
    public abstract void SetAuto();
    public abstract void Shutdown();

    // Called by Tick, once a second. Returns whether it put the fan back on automatic.
    public bool EnforceSafety(object tempObj)
    {
        if (!ManualPercent.HasValue || tempObj == null) return false;
        if (Convert.ToInt32(tempObj) < SafetyTemp) return false;
        SetAuto();
        lock (Sync) SafetyNote = new Msg("gpu.safetyNote", "temp", Convert.ToInt32(tempObj), "time", DateTime.Now.ToString("HH:mm:ss"));
        Log.Info("GPU safety: reverted fan to auto at " + tempObj + " C");
        return true;
    }

    protected void AddFanState(Dictionary<string, object> r)
    {
        r["fanMode"] = ManualPercent.HasValue ? "fixed" : "auto";
        r["fanTarget"] = ManualPercent;
        r["safetyTemp"] = SafetyTemp;
        r["safetyNote"] = SafetyNote == null ? null : SafetyNote.ToJson();
    }
}

sealed class NvmlGpu : GpuMonitor
{
    IntPtr _device;
    uint _fanCount;
    bool _ok;
    string _name = "";
    Msg _error;
    uint _minFan = 0, _maxFan = 100;

    public bool Ok { get { return _ok; } }
    public Msg Error { get { return _error; } }

    public NvmlGpu(int safetyTemp) : base(safetyTemp)
    {
        try
        {
            Check(NvmlNative.nvmlInit_v2(), "nvmlInit");
            uint count;
            Check(NvmlNative.nvmlDeviceGetCount_v2(out count), "GetCount");
            if (count == 0) throw new Unavailable("gpu.noNvidia");
            Check(NvmlNative.nvmlDeviceGetHandleByIndex_v2(0, out _device), "GetHandle");
            var sb = new StringBuilder(96);
            if (NvmlNative.nvmlDeviceGetName(_device, sb, (uint)sb.Capacity) == NvmlNative.Success) _name = sb.ToString();
            if (NvmlNative.nvmlDeviceGetNumFans(_device, out _fanCount) != NvmlNative.Success) _fanCount = 0;
            uint min, max;
            if (NvmlNative.nvmlDeviceGetMinMaxFanSpeed(_device, out min, out max) == NvmlNative.Success) { _minFan = min; _maxFan = max; }
            _ok = true;
        }
        catch (DllNotFoundException) { _error = new Msg("gpu.noNvml"); _ok = false; }
        catch (Exception ex) { _error = Msg.Of(ex) ?? new Msg("gpu.error", "detail", ex.Message); _ok = false; }
    }

    static void Check(int rc, string what)
    {
        if (rc != NvmlNative.Success) throw new Unavailable("gpu.nvmlFailed", "call", what, "code", rc);
    }

    public override Dictionary<string, object> Read()
    {
        lock (Sync)
        {
            var r = new Dictionary<string, object>();
            r["ok"] = _ok;
            if (!_ok) { r["error"] = _error.ToJson(); return r; }
            r["name"] = _name;
            uint v;
            if (NvmlNative.nvmlDeviceGetTemperature(_device, 0, out v) == 0) r["temp"] = (int)v;
            var fans = new List<int>();
            for (uint i = 0; i < _fanCount; i++)
                if (NvmlNative.nvmlDeviceGetFanSpeed_v2(_device, i, out v) == 0) fans.Add((int)v);
            r["fans"] = fans;
            if (NvmlNative.nvmlDeviceGetPowerUsage(_device, out v) == 0) r["power"] = Math.Round(v / 1000.0, 1);
            if (NvmlNative.nvmlDeviceGetEnforcedPowerLimit(_device, out v) == 0) r["powerLimit"] = Math.Round(v / 1000.0, 0);
            NvmlNative.Utilization u;
            if (NvmlNative.nvmlDeviceGetUtilizationRates(_device, out u) == 0) { r["util"] = (int)u.Gpu; r["memUtil"] = (int)u.Memory; }
            if (NvmlNative.nvmlDeviceGetClockInfo(_device, 0, out v) == 0) r["clock"] = (int)v;
            if (NvmlNative.nvmlDeviceGetClockInfo(_device, 2, out v) == 0) r["memClock"] = (int)v;
            NvmlNative.Memory m;
            if (NvmlNative.nvmlDeviceGetMemoryInfo(_device, out m) == 0) { r["memUsed"] = (long)(m.Used >> 20); r["memTotal"] = (long)(m.Total >> 20); }
            r["fanMin"] = (int)_minFan;
            r["fanMax"] = (int)_maxFan;
            AddFanState(r);
            return r;
        }
    }

    public override void SetFixed(int percent)
    {
        lock (Sync)
        {
            if (!_ok) throw new Unavailable("gpu.unavailableWhy", "error", _error);
            if (_fanCount == 0) throw new Unavailable("gpu.noFans");
            percent = AppConfig.Clamp(percent, (int)_minFan, (int)_maxFan);
            for (uint i = 0; i < _fanCount; i++)
            {
                int rc = NvmlNative.nvmlDeviceSetFanSpeed_v2(_device, i, (uint)percent);
                if (rc != 0)
                {
                    for (uint j = 0; j < _fanCount; j++) NvmlNative.nvmlDeviceSetDefaultFanSpeed_v2(_device, j);
                    throw new Unavailable("gpu.refusedNvml", "code", rc);
                }
            }
            ManualPercent = percent;
            SafetyNote = null;
        }
    }

    public override void SetAuto()
    {
        lock (Sync)
        {
            if (!_ok) return;
            for (uint i = 0; i < _fanCount; i++) NvmlNative.nvmlDeviceSetDefaultFanSpeed_v2(_device, i);
            ManualPercent = null;
        }
    }

    public override void Shutdown()
    {
        SetAuto();
        lock (Sync) { if (_ok) NvmlNative.nvmlShutdown(); _ok = false; }
    }
}
