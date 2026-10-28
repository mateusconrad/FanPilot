// The GPU on Linux: NVML only for now, as nvml.dll's Linux name (libnvidia-ml.so.1) and AMD and Intel through hwmon come
// with item 28b; without it the panel shows the GPU as unavailable. GpuMonitor.Windows.cs has the same members.
abstract partial class GpuMonitor
{
    public static GpuMonitor Create(int safetyTemp)
    {
        var nvml = new NvmlGpu(safetyTemp);
        if (!nvml.Ok) Log.Info("GPU unavailable: " + nvml.Error.Text);
        return nvml;
    }
}
