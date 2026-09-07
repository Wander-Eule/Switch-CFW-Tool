using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SwitchCfwTool.Services;

/// <summary>
/// Persists a GitHub personal access token locally, encrypted with Windows DPAPI so the
/// file on disk is unreadable outside the current Windows user account (roughly the same
/// protection browsers use for saved passwords). No account credentials ever touch this —
/// the user pastes a token they generated themselves on github.com.
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
            var plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
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
        var encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(FilePath, encrypted);
    }

    public static void Clear()
    {
        if (File.Exists(FilePath)) File.Delete(FilePath);
    }
}
