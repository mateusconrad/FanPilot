// The terminal (item 29): TerminalFeed's numbers drawn by TerminalScreen, redrawn every second where they changed, and
// the keys. FanPilot run in a console shows it instead of the scrolling log (--log keeps that), and FanPilot --tui
// shows it attached to a FanPilot already running (the service), without Administrator or root.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

static class TerminalApp
{
    // The alternate screen (the terminal's own scrollback stays as it was) without a cursor; and back.
    const string Enter = "\u001b[?1049h\u001b[?25l\u001b[2J", Leave = "\u001b[?25h\u001b[?1049l";

    // Whether this console can show it: a real terminal on both ends, with ANSI (on Windows, VT switched on).
    public static bool Available()
    {
        try { return !Console.IsOutputRedirected && !Console.IsInputRedirected && Platform.EnableTerminal(); }
        catch (IOException) { return false; }
    }

    // FanPilot --tui. 0 on q; 4 when no FanPilot answers (as "already running" uses it), 1 without a terminal.
    public static int Attach(int port)
    {
        string lang = I18n.SystemLanguage;
        if (!Available())
        {
            Console.WriteLine(new Msg("tui.noTerminal").In(lang));
            return 1;
        }
        using (var feed = new TerminalFeed(port))
        {
            if (!feed.Connect())
            {
                Console.WriteLine(feed.Problem.In(lang));
                return 4;
            }
            Run(feed, new ManualResetEvent(false), false, null);
        }
        return 0;
    }

    // The FanPilot in this window: until q (which sets stop, so Main cleans up as on Ctrl+C) or stop set elsewhere.
    public static void RunHere(int port, ManualResetEvent stop, string logPath)
    {
        using (var feed = new TerminalFeed(port))
        {
            feed.Connect();
            Run(feed, stop, true, logPath);
        }
    }

    static void Run(TerminalFeed feed, ManualResetEvent stop, bool owns, string logPath)
    {
        string lang = I18n.SystemLanguage;
        feed.Start();
        bool showLog = false;
        string[] shown = new string[0];
        int width = 0, height = 0;
        var nextDraw = DateTime.MinValue;
        bool ctrlC = Console.TreatControlCAsInput;
        Console.TreatControlCAsInput = true; // Ctrl+C is q here, so the screen is always put back
        Console.Write(Enter);
        try
        {
            while (!stop.WaitOne(0))
            {
                bool redraw = DateTime.UtcNow >= nextDraw;
                while (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.Q || key.Key == ConsoleKey.Escape || (key.Key == ConsoleKey.C && (key.Modifiers & ConsoleModifiers.Control) != 0))
                    {
                        stop.Set();
                        return;
                    }
                    if (key.Key == ConsoleKey.O) Platform.OpenFromTerminal(feed.Url);
                    else if (key.Key == ConsoleKey.T) ThreadPool.QueueUserWorkItem(_ => { try { feed.ToggleTurbo(); } catch (Exception) { } });
                    else if (key.Key == ConsoleKey.L && logPath != null) showLog = !showLog;
                    redraw = true;
                }
                int w = SafeWidth(), h = SafeHeight();
                if (w != width || h != height)
                {
                    width = w;
                    height = h;
                    shown = new string[0];
                    Console.Write("\u001b[2J");
                    redraw = true;
                }
                if (redraw)
                {
                    List<string> lines;
                    if (showLog) lines = TerminalScreen.RenderLog(Tail(logPath, height), width, height, lang, true);
                    else
                    {
                        var view = feed.View(lang);
                        view.OwnsFanPilot = owns;
                        view.HasLog = logPath != null;
                        lines = TerminalScreen.Render(view, width, height, lang, true);
                    }
                    shown = Draw(lines, shown);
                    nextDraw = DateTime.UtcNow.AddSeconds(1);
                }
                stop.WaitOne(100);
            }
        }
        finally
        {
            Console.Write(Leave);
            Console.TreatControlCAsInput = ctrlC;
        }
    }

    // Only the lines that changed, each cleared to its end; returns what is on the screen now.
    static string[] Draw(List<string> lines, string[] shown)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < lines.Count; i++)
        {
            if (i < shown.Length && shown[i] == lines[i]) continue;
            sb.Append("\u001b[").Append(i + 1).Append(";1H").Append(lines[i]).Append("\u001b[K");
        }
        if (sb.Length > 0) Console.Write(sb.ToString());
        return lines.ToArray();
    }

    // The last lines of the log file, as another process (FanPilot itself) writes it.
    static List<string> Tail(string path, int count)
    {
        try
        {
            using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(f, Encoding.UTF8))
            {
                if (f.Length > 64 * 1024) f.Seek(-64 * 1024, SeekOrigin.End);
                var lines = reader.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
                return lines.Skip(Math.Max(0, lines.Count - count)).ToList();
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return new List<string> { ex.Message }; }
    }

    // Some consoles (a service's, a closed window) answer with an exception: the usual 80 x 24 then.
    static int SafeWidth() { try { return Math.Max(20, Console.WindowWidth); } catch (IOException) { return 80; } }

    static int SafeHeight() { try { return Math.Max(3, Console.WindowHeight); } catch (IOException) { return 24; } }
}
