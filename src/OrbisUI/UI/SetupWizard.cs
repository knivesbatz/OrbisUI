using System.Windows;
using OrbisUI.Audio;
using OrbisUI.Core;
using OrbisUI.Input;
using OrbisUI.Library;

namespace OrbisUI.UI;

/// <summary>First-run setup, fully controller operable. Every step can be skipped; nothing online is required.</summary>
public sealed class SetupWizard : Screen
{
    readonly bool _rerun;
    int _step;
    ListScreen? _page;
    string? _soundDir = SoundPack.AutoFind();
    bool _importSounds = true;
    readonly HashSet<string> _offeredDirs = new(StringComparer.OrdinalIgnoreCase);

    static AppSettings S => AppSettings.Current;

    public SetupWizard(bool rerun = false)
    {
        _rerun = rerun;
        Emulators.AutoDetect();
        foreach (var d in RomScanner.Pcsx2GameDirs()) if (!S.ExtraRomDirs.Any(x => x.EndsWith("|" + d, StringComparison.OrdinalIgnoreCase))) _offeredDirs.Add(d);
        Show();
    }

    void Show()
    {
        if (_page != null) Children.Remove(_page);
        _page = _step switch
        {
            0 => Welcome(), 1 => SystemCheck(), 2 => Folders(), 3 => SoundStep(), 4 => Services(), _ => Preferences(),
        };
        _page.OnBack = Back;
        Children.Add(_page);
        UiKit.FadeIn(_page, 200);
    }

    void Next() { _step++; Play(Sfx.Enter); Show(); }
    void Back()
    {
        if (_step == 0) { if (_rerun) Shell.Pop(); return; }
        _step--; Show();
    }

    ListItem Continue(string label = "Continue") => new() { Glyph = "", Label = label, Activate = Next };

    ListScreen Welcome() => new("Welcome", new[]
    {
        new ListItem { Glyph = "", Label = "Set Up OrbisUI", Sub = () => "Folders, emulators, sounds and optional online services", Activate = Next },
        new ListItem { Glyph = "", Label = "Skip and Use Defaults", Sub = () => "You can run setup again from Settings > System", Activate = Finish },
    }, subtitle: "This takes about a minute. Use the D-pad and ✕, or the arrow keys and Enter.");

    ListScreen SystemCheck()
    {
        var steam = Steam.FindInstall();
        var drive = new DriveInfo(Path.GetPathRoot(AppPaths.Root)!);
        var items = new List<ListItem>
        {
            new() { Glyph = "", Label = "Windows", Value = () => Environment.OSVersion.Version.Build >= 22000 ? $"Windows 11 (build {Environment.OSVersion.Version.Build})" : $"Windows 10 (build {Environment.OSVersion.Version.Build})" },
            new() { Glyph = "", Label = "Display", Value = () => $"{(int)SystemParameters.PrimaryScreenWidth} × {(int)SystemParameters.PrimaryScreenHeight} (scaled)" },
            new() { Glyph = "", Label = "Free Space", Value = () => $"{drive.AvailableFreeSpace / (1L << 30)} GB on {drive.Name}" },
            new() { Glyph = "", Label = "Controller", Value = () => Shell.Pad?.PadName ?? "None connected (keyboard works too)" },
            new() { Glyph = "", Label = "Steam", Value = () => steam == null ? "Not installed. Steam games won't appear." : $"Found: {Steam.CurrentUser(steam).persona ?? "signed out"}" },
            new() { IsHeader = true, Label = "Emulators" },
        };
        foreach (var def in Emulators.All)
            items.Add(new() { Glyph = Emulators.ExePath(def.System) != null ? "" : "", Label = $"{Tiles.PlatformLabel(def.System)}  ·  {def.Name}",
                Value = () => Emulators.ExePath(def.System) != null ? "Ready" : "Not found", Sub = () => Emulators.ExePath(def.System) });
        items.Add(Continue());
        return new ListScreen("System Check", items, select: items.Count - 1);
    }

