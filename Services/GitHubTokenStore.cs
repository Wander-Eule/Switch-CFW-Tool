using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SwitchCfwTool.Services;

/// <summary>
/// Persists a GitHub personal access token locally, encrypted with Windows DPAPI on Windows
/// or AES-GCM with 0600 file permissions on Linux/macOS. No account credentials ever touch
/// this — the user pastes a token they generated themselves on github.com.
/// </summary>
public static class GitHubTokenStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Switch-CFW-Tool", "github_token.bin");

    public static string? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var encrypted = File.ReadAllBytes(FilePath);

            if (OperatingSystem.IsWindows())
            {
                var plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }

            // Format on Unix: [12-byte nonce][16-byte tag][ciphertext]
            if (encrypted.Length < 28) return null;
            var nonce = encrypted.AsSpan(0, 12);
            var tag = encrypted.AsSpan(12, 16);
            var cipher = encrypted.AsSpan(28);
            var plainBytes = new byte[cipher.Length];
            using var aes = new AesGcm(DeriveKey(), 16);
            aes.Decrypt(nonce, cipher, tag, plainBytes);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch
        {
            // Corrupt file, or encrypted under a different user profile — treat as "no token"
            // rather than crashing startup over a cosmetic convenience feature.
            return null;
        }
    }

    public static void Save(string token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var plain = Encoding.UTF8.GetBytes(token);

        if (OperatingSystem.IsWindows())
        {
            var encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(FilePath, encrypted);
            return;
        }

        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[plain.Length];
        using (var aes = new AesGcm(DeriveKey(), 16))
            aes.Encrypt(nonce, plain, cipher, tag);

        var payload = new byte[12 + 16 + cipher.Length];
        nonce.CopyTo(payload, 0);
        tag.CopyTo(payload, 12);
        cipher.CopyTo(payload, 28);
        File.WriteAllBytes(FilePath, payload);
        File.SetUnixFileMode(FilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    public static void Clear()
    {
        if (File.Exists(FilePath)) File.Delete(FilePath);
    }

    private static byte[] DeriveKey()
    {
        var material = Encoding.UTF8.GetBytes($"{Environment.UserName}@{Environment.MachineName}:Switch-CFW-Tool");
        return SHA256.HashData(material);
    }
}
