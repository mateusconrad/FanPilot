// Item 05 on Linux (item 28): one FanPilot at a time through a lock file. Each test uses its own file; a second
// FileStream plays the second FanPilot, since the flock behind FileShare.None is per open file, not per process.
using System;
using System.Diagnostics;
using System.IO;
using Xunit;

// The tests' .NET 10 build runs on Linux only, as FanPilot's does.
[assembly: System.Runtime.Versioning.SupportedOSPlatform("linux")]

public sealed class SingleInstanceTests : IDisposable
{
    readonly TempDir _dir = new TempDir();

    public void Dispose() { _dir.Dispose(); }

    [Fact]
    public void SecondInstanceIsRefusedWhileTheFirstRuns()
    {
        string path = _dir.File("fanpilot.lock");
        var first = SingleInstance.TryAcquire(path);
        Assert.NotNull(first);
        Assert.True(SingleInstance.IsRunning(path));
        Assert.Null(SingleInstance.TryAcquire(path));
        first.Dispose();
        Assert.False(SingleInstance.IsRunning(path));
        using (var next = SingleInstance.TryAcquire(path)) Assert.NotNull(next);
    }

    [Fact]
    public void AnInstanceThatWasKilledDoesNotBlockTheNextOne()
    {
        string path = _dir.File("fanpilot.lock");
        // Another process takes the lock and dies with kill -9: the kernel drops the lock with it. -o: flock itself
        // holds it, not the sleep it runs.
        var holder = Process.Start(new ProcessStartInfo("flock", "-o \"" + path + "\" sleep 60") { UseShellExecute = false });
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!SingleInstance.IsRunning(path) && DateTime.UtcNow < deadline) System.Threading.Thread.Sleep(50);
            Assert.True(SingleInstance.IsRunning(path));
        }
        finally
        {
            holder.Kill(true);
            holder.WaitForExit();
        }
        using (var next = SingleInstance.TryAcquire(path)) Assert.NotNull(next);
    }

    [Fact]
    public void NothingRunningIsNothingRunning()
    {
        Assert.False(SingleInstance.IsRunning(_dir.File("fanpilot.lock")));
    }
}