    ListScreen Folders()
    {
        var items = new List<ListItem>
        {
            new() { Glyph = "", Label = "ROM Folder", Sub = () => (S.RomRoot ?? AppPaths.DefaultGames) + "\nPut games in subfolders named PS1, PS2, PS3, PS4, PSP, VITA",
                Activate = () => FilePicker.Pick("ROM Folder", null, d => { S.RomRoot = d; Refresh(); }) },
            new() { Glyph = "", Label = "Emulators Folder", Sub = () => S.EmulatorRoot ?? "Detected automatically",
                Activate = () => FilePicker.Pick("Emulators Folder", null, d => { S.EmulatorRoot = d; Emulators.AutoDetect(); Refresh(); }) },
        };
        foreach (var d in _offeredDirs)
        {
            var entry = "PS2|" + d;
            items.Add(new() { Glyph = "", Label = $"Use PCSX2's game folder {d}", Sub = () => "Found in your PCSX2 settings (read only)",
                Check = () => S.ExtraRomDirs.Contains(entry), Activate = () => { if (!S.ExtraRomDirs.Remove(entry)) S.ExtraRomDirs.Add(entry); } });
        }
        foreach (var def in Emulators.All)
            items.Add(new() { Glyph = "", Label = $"Add Another {Tiles.PlatformLabel(def.System)} Folder…",
                Activate = () => FilePicker.Pick($"{Tiles.PlatformLabel(def.System)} Folder", null, d => { S.ExtraRomDirs.Add($"{def.System}|{d}"); Refresh(); }) });
        items.Add(Continue());
        return new ListScreen("Game Folders", items, subtitle: "Folders are only read. Nothing in them is changed.");
    }

    void Refresh() => Dispatcher.BeginInvoke(() => { int s = _page?.Selected ?? 0; Show(); _page?.Select(s); });

    ListScreen SoundStep() => new("Sounds", new[]
    {
        new ListItem { Glyph = "", Label = _soundDir != null ? "Import the Console Sound Pack" : "Choose a Sound Pack Folder",
            Sub = () => _soundDir ?? "A folder with snd_*.wav files. Without one, built-in tones are used.",
            Check = _soundDir != null ? () => _importSounds : null,
            Activate = () => { if (_soundDir != null) _importSounds = !_importSounds; else FilePicker.Pick("Sound Pack Folder", null, d => { _soundDir = d; Refresh(); }); } },
        new ListItem { Glyph = "", Label = "UI Sounds", Check = () => S.UiSounds, Activate = () => S.UiSounds = !S.UiSounds },
        Continue(),
    }, select: 2);

    ListScreen Services() => new("Online Services (Optional)", new[]
    {
        SettingsScreensAccess.Key("SteamGridDB API Key", "Square artwork for every game. Free at steamgriddb.com > Preferences > API", Core.Secrets.SteamGridDb),
        SettingsScreensAccess.Key("IGDB Client ID", "Details for console games. Free at dev.twitch.tv > Applications", Core.Secrets.IgdbClientId),
        SettingsScreensAccess.Key("IGDB Client Secret", "Pairs with the Client ID", Core.Secrets.IgdbClientSecret),
        Continue("Continue (keys can be added later)"),
    }, subtitle: "Keys are encrypted for your Windows account and never written to plain files.", select: 3);

    ListScreen Preferences() => new("Preferences", new[]
    {
        new ListItem { Glyph = "", Label = "Startup Animation", Check = () => S.BootAnimation, Activate = () => S.BootAnimation = !S.BootAnimation },
        new ListItem { Glyph = "", Label = "Start Steam in the Background", Check = () => S.StartSteamSilently, Activate = () => S.StartSteamSilently = !S.StartSteamSilently },
        new ListItem { Glyph = "", Label = "Lossless Scaling for Games", Sub = () => OrbisUI.Services.LosslessScaling.FindExe() == null ? "Lossless Scaling isn't installed" : "Turns on 10 seconds after a game appears",
            Check = () => S.LosslessEnabled, Activate = () => S.LosslessEnabled = !S.LosslessEnabled },
        new ListItem { Glyph = "", Label = "Finish", Activate = Finish },
    }, select: 3);

    void Finish()
    {
        if (_importSounds && _soundDir != null && !Sound.HasPack) SoundPack.Import(_soundDir);
        S.SetupComplete = true;
        S.Save();
        Play(Sfx.Yes);
        Log.Info("Setup finished");
        if (_rerun) { Shell.Pop(); Shell.Window.Rescan(); }
        else Shell.Window.EnterHome();
    }

    public override bool OnInput(Btn b) => _page?.OnInput(b) ?? false;
    public override void OnResumed() => _page?.Refresh();
}

/// <summary>Bridges the API-key editor from Settings into the wizard.</summary>
static class SettingsScreensAccess
{
    public static ListItem Key(string label, string help, string name) => new()
    {
        Glyph = "", Label = label, Sub = () => help, Value = () => Core.Secrets.Has(name) ? "Saved" : "Not set",
        Activate = () => Osk.Ask(label, "", v => { if (!string.IsNullOrWhiteSpace(v)) Core.Secrets.Set(name, v.Trim()); }, secret: true),
    };
}
