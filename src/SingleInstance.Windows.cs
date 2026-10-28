// One FanPilot at a time on Windows: a named mutex. SingleInstance.Linux.cs has the same members (item 28).
using System.Threading;

// One FanPilot per PC: a second one would fight the first over the fans (and fail on the port anyway).
static class SingleInstance
{
    // Global\: visible from every session, so a FanPilot running as a service (session 0) counts too.
    const string Name = @"Global\FanPilot-e717e36d-9b66-43c9-a08b-96d2af1a9b7c";
    // Held for the whole run. A local variable could be collected, and finalizing the Mutex would let a second one in.
    static Mutex _held;

    public static bool TryAcquire(bool fake)
    {
        _held = TryAcquire(fake ? Name + "-fake" : Name);
        return _held != null;
    }

    public static bool IsRunning(bool fake) { return IsRunning(fake ? Name + "-fake" : Name); }

    internal static Mutex TryAcquire(string name)
    {
        var mutex = new Mutex(false, name);
        try
        {
            if (mutex.WaitOne(0)) return mutex;
            mutex.Dispose();
            return null;
        }
        catch (AbandonedMutexException)
        {
            // The previous FanPilot ended without releasing it (killed, crashed): the mutex is ours now.
            Log.Warn("The previous FanPilot ended without shutting down cleanly.");
            return mutex;
        }
    }

    internal static bool IsRunning(string name)
    {
        Mutex mutex;
        if (!Mutex.TryOpenExisting(name, out mutex)) return false;
        mutex.Dispose();
        return true;
    }
}
