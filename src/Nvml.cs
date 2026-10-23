// NVIDIA GPU monitoring/fan control through NVML (nvml.dll ships with the driver in System32).
// GCC does not cover the GPU, so this is independent of the Gigabyte service.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

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

sealed class GpuMonitor
{
    readonly object _sync = new object();
    readonly int _safetyTemp;
    IntPtr _device;
    uint _fanCount;
    bool _ok;
    string _name = "";
    string _error;
    int? _manualPercent;
    string _safetyNote;
    uint _minFan = 0, _maxFan = 100;

    public GpuMonitor(int safetyTemp)
    {
        _safetyTemp = safetyTemp;
        try
        {
            Check(NvmlNative.nvmlInit_v2(), "nvmlInit");
            uint count;
            Check(NvmlNative.nvmlDeviceGetCount_v2(out count), "GetCount");
            if (count == 0) throw new InvalidOperationException("nenhuma GPU NVIDIA");
            Check(NvmlNative.nvmlDeviceGetHandleByIndex_v2(0, out _device), "GetHandle");
            var sb = new StringBuilder(96);
            if (NvmlNative.nvmlDeviceGetName(_device, sb, (uint)sb.Capacity) == NvmlNative.Success) _name = sb.ToString();
            if (NvmlNative.nvmlDeviceGetNumFans(_device, out _fanCount) != NvmlNative.Success) _fanCount = 0;
            uint min, max;
            if (NvmlNative.nvmlDeviceGetMinMaxFanSpeed(_device, out min, out max) == NvmlNative.Success) { _minFan = min; _maxFan = max; }
            _ok = true;
        }
        catch (Exception ex) { _error = ex.Message; _ok = false; }
    }

    static void Check(int rc, string what)
    {
        if (rc != NvmlNative.Success) throw new InvalidOperationException(what + " falhou (NVML " + rc + ")");
    }

    public Dictionary<string, object> Read()
    {
        lock (_sync)
        {
            var r = new Dictionary<string, object>();
            r["ok"] = _ok;
            if (!_ok) { r["error"] = _error; return r; }
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
            r["fanMode"] = _manualPercent.HasValue ? "fixed" : "auto";
            r["fanTarget"] = _manualPercent;
            r["fanMin"] = (int)_minFan;
            r["fanMax"] = (int)_maxFan;
            r["safetyTemp"] = _safetyTemp;
            r["safetyNote"] = _safetyNote;
            return r;
        }
    }

    // Called once per sample: a manual fan setting must never outlive an overheating GPU.
    public void EnforceSafety(object tempObj)
    {
        if (!_manualPercent.HasValue || tempObj == null) return;
        if (Convert.ToInt32(tempObj) >= _safetyTemp)
        {
            SetAuto();
            lock (_sync) _safetyNote = "Fan voltou para automático: GPU atingiu " + tempObj + " °C (" + DateTime.Now.ToString("HH:mm:ss") + ")";
            Log.Info("GPU safety: reverted fan to auto at " + tempObj + " C");
        }
    }

    public void SetFixed(int percent)
    {
        lock (_sync)
        {
            if (!_ok) throw new InvalidOperationException("GPU indisponível: " + _error);
            if (_fanCount == 0) throw new InvalidOperationException("A GPU não expõe fans controláveis.");
            percent = AppConfig.Clamp(percent, (int)_minFan, (int)_maxFan);
            for (uint i = 0; i < _fanCount; i++)
            {
                int rc = NvmlNative.nvmlDeviceSetFanSpeed_v2(_device, i, (uint)percent);
                if (rc != 0)
                {
                    for (uint j = 0; j < _fanCount; j++) NvmlNative.nvmlDeviceSetDefaultFanSpeed_v2(_device, j);
                    throw new InvalidOperationException("O driver recusou o controle manual do fan (NVML " + rc + ").");
                }
            }
            _manualPercent = percent;
            _safetyNote = null;
        }
    }

    public void SetAuto()
    {
        lock (_sync)
        {
            if (!_ok) return;
            for (uint i = 0; i < _fanCount; i++) NvmlNative.nvmlDeviceSetDefaultFanSpeed_v2(_device, i);
            _manualPercent = null;
        }
    }

    public void Shutdown()
    {
        SetAuto();
        lock (_sync) { if (_ok) NvmlNative.nvmlShutdown(); _ok = false; }
    }
}
