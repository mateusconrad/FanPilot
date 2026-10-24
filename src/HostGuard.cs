// Only requests addressed to this PC are served: the Host header must name one of its own addresses (or localhost,
// the machine name, publicHost) on the configured port. Requests from the PC itself are trusted without a session,
// so without this check a web page could reach the API through DNS rebinding (its own domain resolving to 127.0.0.1).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

sealed class HostGuard
{
    static readonly string[] LoopbackNames = { "localhost", "127.0.0.1", "::1" };

    readonly int _port;
    readonly string _publicHost;
    readonly Func<IEnumerable<string>> _localAddresses;
    readonly object _sync = new object();
    readonly Dictionary<string, DateTime> _lastRejectLog = new Dictionary<string, DateTime>();
    volatile HashSet<string> _allowed;

    public HostGuard(int port, string publicHost) : this(port, publicHost, InterfaceAddresses)
    {
        // DHCP renewals and network switches change the PC's addresses; rebuild the set when Windows reports it.
        NetworkChange.NetworkAddressChanged += (s, e) => Refresh();
    }

    public HostGuard(int port, string publicHost, Func<IEnumerable<string>> localAddresses)
    {
        _port = port;
        _publicHost = publicHost;
        _localAddresses = localAddresses;
        Refresh();
    }

    public void Refresh()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in LoopbackNames) set.Add(n);
        set.Add(Normalize(Environment.MachineName));
        set.Add(Normalize(Environment.MachineName + ".local"));
        if (!string.IsNullOrEmpty(_publicHost)) set.Add(Normalize(_publicHost));
        try { foreach (var a in _localAddresses()) set.Add(Normalize(a)); }
        catch (Exception ex) { Log.Warn("HostGuard: não foi possível listar os endereços de rede: " + ex.Message); }
        _allowed = set;
    }

    public bool IsAllowed(Uri url)
    {
        return url != null && url.Port == _port && _allowed.Contains(Normalize(url.Host));
    }

    // Trust without a session needs a loopback name too, not just a loopback connection (defense in depth).
    public static bool IsLoopbackName(Uri url)
    {
        return url != null && LoopbackNames.Contains(Normalize(url.Host), StringComparer.OrdinalIgnoreCase);
    }

    // At most once per host per hour: a page retrying in a loop must not flood the log. Returns whether it logged.
    public bool LogRejected(string authority)
    {
        string key = authority.Length > 100 ? authority.Substring(0, 100) : authority;
        lock (_sync)
        {
            DateTime last;
            if (_lastRejectLog.TryGetValue(key, out last) && DateTime.UtcNow - last < TimeSpan.FromHours(1)) return false;
            if (_lastRejectLog.Count >= 1000) _lastRejectLog.Clear();
            _lastRejectLog[key] = DateTime.UtcNow;
        }
        Log.Warn("Requisição recusada: '" + key + "' não é um endereço deste PC (possível DNS rebinding).");
        return true;
    }

    // "[::1]" -> "::1", "fe80::1%12" -> "fe80::1", "LocalHost." -> "localhost".
    static string Normalize(string host)
    {
        string h = (host ?? "").Trim().TrimStart('[').TrimEnd(']');
        int scope = h.IndexOf('%');
        if (scope >= 0) h = h.Substring(0, scope);
        h = h.TrimEnd('.');
        // Uri expands IPv6 ("[::1]" -> "[0000:...:0001]") while interfaces report it compressed: compare parsed values.
        IPAddress ip;
        if (h.IndexOf(':') >= 0 && IPAddress.TryParse(h, out ip)) return ip.ToString();
        return h.ToLowerInvariant();
    }

    static IEnumerable<string> InterfaceAddresses()
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            foreach (var a in ni.GetIPProperties().UnicastAddresses)
                if (a.Address.AddressFamily == AddressFamily.InterNetwork || a.Address.AddressFamily == AddressFamily.InterNetworkV6)
                    yield return a.Address.ToString();
        }
    }
}
