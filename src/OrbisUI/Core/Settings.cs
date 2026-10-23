using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrbisUI.Core;

public sealed class EmulatorSetting
{
    public string? ExePath { get; set; }
    /// <summary>Launch arguments. {rom} is the game path, {id} the title id.</summary>
    public string? Args { get; set; }
}

public sealed class GameOverride
{
    public bool Hidden { get; set; }
    public string? Title { get; set; }
    public string? Args { get; set; }
    public string? ArtPath { get; set; }
    public string? ReShadePreset { get; set; }
    public bool? Lossless { get; set; }
    public int? LosslessDelaySec { get; set; }
}

public sealed class AppFolder
{
    public string Name { get; set; } = "TV & Video";
    public List<AppEntry> Apps { get; set; } = new();
}

public sealed class AppEntry
{
    public string Name { get; set; } = "";
    /// <summary>Executable path, URL or steam:// link.</summary>
    public string Target { get; set; } = "";
    public string? Args { get; set; }
    public string? ArtPath { get; set; }
}

public sealed class AppSettings
{
    public int Version { get; set; } = 1;
    public bool SetupComplete { get; set; }

    // Directories
    public string? RomRoot { get; set; }
    public List<string> ExtraRomDirs { get; set; } = new();
    public string? EmulatorRoot { get; set; }
    public string? SteamPath { get; set; }
    public string? SteamId64 { get; set; }

    public Dictionary<string, EmulatorSetting> Emulators { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, GameOverride> Games { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<AppFolder> Folders { get; set; } = new()
    {
        new AppFolder
        {
            Name = "TV & Video",
            Apps =
            {
                new AppEntry { Name = "YouTube", Target = "https://www.youtube.com/tv" },
                new AppEntry { Name = "Netflix", Target = "https://www.netflix.com" },
            }
        }
    };

    // Presentation
    public bool BootAnimation { get; set; } = true;
    public bool ReducedMotion { get; set; }
    public bool HighContrast { get; set; }
    public bool AnimatedBackground { get; set; } = true;
    public bool UiSounds { get; set; } = true;
    public double UiVolume { get; set; } = 0.7;
    public bool HomeMusic { get; set; } = true;
    public double MusicVolume { get; set; } = 0.4;
    public bool ConfirmWithCircle { get; set; }

    // Steam
    public bool StartSteamSilently { get; set; } = true;
    public bool LaunchSteamGamesThroughSteam { get; set; } = true;

    // Lossless Scaling
    public bool LosslessEnabled { get; set; }
    public int LosslessDelaySec { get; set; } = 10;
    public string? LosslessPath { get; set; }
    public string LosslessHotkey { get; set; } = "Ctrl+Alt+S";

    // Updates
    public bool AutoCheckUpdates { get; set; } = true;
    public int UpdateCheckHours { get; set; } = 6;
    public DateTime LastUpdateCheck { get; set; }

    // Library
    public int LaunchTimeoutSec { get; set; } = 90;
    public string? LastSelectedId { get; set; }
    public Dictionary<string, long> LastPlayed { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore] public static AppSettings Current { get; private set; } = new();

    static string FilePath => Path.Combine(AppPaths.Config, "settings.json");

    internal static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOpts) ?? new();
        }
        catch (Exception ex)
        {
            // Keep the broken file for inspection instead of silently losing it.
            Log.Error("settings.json unreadable, starting with defaults", ex);
            try { File.Copy(FilePath, FilePath + ".broken", true); } catch { }
            Current = new();
        }
        Current.Emulators = new(Current.Emulators, StringComparer.OrdinalIgnoreCase);
        Current.Games = new(Current.Games, StringComparer.OrdinalIgnoreCase);
        Current.LastPlayed = new(Current.LastPlayed, StringComparer.OrdinalIgnoreCase);
        return Current;
    }

    public void Save() => AtomicFile.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOpts));

    public GameOverride For(string gameId)
    {
        if (!Games.TryGetValue(gameId, out var o)) Games[gameId] = o = new GameOverride();
        return o;
    }
}

public static class AtomicFile
{
    /// <summary>Write to a temp file then swap, so a crash never leaves a half-written file.</summary>
    public static void WriteAllText(string path, string text)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, text);
        File.Move(tmp, path, true);
    }

    public static void WriteAllBytes(string path, byte[] data)
    {
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, data);
        File.Move(tmp, path, true);
    }
}
