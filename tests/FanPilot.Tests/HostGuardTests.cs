// Item 01: only requests addressed to this PC are served (DNS rebinding). The interface list is fixed by the test.
using System;
using Xunit;

public class HostGuardTests
{
    readonly HostGuard _guard = new HostGuard(8787, "meupc.lan", () => new[] { "192.168.0.10", "fe80::1c2b:3a4d:5e6f:7081%12", "172.20.16.1" });

    [Theory]
    [InlineData("http://localhost:8787/")]
    [InlineData("http://LOCALHOST.:8787/api/fans")]
    [InlineData("http://127.0.0.1:8787/")]
    [InlineData("http://[::1]:8787/")]
    [InlineData("http://192.168.0.10:8787/")]
    [InlineData("http://[fe80::1c2b:3a4d:5e6f:7081%2512]:8787/")]
    [InlineData("http://172.20.16.1:8787/")]
    [InlineData("http://meupc.lan:8787/")]
    public void AllowsThisPcsAddresses(string url)
    {
        Assert.True(_guard.IsAllowed(new Uri(url)));
    }

    [Fact]
    public void AllowsTheMachineName()
    {
        Assert.True(_guard.IsAllowed(new Uri("http://" + Environment.MachineName + ":8787/")));
        Assert.True(_guard.IsAllowed(new Uri("http://" + Environment.MachineName.ToLowerInvariant() + ".local:8787/")));
    }

    [Theory]
    [InlineData("http://exemplo.invalid:8787/")]
    [InlineData("http://evil.localhost.exemplo.invalid:8787/")]
    [InlineData("http://10.0.0.5:8787/")]
    [InlineData("http://localhost:9999/")]
    [InlineData("http://localhost/")]
    public void RefusesEverythingElse(string url)
    {
        Assert.False(_guard.IsAllowed(new Uri(url)));
    }

    [Theory]
    [InlineData("http://localhost:8787/", true)]
    [InlineData("http://127.0.0.1:8787/", true)]
    [InlineData("http://[::1]:8787/", true)]
    [InlineData("http://192.168.0.10:8787/", false)]
    [InlineData("http://exemplo.invalid:8787/", false)]
    public void OnlyLoopbackNamesCountAsThePcItself(string url, bool loopback)
    {
        Assert.Equal(loopback, HostGuard.IsLoopbackName(new Uri(url)));
    }

    [Fact]
    public void LogsEachRefusedHostOncePerHour()
    {
        Assert.True(_guard.LogRejected("exemplo.invalid:8787"));
        Assert.False(_guard.LogRejected("exemplo.invalid:8787"));
        Assert.True(_guard.LogRejected("outro.invalid:8787"));
    }

    [Fact]
    public void StillAllowsLocalhostWhenTheInterfacesCannotBeListed()
    {
        var broken = new HostGuard(8787, null, () => { throw new InvalidOperationException("sem rede"); });
        Assert.True(broken.IsAllowed(new Uri("http://localhost:8787/")));
        Assert.False(broken.IsAllowed(new Uri("http://192.168.0.10:8787/")));
    }
}
