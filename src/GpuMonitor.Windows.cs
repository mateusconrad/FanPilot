// The GPU on Windows. GpuMonitor.Linux.cs has the same members (item 28).
abstract partial class GpuMonitor
{
    // NVML when the NVIDIA driver provides it (most complete), otherwise LibreHardwareMonitor (AMD, Intel Arc, NVIDIA).
    public static GpuMonitor Create(int safetyTemp)
    {
        var nvml = new NvmlGpu(safetyTemp);
        if (nvml.Ok) return nvml;
        var lhm = new LhmGpu(safetyTemp, nvml.Error);
        Log.Info(lhm.Ok ? "GPU via LibreHardwareMonitor: " + lhm.Name : "GPU unavailable: " + lhm.Error.Text);
        return lhm;
    }
}
