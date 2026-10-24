// QR-code pairing:
//  1. The PC shows http://localhost:PORT/pair (loopback only) with a QR of http://<lan-ip>:PORT/auth?c=<code>.
//  2. The code is random, single-use and expires after a few minutes (then rotates).
//  3. Redeeming it sets an HttpOnly, SameSite=Strict session cookie; only its SHA-256 is stored on disk.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

sealed class Session
{
    public string Id;
    public string TokenHash;
    public DateTime CreatedUtc;
    public DateTime LastSeenUtc;
    public string Ip;
    public string UserAgent;
}

sealed class AuthManager
{
    public const string CookieName = "fanpilot_session";
    const int MaxFailuresPerMinute = 10;

    readonly object _sync = new object();
    readonly string _path;
    readonly TimeSpan _codeTtl, _sessionTtl;
    readonly Func<DateTime> _utcNow;
    readonly Dictionary<string, Session> _sessions = new Dictionary<string, Session>();
    readonly Dictionary<string, List<DateTime>> _failures = new Dictionary<string, List<DateTime>>();
    string _code;
    DateTime _codeExpiresUtc;
    DateTime _lastSave = DateTime.MinValue;

    public AuthManager(string dataDir, AppConfig cfg) : this(dataDir, cfg, () => DateTime.UtcNow) { }

    // utcNow: the tests move the clock to check expirations.
    internal AuthManager(string dataDir, AppConfig cfg, Func<DateTime> utcNow)
    {
        _utcNow = utcNow;
        _path = Path.Combine(dataDir, "sessions.json");
        _codeTtl = TimeSpan.FromSeconds(Math.Max(30, cfg.PairCodeTtlSeconds));
        _sessionTtl = TimeSpan.FromDays(Math.Max(1, cfg.SessionDays));
        Load();
        RotateCode();
    }

    static string RandomToken(int bytes)
    {
        var b = new byte[bytes];
        using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
        return Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    static string Hash(string token)
    {
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(token))).Replace("-", "").ToLowerInvariant();
    }

    static bool FixedTimeEquals(string a, string b)
    {
        if (a == null || b == null) return false;
        var x = Encoding.UTF8.GetBytes(a);
        var y = Encoding.UTF8.GetBytes(b);
        int diff = x.Length ^ y.Length;
        for (int i = 0; i < Math.Min(x.Length, y.Length); i++) diff |= x[i] ^ y[i];
        return diff == 0;
    }

    void RotateCode()
    {
        _code = RandomToken(18);
        _codeExpiresUtc = _utcNow() + _codeTtl;
    }

    public void GetPairCode(out string code, out DateTime expiresUtc)
    {
        lock (_sync)
        {
            if (_utcNow() >= _codeExpiresUtc) RotateCode();
            code = _code;
            expiresUtc = _codeExpiresUtc;
        }
    }

    public bool IsBlocked(string ip)
    {
        lock (_sync)
        {
            PruneFailures();
            List<DateTime> list;
            return _failures.TryGetValue(ip, out list) && list.Count >= MaxFailuresPerMinute;
        }
    }

    // Only the last minute counts: older attempts, and the IPs left with none, are dropped (the map would otherwise
    // keep every address that ever mistyped a code).
    void PruneFailures()
    {
        var since = _utcNow().AddMinutes(-1);
        foreach (var ip in _failures.Keys.ToList())
        {
            _failures[ip].RemoveAll(t => t < since);
            if (_failures[ip].Count == 0) _failures.Remove(ip);
        }
    }

    // For the tests: how many addresses have recent wrong attempts.
    internal int TrackedFailureIps { get { lock (_sync) { PruneFailures(); return _failures.Count; } } }

    // Returns a new session token, or null if the code is wrong/expired.
    public string Redeem(string code, string ip, string userAgent)
    {
        lock (_sync)
        {
            if (_utcNow() >= _codeExpiresUtc || !FixedTimeEquals(code, _code))
            {
                PruneFailures();
                List<DateTime> list;
                if (!_failures.TryGetValue(ip, out list)) _failures[ip] = list = new List<DateTime>();
                list.Add(_utcNow());
                return null;
            }
            RotateCode();
            string token = RandomToken(32);
            var s = new Session
            {
                Id = RandomToken(6),
                TokenHash = Hash(token),
                CreatedUtc = _utcNow(),
                LastSeenUtc = _utcNow(),
                Ip = ip,
                UserAgent = userAgent ?? ""
            };
            _sessions[s.TokenHash] = s;
            Save();
            Log.Info("Paired new device " + s.Id + " from " + ip);
            return token;
        }
    }

    public Session Validate(string token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        string h = Hash(token);
        lock (_sync)
        {
            Session s;
            if (!_sessions.TryGetValue(h, out s)) return null;
            if (_utcNow() - s.LastSeenUtc > _sessionTtl) { _sessions.Remove(h); Save(); return null; }
            s.LastSeenUtc = _utcNow();
            if (_utcNow() - _lastSave > TimeSpan.FromMinutes(5)) Save();
            return s;
        }
    }

    public void Revoke(string token) { lock (_sync) { if (_sessions.Remove(Hash(token))) Save(); } }

    public bool RevokeById(string id)
    {
        lock (_sync)
        {
            var s = _sessions.Values.FirstOrDefault(x => x.Id == id);
            if (s == null) return false;
            _sessions.Remove(s.TokenHash);
            Save();
            return true;
        }
    }

    public void RevokeAll() { lock (_sync) { _sessions.Clear(); RotateCode(); Save(); } }

    public List<Dictionary<string, object>> List()
    {
        lock (_sync)
        {
            return _sessions.Values.OrderByDescending(s => s.LastSeenUtc).Select(s => new Dictionary<string, object> {
                { "id", s.Id }, { "ip", s.Ip }, { "userAgent", s.UserAgent },
                { "created", s.CreatedUtc.ToString("o") }, { "lastSeen", s.LastSeenUtc.ToString("o") }
            }).ToList();
        }
    }

    void Load()
    {
        var saved = DataFile.Read(_path);
        if (saved == null) return;
        var sessions = Json.Get(saved, "sessions") as System.Collections.IEnumerable;
        if (sessions == null) return;
        foreach (object o in sessions)
        {
            var d = o as Dictionary<string, object>;
            if (d == null) continue;
            try
            {
                var s = new Session
                {
                    Id = Json.GetString(d, "id", ""),
                    TokenHash = Json.GetString(d, "tokenHash", ""),
                    CreatedUtc = DateTime.Parse(Json.GetString(d, "created", ""), null, System.Globalization.DateTimeStyles.RoundtripKind),
                    LastSeenUtc = DateTime.Parse(Json.GetString(d, "lastSeen", ""), null, System.Globalization.DateTimeStyles.RoundtripKind),
                    Ip = Json.GetString(d, "ip", ""),
                    UserAgent = Json.GetString(d, "userAgent", "")
                };
                if (s.TokenHash.Length == 64) _sessions[s.TokenHash] = s;
            }
            catch (FormatException) { } // one damaged session does not cost the others (that phone pairs again)
        }
    }

    void Save()
    {
        _lastSave = _utcNow();
        var list = _sessions.Values.Select(s => new Dictionary<string, object> {
            { "id", s.Id }, { "tokenHash", s.TokenHash }, { "created", s.CreatedUtc.ToString("o") },
            { "lastSeen", s.LastSeenUtc.ToString("o") }, { "ip", s.Ip }, { "userAgent", s.UserAgent }
        }).ToList();
        DataFile.WriteAtomic(_path, new Dictionary<string, object> { { "sessions", list } });
    }
}
