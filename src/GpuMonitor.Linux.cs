// The GPU on Linux (item 28b). GpuMonitor.Windows.cs has the same members.
abstract partial class GpuMonitor
{
    // NVML when the NVIDIA driver provides it (libnvidia-ml.so.1, Platform.Init points nvml.dll at it), otherwise the
    // AMD or Intel driver's hwmon.
    public static GpuMonitor Create(int safetyTemp)
    {
        var nvml = new NvmlGpu(safetyTemp);
        if (nvml.Ok) return nvml;
        var hwmon = HwmonGpu.Find(safetyTemp);
        if (hwmon != null)
        {
            Log.Info("GPU via hwmon: " + hwmon.Name);
            return hwmon;
        }
        Log.Info("GPU unavailable: " + nvml.Error.Text);
        return nvml;
    }
}
