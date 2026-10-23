namespace OrbisUI.Library;

public enum LaunchKind { Steam, SteamShortcut, Exe, Emulator, Url }

public sealed class Game
{
    /// <summary>Stable id: steam:APPID, sc:APPID, emu:SYSTEM:HASH, exe:HASH.</summary>
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>PC, PS1, PS2, PS3, PS4, PSP, VITA.</summary>
    public string Platform { get; set; } = "PC";
    public LaunchKind Kind { get; set; }
    public string? SteamAppId { get; set; }
    /// <summary>Exe, ROM file, game folder (PS3/PS4) or Vita title id.</summary>
    public string? Path { get; set; }
    public string? Args { get; set; }
    public string? WorkDir { get; set; }
    public string? TitleId { get; set; }
    public string? InstallDir { get; set; }

    // Metadata (filled by MetadataService, cached in library.json)
    public string? Description { get; set; }
    public string? Publisher { get; set; }
    public string? Year { get; set; }
    public string? Genres { get; set; }
    public string? MetaSource { get; set; }
    public string? ArtSource { get; set; }
    public bool MetaTried { get; set; }
    public long SizeBytes { get; set; }

    /// <summary>Local square tile and background image (absolute paths inside Cache/Art).</summary>
    public string? Art { get; set; }
    public string? Background { get; set; }

    /// <summary>Local art shipped with the game (PS4/Vita icon0.png, Steam library cache, emulator covers).</summary>
    public string? LocalArtHint { get; set; }
    public string? LocalBgHint { get; set; }

    public override string ToString() => $"{Platform} {Title}";
}
