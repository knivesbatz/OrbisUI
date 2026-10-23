using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OrbisUI.Core;

/// <summary>API keys and tokens, encrypted with Windows DPAPI (current user). Never written in plain text.</summary>
public static class Secrets
{
    public const string SteamGridDb = "steamgriddb";
    public const string SteamWebApi = "steamwebapi";
    public const string IgdbClientId = "igdb_client_id";
    public const string IgdbClientSecret = "igdb_client_secret";
    public const string RetroAchievementsUser = "ra_user";
    public const string RetroAchievementsKey = "ra_key";

    static readonly byte[] Entropy = Encoding.UTF8.GetBytes("OrbisUI.secrets.v1");
    static readonly object Gate = new();
    static Dictionary<string, string>? _cache;

    static string FilePath => Path.Combine(AppPaths.Config, "secrets.dat");

    static Dictionary<string, string> Load()
    {
        if (_cache != null) return _cache;
        try
        {
            if (File.Exists(FilePath))
            {
                var plain = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.CurrentUser);
                _cache = JsonSerializer.Deserialize<Dictionary<string, string>>(plain) ?? new();
                return _cache;
            }
        }
        catch (Exception ex) { Log.Warn("secrets.dat could not be decrypted (different Windows user?): " + ex.Message); }
        return _cache = new();
    }

    public static string? Get(string name)
    {
        lock (Gate) return Load().TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;
    }

    public static void Set(string name, string? value)
    {
        lock (Gate)
        {
            var d = Load();
            if (string.IsNullOrWhiteSpace(value)) d.Remove(name); else d[name] = value.Trim();
            var blob = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(d), Entropy, DataProtectionScope.CurrentUser);
            AtomicFile.WriteAllBytes(FilePath, blob);
        }
    }

    public static bool Has(string name) => Get(name) != null;
}
