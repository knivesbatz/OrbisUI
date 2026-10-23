namespace OrbisUI.Core;

/// <summary>Portable directory layout. Everything lives next to the executable unless ORBISUI_ROOT overrides it.</summary>
public static class AppPaths
{
    public static string Root { get; private set; } = ResolveRoot();

    static string ResolveRoot()
    {
        var env = Environment.GetEnvironmentVariable("ORBISUI_ROOT");
        return !string.IsNullOrWhiteSpace(env) ? env : AppContext.BaseDirectory.TrimEnd('\\', '/');
    }

    /// <summary>Tests point the app at a throwaway folder.</summary>
    internal static void OverrideRoot(string root) => Root = root;

    static string Dir(string rel)
    {
        var p = Path.Combine(Root, rel);
        Directory.CreateDirectory(p);
        return p;
    }

    public static string Config => Dir("Config");
    public static string Cache => Dir("Cache");
    public static string Art => Dir(Path.Combine("Cache", "Art"));
    public static string ArtOriginals => Dir(Path.Combine("Cache", "Art", "Originals"));
    public static string Logs => Dir("Logs");
    public static string Sounds => Dir(Path.Combine("Data", "Sounds"));
    public static string Media => Dir(Path.Combine("Data", "Media"));
    /// <summary>Optional user-supplied media (boot.mp4, background.mp4, music.mp3); null when absent.</summary>
    public static string? MediaFile(string name) => File.Exists(Path.Combine(Media, name)) ? Path.Combine(Media, name) : null;
    public static string Downloads => Dir("Downloads");
    public static string Backups => Dir("Backups");
    public static string DefaultGames => Dir("Games");
    public static string DefaultFirmware => Dir("Firmware");
    public static string DefaultEmulators => Dir("Emulators");
}
