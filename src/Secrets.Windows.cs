// Secrets.Linux.cs has the same members (item 28).
using System;
using System.Security.Cryptography;
using System.Text;

// Secrets in the data files (the alerts' destinations, item 16; the MQTT password, item 23): encrypted with DPAPI for
// this PC ("dpapi:<base64>"), so a copied data\ folder doesn't carry them to another one. Unprotect throws
// CryptographicException there.
static class Secrets
{
    internal const string Prefix = "dpapi:";

    public static string Protect(string plain, byte[] entropy)
    {
        if (plain == null) return null;
        return Prefix + Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), entropy, DataProtectionScope.LocalMachine));
    }

    public static string Unprotect(string stored, byte[] entropy)
    {
        if (stored == null || !stored.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored.Substring(Prefix.Length)), entropy, DataProtectionScope.LocalMachine));
    }
}
