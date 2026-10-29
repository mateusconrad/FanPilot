using System.Collections.Generic;
using Xunit;

// What the tray icon (item 14b) says from /api/state: the tooltip and the mark on the icon.
public class TrayStatusTests
{
    static Dictionary<string, object> State(string json) { return Json.ParseObject(json); }

    const string Sensors = "\"sensors\":[{\"type\":\"Temperature\",\"title\":\"PCH\",\"value\":31},"
        + "{\"type\":\"Temperature\",\"title\":\"CPU · socket\",\"value\":36.4,\"role\":\"cpu-main\"}]";

    [Fact]
    public void TheCpuAndGpuTemperatures()
    {
        var s = TrayStatus.Of(State("{" + Sensors + ",\"service\":{\"ok\":true},\"gpu\":{\"ok\":true,\"temp\":41},\"alerts\":[]}"), I18n.Portuguese);
        Assert.Equal(TrayLevel.Ok, s.Level);
        Assert.Equal("FanPilot · CPU 36 °C · GPU 41 °C", s.Tooltip);
    }

    [Fact]
    public void TheBoardsCpuSensorWhenNoneIsMarked()
    {
        var s = TrayStatus.Of(State("{\"sensors\":[{\"type\":\"Temperature\",\"title\":\"CPU\",\"value\":50}],\"gpu\":{\"ok\":false}}"), I18n.English);
        Assert.Equal("FanPilot · CPU 50 °C", s.Tooltip);
    }

    [Fact]
    public void ActiveAlertsMarkTheIconAndComeFirst()
    {
        var alerts = "\"alerts\":[{\"id\":\"a\",\"resolved\":null},{\"id\":\"b\",\"resolved\":null},{\"id\":\"c\",\"resolved\":1793277447460}]";
        var s = TrayStatus.Of(State("{" + Sensors + "," + alerts + "}"), I18n.Portuguese);
        Assert.Equal(TrayLevel.Alert, s.Level);
        Assert.Equal("FanPilot · 2 alertas · CPU 36 °C", s.Tooltip);
        Assert.Equal("FanPilot · 1 alert", TrayStatus.Of(State("{\"alerts\":[{\"id\":\"a\"}]}"), I18n.English).Tooltip);
    }

    [Fact]
    public void ABoardThatDoesNotAnswerIsAnAlert()
    {
        var s = TrayStatus.Of(State("{\"service\":{\"ok\":false,\"error\":\"EasyTuneEngineService unavailable\"}}"), I18n.English);
        Assert.Equal(TrayLevel.Alert, s.Level);
        Assert.Equal("FanPilot · board not answering", s.Tooltip);
    }

    [Fact]
    public void FanPilotNotAnswering()
    {
        var s = TrayStatus.Down(I18n.Portuguese);
        Assert.Equal(TrayLevel.Down, s.Level);
        Assert.Equal("FanPilot · não está respondendo", s.Tooltip);
    }

    [Fact]
    public void TheTooltipFitsWhatWindowsAccepts()
    {
        var alerts = "\"alerts\":[" + string.Join(",", new[] { "{}", "{}", "{}" }) + "]";
        var s = TrayStatus.Of(State("{" + Sensors + "," + alerts + ",\"service\":{\"ok\":false},\"gpu\":{\"temp\":41}}"), I18n.Portuguese);
        Assert.True(s.Tooltip.Length <= TrayStatus.MaxTooltip, s.Tooltip);
        Assert.EndsWith("…", s.Tooltip);
    }

    // Item 16 in the tray: a notification once per alert, in Windows' language.
    [Fact]
    public void EachNewAlertIsNotifiedOnce()
    {
        var seen = new HashSet<string>();
        const string stopped = "{\"id\":\"fanStopped:SYS_FAN1\",\"severity\":\"critical\",\"resolved\":null,"
            + "\"title\":{\"code\":\"alert.fanStopped.title\",\"text\":\"Fan stopped: SYS 1\",\"params\":{\"name\":\"SYS 1\"}},\"detail\":{\"text\":\"0 RPM\"}}";
        var first = TrayStatus.NewAlerts(State("{\"alerts\":[" + stopped + "]}"), seen, I18n.English);
        var notice = Assert.Single(first);
        Assert.Equal("Fan stopped: SYS 1", notice.Title);
        Assert.Equal("0 RPM", notice.Detail);
        Assert.True(notice.Critical);
        Assert.Empty(TrayStatus.NewAlerts(State("{\"alerts\":[" + stopped + "]}"), seen, I18n.English));
        // Resolved, then back: shown again.
        Assert.Empty(TrayStatus.NewAlerts(State("{\"alerts\":[]}"), seen, I18n.English));
        Assert.Single(TrayStatus.NewAlerts(State("{\"alerts\":[" + stopped + "]}"), seen, I18n.English));
    }

    [Fact]
    public void AnAcknowledgedOrResolvedAlertStaysQuiet()
    {
        var seen = new HashSet<string>();
        var state = State("{\"alerts\":[{\"id\":\"a\",\"acknowledged\":true,\"resolved\":null},{\"id\":\"b\",\"resolved\":1793277447460}]}");
        Assert.Empty(TrayStatus.NewAlerts(state, seen, I18n.English));
        Assert.Contains("a", seen);
        Assert.DoesNotContain("b", seen);
    }

    [Fact]
    public void TheProfilesHaveThePanelsNames()
    {
        Assert.Equal(new[] { "Personalizado", "Silencioso", "Padrão", "Performance" },
            System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(TrayStatus.Modes, m => TrayStatus.ModeLabel(m, I18n.Portuguese))));
    }

    [Fact]
    public void TheServersErrorInWindowsLanguage()
    {
        Assert.Equal("Somente no PC.", TrayStatus.ErrorText("{\"error\":\"This PC only.\",\"code\":\"err.localOnly\"}", I18n.Portuguese));
        Assert.Equal("boom", TrayStatus.ErrorText("{\"error\":\"boom\"}", I18n.Portuguese));
        Assert.Null(TrayStatus.ErrorText("<html>", I18n.Portuguese));
    }
}
