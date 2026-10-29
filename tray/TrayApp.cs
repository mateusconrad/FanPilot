// FanPilotTray.exe (item 14b): the FanPilot icon next to the clock, started at every logon by the Run key that
// autostart.ps1 writes. Every few seconds it reads the service's /api/state for the tooltip, the mark on the icon and a
// Windows notification for each new alert (item 16); a click opens the terminal screen (FanPilot.exe --tui, item 29) in
// the default terminal, and closing that window leaves the icon and the service as they were. The menu also pairs a
// phone, switches Turbo and the global profile (item 18) and shows the log, all through the same local API. "--port <n>" says where FanPilot listens (8787 by default): the service's
// folder, with its config.json, is not readable here.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
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
    readonly ToolStripMenuItem _turbo, _profiles;
    readonly HashSet<string> _seen = new HashSet<string>();
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
        menu.Items.Add(I18n.T(_lang, "tray.openPanel"), null, (s, e) => OpenPage(""));
        menu.Items.Add(I18n.T(_lang, "tray.pair"), null, (s, e) => OpenPage("pair"));
        menu.Items.Add(new ToolStripSeparator());
        _turbo = new ToolStripMenuItem(I18n.T(_lang, "turbo.off"), null, (s, e) => Send("api/fans/turbo", new Dictionary<string, object> { { "on", !_turbo.Checked } }));
        menu.Items.Add(_turbo);
        _profiles = new ToolStripMenuItem(I18n.T(_lang, "profiles.aria"));
        foreach (var mode in TrayStatus.Modes)
        {
            string m = mode;
            _profiles.DropDownItems.Add(new ToolStripMenuItem(TrayStatus.ModeLabel(m, _lang), null, (s, e) => Send("api/fans/mode", new Dictionary<string, object> { { "mode", m } })) { Tag = m });
        }
        menu.Items.Add(_profiles);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(I18n.T(_lang, "tray.log"), null, (s, e) => OpenLog());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(I18n.T(_lang, "tray.exit"), null, (s, e) => ExitThread());
        // Turbo and the profile change from the panel and the phone too: read again each time the menu opens.
        _turbo.Enabled = _profiles.Enabled = false;
        menu.Opening += (s, e) => RefreshFans();

        _icon = new NotifyIcon { Icon = _base, Text = "FanPilot", ContextMenuStrip = menu, Visible = true };
        _icon.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) OpenMonitor(); };
        _icon.BalloonTipClicked += (s, e) => OpenPage("");
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
        List<TrayNotice> notices = null;
        try
        {
            var state = Json.ParseObject(await _http.GetStringAsync("api/state"));
            status = TrayStatus.Of(state, _lang);
            notices = TrayStatus.NewAlerts(state, _seen, _lang);
        }
        catch (Exception ex) when (IsDown(ex)) { status = TrayStatus.Down(_lang); }
        finally { _polling = false; }
        Show(status);
        if (notices != null && notices.Count > 0) Notify(notices);
    }

    static bool IsDown(Exception ex)
    {
        return ex is HttpRequestException || ex is TaskCanceledException || ex is FormatException;
    }

    // A Windows notification (a balloon becomes one since Windows 10); several at once go in one, as each balloon
    // replaces the one before. A click opens the panel, where the alert is acknowledged.
    void Notify(List<TrayNotice> notices)
    {
        if (_closed) return;
        var icon = notices.Any(n => n.Critical) ? ToolTipIcon.Error : ToolTipIcon.Warning;
        if (notices.Count == 1) _icon.ShowBalloonTip(10000, notices[0].Title, notices[0].Detail.Length > 0 ? notices[0].Detail : "FanPilot", icon);
        else _icon.ShowBalloonTip(10000, "FanPilot · " + I18n.T(_lang, "tray.alerts", new Dictionary<string, object> { { "count", notices.Count } }),
            string.Join("\n", notices.Select(n => n.Title)), icon);
    }

    async void RefreshFans()
    {
        Dictionary<string, object> fans;
        try { fans = Json.ParseObject(await _http.GetStringAsync("api/fans")); }
        catch (Exception ex) when (IsDown(ex)) { fans = null; }
        if (_closed) return;
        _turbo.Enabled = _profiles.Enabled = fans != null;
        if (fans == null) return;
        _turbo.Checked = Json.Get(fans, "turbo") is bool && (bool)Json.Get(fans, "turbo");
        string mode = Json.GetString(fans, "mode", "custom");
        foreach (ToolStripMenuItem item in _profiles.DropDownItems) item.Checked = (string)item.Tag == mode;
    }

    // A POST as the panel sends it (the header the server asks of every one, item 01); what went wrong in a balloon.
    async void Send(string path, Dictionary<string, object> body)
    {
        string problem = null;
        try
        {
            using (var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(Json.Serialize(body), Encoding.UTF8, "application/json") })
            {
                request.Headers.Add("X-FanPilot", "1");
                using (var response = await _http.SendAsync(request))
                {
                    if (!response.IsSuccessStatusCode)
                        problem = TrayStatus.ErrorText(await response.Content.ReadAsStringAsync(), _lang) ?? ((int)response.StatusCode + " " + response.ReasonPhrase);
                }
            }
        }
        catch (Exception ex) when (IsDown(ex)) { problem = "FanPilot · " + I18n.T(_lang, "tray.down"); }
        if (_closed) return;
        if (problem != null) _icon.ShowBalloonTip(8000, "FanPilot", problem, ToolTipIcon.Error);
        RefreshFans();
    }

    // The service's log is in a folder only Administrators read: the end of it comes over the API, into a copy in
    // %TEMP% that Notepad opens.
    async void OpenLog()
    {
        string text;
        try { text = await _http.GetStringAsync("api/log"); }
        catch (Exception ex) when (IsDown(ex))
        {
            if (!_closed) _icon.ShowBalloonTip(8000, "FanPilot", "FanPilot · " + I18n.T(_lang, "tray.down"), ToolTipIcon.Warning);
            return;
        }
        var file = Path.Combine(Path.GetTempPath(), "FanPilot", "fanpilot.log");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, text, new UTF8Encoding(false));
            using (Process.Start(new ProcessStartInfo("notepad.exe", "\"" + file + "\"") { UseShellExecute = true })) { }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is Win32Exception)
        {
            if (!_closed) _icon.ShowBalloonTip(8000, "FanPilot", I18n.T(_lang, "tray.openFailed", new Dictionary<string, object> { { "path", file }, { "error", ex.Message } }), ToolTipIcon.Error);
        }
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

    // The panel, or the pairing page (only the PC itself opens it, at localhost).
    void OpenPage(string path)
    {
        using (Process.Start(new ProcessStartInfo("http://localhost:" + _port + "/" + path) { UseShellExecute = true })) { }
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
