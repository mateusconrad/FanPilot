// Item 28: "this PC only" (FanPilot without root, the tests) on Linux, where .NET's HttpListener can't bind both
// loopback addresses by name: it listens on every address and turns away whatever doesn't come from this PC.
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Xunit;

public class LocalhostOnlyTests
{
    static WebServer Start(AppConfig cfg)
    {
        return TestEnv.StartServer(p =>
        {
            cfg.Port = p;
            return new WebServer(cfg, Path.Combine(TestEnv.OutputDir, "wwwroot")) { ListenHost = "localhost" };
        });
    }

    // What came back on a GET / sent to address: the status line, or null when the connection was closed unanswered.
    static string StatusLine(IPAddress address, int port)
    {
        using (var tcp = new TcpClient(address.AddressFamily))
        {
            tcp.Connect(address, port);
            var stream = tcp.GetStream();
            stream.ReadTimeout = 10000;
            byte[] request = Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: localhost:" + port + "\r\nConnection: close\r\n\r\n");
            try
            {
                stream.Write(request, 0, request.Length);
                string text = new StreamReader(stream, Encoding.ASCII).ReadToEnd();
                return text.Length == 0 ? null : text.Substring(0, text.IndexOf('\r'));
            }
            catch (IOException) { return null; } // reset by the server
        }
    }

    [Fact]
    public void TheLoopbackAddressIsAnswered()
    {
        var cfg = new AppConfig();
        var server = Start(cfg);
        try
        {
            // IPv4 only: on Linux .NET's HttpListener takes every IPv4 address for "+". A browser asked for localhost
            // tries ::1 and then 127.0.0.1.
            Assert.StartsWith("HTTP/1.1 ", StatusLine(IPAddress.Loopback, cfg.Port));
        }
        finally { server.Stop(); }
    }

    [SkippableFact]
    public void TheNetworkIsTurnedAway()
    {
        // One of this PC's own network addresses plays the phone on the LAN: the connection comes in through it.
        var lan = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(a => a.Address)
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
        Skip.If(lan == null, "no network address on this machine");
        var cfg = new AppConfig();
        var server = Start(cfg);
        try
        {
            // Nothing of FanPilot's: no page, no file, not even the "starting" answer.
            string answer = StatusLine(lan, cfg.Port);
            Assert.True(answer == null || answer.StartsWith("HTTP/1.1 403 "), "answered from " + lan + ": " + answer);
        }
        finally { server.Stop(); }
    }
}
