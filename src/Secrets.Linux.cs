// Secrets in the data files (the alerts' destinations, item 16; the MQTT password, item 23) on Linux (item 28), where
// there is no DPAPI: AES-GCM ("aesgcm:<base64 of nonce, tag and ciphertext>") with a key kept outside the data folder,
// so a copied data folder doesn't carry them to another PC, as DPAPI ensures on Windows. Without the key (another PC,
// the key deleted), Unprotect throws CryptographicException, as DPAPI does. Secrets.Windows.cs has the same members.
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

static class Secrets
{
    internal const string Prefix = "aesgcm:";
    const int KeyBytes = 32, NonceBytes = 12, TagBytes = 16;

    // Root (the service, sudo): /etc/fanpilot/secret.key. Anyone else (the fake board, the tests): the user's config
    // folder. Read and written by its owner only.
    internal static string KeyPath = Platform.IsElevated
        ? "/etc/fanpilot/secret.key"
        : Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is string xdg && xdg.Length > 0
                           ? xdg : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"),
                       "fanpilot", "secret.key");

    public static string Protect(string plain, byte[] entropy)
    {
        if (plain == null) return null;
        byte[] key = Key(true);
        byte[] text = Encoding.UTF8.GetBytes(plain);
        byte[] sealedBytes = new byte[NonceBytes + TagBytes + text.Length];
        var nonce = new Span<byte>(sealedBytes, 0, NonceBytes);
        RandomNumberGenerator.Fill(nonce);
        using (var aes = new AesGcm(key, TagBytes))
            aes.Encrypt(nonce, text, new Span<byte>(sealedBytes, NonceBytes + TagBytes, text.Length),
                        new Span<byte>(sealedBytes, NonceBytes, TagBytes), entropy);
        return Prefix + Convert.ToBase64String(sealedBytes);
    }

    public static string Unprotect(string stored, byte[] entropy)
    {
        if (stored == null || !stored.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        byte[] sealedBytes;
        try { sealedBytes = Convert.FromBase64String(stored.Substring(Prefix.Length)); }
        catch (FormatException ex) { throw new CryptographicException("Damaged secret.", ex); }
        if (sealedBytes.Length < NonceBytes + TagBytes) throw new CryptographicException("Damaged secret.");
        byte[] key = Key(false);
        byte[] text = new byte[sealedBytes.Length - NonceBytes - TagBytes];
        using (var aes = new AesGcm(key, TagBytes))
            aes.Decrypt(new ReadOnlySpan<byte>(sealedBytes, 0, NonceBytes), new ReadOnlySpan<byte>(sealedBytes, NonceBytes + TagBytes, text.Length),
                        new ReadOnlySpan<byte>(sealedBytes, NonceBytes, TagBytes), text, entropy);
        return Encoding.UTF8.GetString(text);
    }

    // create: the first secret saved on this PC makes the key.
    static byte[] Key(bool create)
    {
        if (File.Exists(KeyPath)) return ReadKey();
        if (!create) throw new CryptographicException("This PC has no key for FanPilot's secrets (" + KeyPath + ").");
        Directory.CreateDirectory(Path.GetDirectoryName(KeyPath));
        File.SetUnixFileMode(Path.GetDirectoryName(KeyPath), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        byte[] key = RandomNumberGenerator.GetBytes(KeyBytes);
        try
        {
            var options = new FileStreamOptions {
                Mode = FileMode.CreateNew, Access = FileAccess.Write, UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite };
            using (var f = new FileStream(KeyPath, options)) f.Write(key, 0, key.Length);
            return key;
        }
        catch (IOException) when (File.Exists(KeyPath)) { return ReadKey(); } // another FanPilot made it first
    }

    static byte[] ReadKey()
    {
        byte[] key = File.ReadAllBytes(KeyPath);
        if (key.Length != KeyBytes) throw new CryptographicException("Damaged key for FanPilot's secrets (" + KeyPath + ").");
        return key;
    }
}
