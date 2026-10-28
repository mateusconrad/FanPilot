// One FanPilot at a time on Linux (item 28): a lock file held open with FileShare.None, which .NET turns into an
// exclusive flock. The kernel drops it when the process ends, however it ends, so a FanPilot that was killed never
// blocks the next one. SingleInstance.Windows.cs has the same members.
using System;
using System.IO;

static class SingleInstance
{
    // Held for the whole run: closing it (or the GC finalizing it) would let a second FanPilot in.
    static FileStream _held;

    public static bool TryAcquire(bool fake)
    {
        _held = TryAcquire(LockPath(fake));
        return _held != null;
    }

    public static bool IsRunning(bool fake) { return IsRunning(LockPath(fake)); }

    // /run/fanpilot as root (the service and sudo alike, so they see each other); the fake board, which runs without
    // root, in the user's runtime folder.
    static string LockPath(bool fake)
    {
        string dir = Platform.IsElevated ? "/run/fanpilot"
                   : Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? Path.GetTempPath();
        return Path.Combine(dir, fake ? "fanpilot-fake.lock" : "fanpilot.lock");
    }

    internal static FileStream TryAcquire(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return null; }
    }

    internal static bool IsRunning(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) return false;
        }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; } // root's lock seen by a user: only a FanPilot makes it
    }
}
