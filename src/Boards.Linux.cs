// The motherboard on Linux: the kernel's hwmon drivers (/sys/class/hwmon), which item 28b brings. Until then FanPilot
// starts on Linux, says so and leaves every fan alone; --fake-board (Debug builds) runs the rest of it. Boards.Windows.cs
// has the same members.
static class Boards
{
    public static IBoard Create(AppConfig cfg, bool interactive)
    {
        throw new Unavailable("startup.linuxBoard");
    }

    public static string VersionNote(IBoard board) { return ""; }
}
