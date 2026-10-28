// Item 05: one FanPilot at a time. Each test uses its own Local\ name (Global\ needs Administrator to create), and a
// second thread plays the second FanPilot: a Mutex is re-entrant for the thread that owns it.
using System;
using System.Threading;
using Xunit;

public class SingleInstanceTests
{
    static string NewName() { return @"Local\FanPilot-test-" + Guid.NewGuid().ToString("N"); }

    static bool AcquireFromAnotherThread(string name)
    {
        bool acquired = false;
        var t = new Thread(() =>
        {
            var m = SingleInstance.TryAcquire(name);
            acquired = m != null;
            if (m != null) { m.ReleaseMutex(); m.Dispose(); }
        });
        t.Start();
        t.Join();
        return acquired;
    }

    [Fact]
    public void SecondInstanceIsRefusedWhileTheFirstRuns()
    {
        string name = NewName();
        var first = SingleInstance.TryAcquire(name);
        Assert.NotNull(first);
        Assert.True(SingleInstance.IsRunning(name));
        Assert.False(AcquireFromAnotherThread(name));
        first.ReleaseMutex();
        first.Dispose();
        Assert.True(AcquireFromAnotherThread(name));
    }

    [Fact]
    public void AnInstanceThatDiedWithoutReleasingDoesNotBlockTheNextOne()
    {
        string name = NewName();
        var dead = new Thread(() => SingleInstance.TryAcquire(name)); // ends holding it: the mutex is abandoned
        dead.Start();
        dead.Join();
        var next = SingleInstance.TryAcquire(name);
        Assert.NotNull(next);
        next.ReleaseMutex();
        next.Dispose();
    }

    [Fact]
    public void NothingRunningIsNothingRunning()
    {
        Assert.False(SingleInstance.IsRunning(NewName()));
    }
}
