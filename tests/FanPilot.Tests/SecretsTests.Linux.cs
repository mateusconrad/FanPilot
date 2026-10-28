// Item 28: the secrets in the data files on Linux, where DPAPI doesn't exist: AES-GCM with a key kept outside the data
// folder, so a folder copied to another PC doesn't carry them (AlertTests and MqttTests check they never reach the disk
// in clear).
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Xunit;

public class SecretsTests
{
    static readonly byte[] Entropy = Encoding.UTF8.GetBytes("FanPilot.tests");

    [Fact]
    public void ASecretComesBackAsItWent()
    {
        string stored = Secrets.Protect("token-123 é", Entropy);
        Assert.StartsWith(Secrets.Prefix, stored);
        Assert.DoesNotContain("token", stored);
        Assert.NotEqual(stored, Secrets.Protect("token-123 é", Entropy)); // a new nonce every time
        Assert.Equal("token-123 é", Secrets.Unprotect(stored, Entropy));
        Assert.Null(Secrets.Unprotect(null, Entropy));
        Assert.Null(Secrets.Unprotect("dpapi:AAAA", Entropy)); // Windows' kind: not this PC's to read
    }

    [Fact]
    public void TheKeyIsTheOwnersAloneAndLivesOutsideTheData()
    {
        Secrets.Protect("x", Entropy);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Secrets.KeyPath));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                     File.GetUnixFileMode(Path.GetDirectoryName(Secrets.KeyPath)));
        Assert.Equal(32, new FileInfo(Secrets.KeyPath).Length);
    }

    [Fact]
    public void AnotherPCsSecretIsUnreadable()
    {
        // Sealed with some other key, as another PC's FanPilot would.
        byte[] key = RandomNumberGenerator.GetBytes(32), text = Encoding.UTF8.GetBytes("segredo"), sealedBytes = new byte[12 + 16 + text.Length];
        RandomNumberGenerator.Fill(new Span<byte>(sealedBytes, 0, 12));
        using (var aes = new AesGcm(key, 16))
            aes.Encrypt(new Span<byte>(sealedBytes, 0, 12), text, new Span<byte>(sealedBytes, 28, text.Length), new Span<byte>(sealedBytes, 12, 16), Entropy);
        Assert.ThrowsAny<CryptographicException>(() => Secrets.Unprotect(Secrets.Prefix + Convert.ToBase64String(sealedBytes), Entropy));
        // Damaged, or not even base64: unreadable the same way, never a crash elsewhere.
        Assert.ThrowsAny<CryptographicException>(() => Secrets.Unprotect(Secrets.Prefix + "AAAA", Entropy));
        Assert.ThrowsAny<CryptographicException>(() => Secrets.Unprotect(Secrets.Prefix + "não é base64", Entropy));
        // The right key, another field (entropy): unreadable too.
        string mine = Secrets.Protect("segredo", Entropy);
        Assert.ThrowsAny<CryptographicException>(() => Secrets.Unprotect(mine, Encoding.UTF8.GetBytes("outro")));
    }
}
