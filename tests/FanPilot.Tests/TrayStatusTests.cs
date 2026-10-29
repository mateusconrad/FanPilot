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
}
