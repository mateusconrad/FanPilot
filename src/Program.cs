// FanPilot - web monitor/fan controller for the motherboard's and the GPU's fans. Two builds (item 28): Windows, on the
// .NET Framework 4.7.2 that ships with it (Gigabyte Control Center's service speaks .NET Remoting, which only exists
// there), and Linux, on .NET 10. The .NET SDK is only needed to build.
static class Program
{
    static int Main(string[] args)
    {
        Platform.Init();
        return App.Run(args);
    }
}
