// QR pairing and sessions, with the clock under the test's control. Defaults (AppConfig): code valid for 180 s,
// sessions expire after 30 days without use, 10 wrong codes per minute block the IP.
using System;
using System.IO;
using System.Linq;
using Xunit;

public sealed class AuthManagerTests : IDisposable
{
    const string Phone = "192.168.0.50", Agent = "Mozilla/5.0 (Android)";
    readonly TempDir _dir = new TempDir();
    readonly AppConfig _cfg = new AppConfig();
    DateTime _now = new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);

    public void Dispose() { _dir.Dispose(); }

    AuthManager Auth() { return new AuthManager(_dir.Path, _cfg, () => _now); }

    static string Code(AuthManager a)
    {
        string code;
        DateTime expires;
        a.GetPairCode(out code, out expires);
        return code;
    }

    [Fact]
    public void CodeWorksOnceThenRotates()
    {
        var a = Auth();
        string code = Code(a);
        Assert.NotNull(a.Redeem(code, Phone, Agent));
        Assert.Null(a.Redeem(code, Phone, Agent));
        Assert.NotEqual(code, Code(a));
    }

    [Fact]
    public void CodeExpires()
    {
        var a = Auth();
        string code = Code(a);
        _now = _now.AddSeconds(181);
        Assert.Null(a.Redeem(code, Phone, Agent));
        Assert.NotEqual(code, Code(a));
    }

    [Fact]
    public void TooManyWrongCodesBlockTheIpForAMinute()
    {
        var a = Auth();
        for (int i = 0; i < 10; i++) Assert.Null(a.Redeem("errado", Phone, Agent));
        Assert.True(a.IsBlocked(Phone));
        Assert.False(a.IsBlocked("192.168.0.51"));
        _now = _now.AddSeconds(61);
        Assert.False(a.IsBlocked(Phone));
    }

    [Fact]
    public void SessionLastsWhileItIsUsed()
    {
        var a = Auth();
        string token = a.Redeem(Code(a), Phone, Agent);
        _now = _now.AddDays(29);
        Assert.NotNull(a.Validate(token));
        _now = _now.AddDays(29); // 58 days after pairing, but only 29 since last seen
        Assert.NotNull(a.Validate(token));
        _now = _now.AddDays(31);
        Assert.Null(a.Validate(token));
    }

    [Fact]
    public void RevokingEndsTheSession()
    {
        var a = Auth();
        string first = a.Redeem(Code(a), Phone, Agent), second = a.Redeem(Code(a), Phone, Agent);
        a.Revoke(first);
        Assert.Null(a.Validate(first));
        Assert.NotNull(a.Validate(second));

        string id = (string)a.List().Single()["id"];
        Assert.True(a.RevokeById(id));
        Assert.Null(a.Validate(second));
    }

    [Fact]
    public void RevokeAllAlsoRotatesTheCode()
    {
        var a = Auth();
        string token = a.Redeem(Code(a), Phone, Agent);
        string code = Code(a);
        a.RevokeAll();
        Assert.Null(a.Validate(token));
        Assert.NotEqual(code, Code(a));
    }

    [Fact]
    public void OnlyTheTokensHashIsStored()
    {
        var a = Auth();
        string token = a.Redeem(Code(a), Phone, Agent);
        string file = File.ReadAllText(_dir.File("sessions.json"));
        Assert.DoesNotContain(token, file);
        Assert.Contains("tokenHash", file);
    }

    [Fact]
    public void DamagedSessionsFileStartsWithoutSessions()
    {
        File.WriteAllText(_dir.File("sessions.json"), "{\"sessions\": [{\"id\": ");
        var a = Auth();
        Assert.Empty(a.List());
        Assert.NotNull(a.Redeem(Code(a), Phone, Agent)); // pairing works again
    }

    [Fact]
    public void OneDamagedSessionDoesNotCostTheOthers()
    {
        var a = Auth();
        string token = a.Redeem(Code(a), Phone, Agent);
        string file = _dir.File("sessions.json");
        File.WriteAllText(file, File.ReadAllText(file).Replace("{\"id\"", "{\"lastSeen\":\"ontem\"},{\"id\""));
        Assert.NotNull(Auth().Validate(token));
    }

    [Fact]
    public void SessionsSurviveARestart()
    {
        var a = Auth();
        string token = a.Redeem(Code(a), Phone, Agent);
        Assert.NotNull(Auth().Validate(token));
    }
}
