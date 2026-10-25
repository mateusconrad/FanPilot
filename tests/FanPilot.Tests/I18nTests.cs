using System.Collections.Generic;
using Xunit;

// The words FanPilot writes for people in English and Portuguese (item 21): the panel's dictionaries, embedded in the
// exe (I18n.cs).
public class I18nTests
{
    [Theory]
    [InlineData("pt-BR,pt;q=0.9,en-US;q=0.8,en;q=0.7", I18n.Portuguese)]
    [InlineData("en-US,en;q=0.9,pt-BR;q=0.8", I18n.English)]
    [InlineData("es-ES,es;q=0.9,pt;q=0.8", I18n.Portuguese)]
    [InlineData("de-DE", I18n.English)]
    [InlineData("en;q=0.5,pt-PT;q=0.8", I18n.Portuguese)]
    [InlineData("pt;q=0,en", I18n.English)]
    [InlineData("", I18n.English)]
    [InlineData(null, I18n.English)]
    public void TheBrowsersFirstChoiceBetweenTheTwo(string header, string expected)
    {
        Assert.Equal(expected, I18n.FromAcceptLanguage(header));
    }

    [Fact]
    public void ParametersFollowTheLanguage()
    {
        var m = new Msg("err.curve.maxTemp", "max", 90);
        Assert.Equal("Highest temperature allowed in the curve: 90 °C.", m.Text);
        Assert.Equal("Temperatura máxima permitida na curva: 90 °C.", m.In(I18n.Portuguese));
        // Decimals with the language's mark, one at most, as the panel shows them.
        var note = new Msg("alert.temp.title", "what", "CPU", "temp", 96.54);
        Assert.Equal("CPU at 96.5 °C", note.Text);
        Assert.Equal("CPU a 96,5 °C", note.In(I18n.Portuguese));
    }

    [Fact]
    public void PluralsAndMessagesInsideMessages()
    {
        Assert.EndsWith("for it.", new Msg("notice.backupLost", "fans", "System 1", "count", 1).Text);
        Assert.EndsWith("para eles.", new Msg("notice.backupLost", "fans", "System 1, System 2", "count", 2).In(I18n.Portuguese));
        var resolved = new Msg("alert.resolved.title", "title", new Msg("alert.fanStopped.title", "name", "System 2"));
        Assert.Equal("Resolved: Fan stopped: System 2", resolved.Text);
        Assert.Equal("Resolvido: Fan parado: System 2", resolved.In(I18n.Portuguese));
    }

    [Fact]
    public void AMissingKeyShowsItselfInsteadOfABlank()
    {
        Assert.Equal("no.such.key", new Msg("no.such.key").Text);
        Assert.Equal("Fan stopped: {name}", I18n.T(I18n.English, "alert.fanStopped.title"));
    }

    // What the API sends: the panel translates code with params and falls back to text.
    [Fact]
    public void TheApiGetsTheCodeTheParametersAndTheEnglish()
    {
        var json = new Msg("gpu.unavailableWhy", "error", new Msg("gpu.noNvml")).ToJson();
        Assert.Equal("gpu.unavailableWhy", json["code"]);
        Assert.Equal("GPU unavailable: NVML not found (nvml.dll): install the NVIDIA driver.", json["text"]);
        var error = (Dictionary<string, object>)((Dictionary<string, object>)json["params"])["error"];
        Assert.Equal("gpu.noNvml", error["code"]);
        Assert.False(new Msg("err.internal").ToJson().ContainsKey("params"));
    }

    [Fact]
    public void OurExceptionsCarryTheirMessage()
    {
        Assert.Equal("err.fanMissing", Msg.Of(new UserError("err.fanMissing")).Key);
        Assert.Equal("No such fan.", new UserError("err.fanMissing").Message);
        Assert.Equal("board.closing", Msg.Of(new Unavailable("board.closing")).Key);
        Assert.Null(Msg.Of(new System.ArgumentException("any")));
    }
}
