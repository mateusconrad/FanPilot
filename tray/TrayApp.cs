// FanPilotTray.exe (item 14b): the FanPilot icon next to the clock, started at every logon by the Run key that
// autostart.ps1 writes. Every few seconds it reads the service's /api/state for the tooltip and the mark on the icon; a
// click opens the terminal screen (FanPilot.exe --tui, item 29) in the default terminal, and closing that window leaves
// the icon and the service as they were. "--port <n>" says where FanPilot listens (8787 by default): the service's
// folder, with its config.json, is not readable here.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        int port = 8787;
        for (int i = 0; i + 1 < args.Length; i++)
        {
            int p;
            if (args[i] == "--port" && int.TryParse(args[i + 1], out p) && p > 0 && p < 65536) port = p;
        }
        // One icon per session: the logon's, or the first one opened by hand.
        bool first;
        using (new Mutex(true, @"Local\FanPilotTray", out first))
        {
            if (!first) return;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayContext(port));
        }
    }
}

sealed class TrayContext : ApplicationContext
{
    const int PollMs = 5000;

    readonly string _lang = I18n.SystemLanguage;
    readonly int _port;
    readonly HttpClient _http;
    readonly NotifyIcon _icon;
    readonly System.Windows.Forms.Timer _timer;
    readonly Icon _base;
    Icon _marked;
    TrayLevel? _shown;
    bool _polling, _closed;
    Process _monitor;

    public TrayContext(int port)
    {
        _port = port;
        // localhost, as the server's own "this PC only" expects; over the loopback the API needs no pairing.
        _http = new HttpClient { BaseAddress = new Uri("http://localhost:" + port + "/"), Timeout = TimeSpan.FromSeconds(4) };
        using (var stream = typeof(TrayContext).Assembly.GetManifestResourceStream("fanpilot.ico"))
            _base = new Icon(stream, SystemInformation.SmallIconSize);

        var menu = new ContextMenuStrip();
        var open = menu.Items.Add(I18n.T(_lang, "tray.openMonitor"), null, (s, e) => OpenMonitor());
        open.Font = new Font(open.Font, FontStyle.Bold);
        menu.Items.Add(I18n.T(_lang, "tray.openPanel"), null, (s, e) => OpenPanel());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(I18n.T(_lang, "tray.exit"), null, (s, e) => ExitThread());

        _icon = new NotifyIcon { Icon = _base, Text = "FanPilot", ContextMenuStrip = menu, Visible = true };
        _icon.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) OpenMonitor(); };
        _timer = new System.Windows.Forms.Timer { Interval = PollMs };
        _timer.Tick += (s, e) => Poll();
        _timer.Start();
        Poll();
    }

    // On the UI thread before and after the request (WinForms' synchronization context), so nothing here is shared.
    async void Poll()
    {
        if (_polling) return;
        _polling = true;
        TrayStatus status;
        try { status = TrayStatus.Of(Json.ParseObject(await _http.GetStringAsync("api/state")), _lang); }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is FormatException) { status = TrayStatus.Down(_lang); }
        finally { _polling = false; }
        Show(status);
    }

    void Show(TrayStatus status)
    {
        if (_closed) return;
        _icon.Text = status.Tooltip;
        if (_shown == status.Level) return;
        _shown = status.Level;
        var old = _marked;
        _marked = status.Level == TrayLevel.Ok ? null : Marked(_base, status.Level == TrayLevel.Down ? Color.Gray : Color.OrangeRed);
        _icon.Icon = _marked ?? _base;
        if (old != null) old.Dispose();
    }

    // The icon with a dot in its lower right corner: orange for an alert, gray while FanPilot doesn't answer.
    static Icon Marked(Icon icon, Color color)
    {
        using (var bmp = icon.ToBitmap())
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int d = Math.Max(6, bmp.Width / 2);
                var r = new Rectangle(bmp.Width - d, bmp.Height - d, d - 1, d - 1);
                using (var brush = new SolidBrush(color)) g.FillEllipse(brush, r);
                using (var pen = new Pen(Color.White, Math.Max(1, d / 6))) g.DrawEllipse(pen, r);
            }
            IntPtr handle = bmp.GetHicon();
            try { using (var tmp = Icon.FromHandle(handle)) return (Icon)tmp.Clone(); }
            finally { DestroyIcon(handle); }
        }
    }

    void OpenMonitor()
    {
        // The one already open comes back to the front. In Windows Terminal (Windows 11's default) the console has no
        // window of its own to bring back, and another one opens.
        if (_monitor != null && !_monitor.HasExited)
        {
            _monitor.Refresh();
            var window = _monitor.MainWindowHandle;
            if (window != IntPtr.Zero)
            {
                ShowWindow(window, SwRestore);
                SetForegroundWindow(window);
                return;
            }
        }
        var exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FanPilot.exe");
        var psi = new ProcessStartInfo(exe, "--tui --port " + _port) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) };
        // FanPilot.exe's manifest asks for Administrator, which --tui doesn't need (the Start menu shortcut does the same).
        psi.EnvironmentVariables["__COMPAT_LAYER"] = "RunAsInvoker";
        try
        {
            var started = Process.Start(psi);
            if (_monitor != null) _monitor.Dispose();
            _monitor = started;
        }
        catch (Win32Exception ex)
        {
            _icon.ShowBalloonTip(8000, "FanPilot", I18n.T(_lang, "tray.openFailed", new Dictionary<string, object> { { "path", exe }, { "error", ex.Message } }), ToolTipIcon.Error);
        }
    }

    void OpenPanel()
    {
        using (Process.Start(new ProcessStartInfo("http://localhost:" + _port + "/") { UseShellExecute = true })) { }
    }

    // "Close the icon": the service, and a terminal screen already open, carry on.
    protected override void ExitThreadCore()
    {
        _closed = true;
        _timer.Stop();
        _timer.Dispose();
        _icon.Visible = false;
        _icon.Dispose();
        if (_marked != null) _marked.Dispose();
        _base.Dispose();
        _http.Dispose();
        if (_monitor != null) _monitor.Dispose();
        base.ExitThreadCore();
    }

    const int SwRestore = 9;
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr hIcon);
}
