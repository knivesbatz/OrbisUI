using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OrbisUI.Audio;
using OrbisUI.Core;
using OrbisUI.Input;
using OrbisUI.Library;
using OrbisUI.Services;

namespace OrbisUI.UI;

public static class PowerMenu
{
    public static void Show()
    {
        Shell.Push(new ListScreen("Power", new[]
        {
            new ListItem { Glyph = "", Label = "Close Application", Sub = () => Shell.Launcher.Running?.Title,
                Enabled = () => Shell.Launcher.Running != null, Activate = () => { Shell.Pop(); Shell.Launcher.CloseGame(); } },
            new ListItem { Glyph = "", Label = "Enter Rest Mode", Sub = () => "Sleep the device", Activate = () => Confirmed("Enter rest mode?", () => Run("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0")) },
            new ListItem { Glyph = "", Label = "Turn Off", Activate = () => Confirmed("Turn off the device?", () => Run("shutdown.exe", "/s /t 0")) },
            new ListItem { Glyph = "", Label = "Restart", Activate = () => Confirmed("Restart the device?", () => Run("shutdown.exe", "/r /t 0")) },
            new ListItem { Glyph = "", Label = "Exit to Windows Desktop", Activate = () => { Shell.Pop(); Shell.Window.ExitToDesktop(); } },
            new ListItem { Glyph = "", Label = "Open Steam Big Picture", Activate = () => { Shell.Pop(); Steam.OpenUrl(Steam.Links.BigPicture); } },
        }, dialog: true));
    }

    static void Confirmed(string q, Action a) { Shell.Pop(); Shell.Confirm("Power", q, a); }

    static void Run(string exe, string args)
    {
        AppSettings.Current.Save();
        try { Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true }); }
        catch (Exception ex) { Log.Warn("power: " + ex.Message); }
    }
}

/// <summary>Options menu for one game (Options button on its tile).</summary>
public static class GameMenu
{
    public static void Show(Game g)
    {
        var items = new List<ListItem>
        {
            new() { Glyph = "", Label = "Start", Activate = () => { Shell.Pop(); Shell.Window.StartGame(g); } },
            new() { Glyph = "", Label = "Information", Activate = () => { Shell.Pop(); Shell.Push(InfoScreen(g)); } },
            new() { Glyph = "\uEB9F", Label = "Change Artwork", Activate = () => { Shell.Pop(); Shell.Push(new ArtPickerScreen(g)); } },
            new() { Glyph = "", Label = "Game Settings", Activate = () => { Shell.Pop(); Shell.Push(GameSettings(g)); } },
        };
        if (g.Kind == LaunchKind.Steam)
            items.Add(new() { Glyph = "", Label = "Uninstall", Sub = () => "Steam will ask to confirm",
                Activate = () => { Shell.Pop(); Steam.OpenUrl(Steam.Links.Uninstall(g.SteamAppId!)); } });
        if (g.Id.StartsWith("exe:"))
            items.Add(new() { Glyph = "", Label = "Remove from Library", Activate = () => { Shell.Pop(); Shell.Confirm("Remove", $"Remove {g.Title} from the library? Files are not deleted.", () => { Shell.Library.RemoveManual(g.Id); _ = Shell.Library.RescanAsync(); }); } });
        items.Add(new() { Glyph = "", Label = "Hide from Home", Activate = () => { Shell.Pop(); AppSettings.Current.For(g.Id).Hidden = true; AppSettings.Current.Save(); Shell.Library.NotifyChanged(); } });
        Shell.Push(new ListScreen(LibraryService.DisplayTitle(g), items, dialog: true));
    }

    public static Screen InfoScreen(Game g)
    {
        string Size(long b) => b <= 0 ? "" : b > 1L << 30 ? $"{b / (double)(1L << 30):0.0} GB" : $"{b / (double)(1L << 20):0} MB";
        var items = new List<ListItem>
        {
            new() { Glyph = "", Label = "Platform", Value = () => Tiles.PlatformLabel(g.Platform) },
            new() { Glyph = "", Label = "Launches with", Value = () => g.Kind switch { LaunchKind.Steam or LaunchKind.SteamShortcut => "Steam", LaunchKind.Emulator => Emulators.Get(g.Platform)?.Name, _ => "Windows" } },
            new() { Glyph = "", Label = "Released", Value = () => g.Year ?? "Unknown" },
            new() { Glyph = "", Label = "Publisher", Value = () => g.Publisher ?? "Unknown" },
            new() { Glyph = "", Label = "Genre", Value = () => g.Genres ?? "Unknown" },
            new() { Glyph = "", Label = "Size", Value = () => Size(g.SizeBytes) },
            new() { Glyph = "", Label = "Artwork source", Value = () => g.ArtSource ?? "Generated tile" },
            new() { Glyph = "", Label = "Details source", Value = () => g.MetaSource ?? "None found" },
            new() { Glyph = "", Label = "Location", Sub = () => g.Path ?? g.InstallDir ?? g.SteamAppId },
        };
        if (!string.IsNullOrEmpty(g.Description))
            items.Insert(0, new() { Glyph = "", Label = "About", Sub = () => g.Description!.Length > 160 ? g.Description[..160] + "…" : g.Description });
        return new ListScreen(LibraryService.DisplayTitle(g), items);
    }

    public static Screen GameSettings(Game g)
    {
        var o = AppSettings.Current.For(g.Id);
        void Save() { AppSettings.Current.Save(); }
        var items = new List<ListItem>
        {
            new() { Glyph = "", Label = "Name", Value = () => LibraryService.DisplayTitle(g),
                Activate = () => Osk.Ask("Name", LibraryService.DisplayTitle(g), v => { o.Title = string.IsNullOrWhiteSpace(v) || v == g.Title ? null : v; Save(); Shell.Library.NotifyChanged(); }) },
            new() { Glyph = "", Label = "Refresh Artwork and Information", Activate = async () =>
                {
                    g.Art = null; g.Background = null; g.Description = null; g.MetaTried = false;
                    Shell.Toast("Refreshing", LibraryService.DisplayTitle(g), "");
                    await Shell.Metadata.ProcessAsync(g, true, CancellationToken.None);
                    Shell.Library.Save();
                } },
            new() { Glyph = "\uEB9F", Label = "Change Artwork", Sub = () => "Browse SteamGridDB or use an image file", Activate = () => Shell.Push(new ArtPickerScreen(g)) },
            new() { Glyph = "", Label = "Lossless Scaling", Value = () => o.Lossless switch { true => "On", false => "Off", null => AppSettings.Current.LosslessEnabled ? "Default (On)" : "Default (Off)" },
                Adjust = d => { o.Lossless = o.Lossless switch { null => true, true => false, false => null }; Save(); } },
            new() { Glyph = "", Label = "Lossless Scaling Delay", Value = () => $"{o.LosslessDelaySec ?? AppSettings.Current.LosslessDelaySec} s",
                Adjust = d => { o.LosslessDelaySec = Math.Clamp((o.LosslessDelaySec ?? AppSettings.Current.LosslessDelaySec) + d * 5, 0, 120); Save(); } },
        };
        if (g.Kind is LaunchKind.Emulator or LaunchKind.Exe)
            items.Insert(1, new() { Glyph = "", Label = "Launch Arguments", Sub = () => o.Args ?? (g.Kind == LaunchKind.Emulator ? Emulators.Args(g.Platform) + "  (default)" : "None"),
                Activate = () => Osk.Ask("Launch Arguments", o.Args ?? (g.Kind == LaunchKind.Emulator ? Emulators.Args(g.Platform) : ""), v => { o.Args = string.IsNullOrWhiteSpace(v) ? null : v; Save(); }) });
        items.Add(new() { Glyph = "\uE8F1", Label = "Versions", Value = () => o.Versions is { Count: > 0 } v ? $"{v.Count + 1}" : "1",
            Sub = () => "Add other exes or shortcuts; you'll choose one before the game starts", Activate = () => Shell.Push(VersionsScreen(g)) });
        items.Add(new() { Glyph = "\uE790", Label = "ReShade Preset", Value = () => o.ReShadePreset is string p ? Path.GetFileNameWithoutExtension(p) : "Default",
            Activate = () => Shell.Push(ReShadeScreen(g)) });
        if (g.Kind == LaunchKind.Emulator && o.Args != null)
            items.Add(new() { Glyph = "", Label = "Reset Launch Arguments to Default", Activate = () => { o.Args = null; Save(); } });
        return new ListScreen("Game Settings", items, subtitle: LibraryService.DisplayTitle(g));
    }

    public static readonly string[] GameFileTypes = { ".exe", ".lnk", ".url" };

    /// <summary>Extra versions of a game (another exe, a shortcut or a Store app). Cross on one removes it.</summary>
    static Screen VersionsScreen(Game g)
    {
        var o = AppSettings.Current.For(g.Id);
        void Reopen() { Shell.Pop(); Shell.Push(VersionsScreen(g)); }
        void Add(string t) { (o.Versions ??= new()).Add(t); AppSettings.Current.Save(); Reopen(); }
        var items = new List<ListItem> { new() { Glyph = "", Label = "Main Version", Sub = () => g.Path ?? g.SteamAppId } };
        foreach (var v in o.Versions ?? new())
            items.Add(new() { Glyph = "", Label = VersionName(v), Sub = () => v + "   ·   Cross to remove",
                Activate = () => Shell.Confirm("Remove Version", $"Remove {VersionName(v)}? No files are deleted.", () => { o.Versions!.Remove(v); if (o.Versions.Count == 0) o.Versions = null; AppSettings.Current.Save(); Reopen(); }) });
        items.Add(new() { Glyph = "", Label = "Add an Exe or Shortcut…", Activate = () => FilePicker.Pick("Add a Version", GameFileTypes, Add, g.InstallDir) });
        items.Add(new() { Glyph = "", Label = "Add a Microsoft Store App…", Activate = () => PickStoreApp((_, t) => Add(t)) });
        return new ListScreen("Versions", items, subtitle: LibraryService.DisplayTitle(g));
    }

    public static string VersionName(string target) =>
        target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) ? target[(target.LastIndexOf('\\') + 1)..].Split('_')[0] : Path.GetFileNameWithoutExtension(target);

    /// <summary>The version picker shown before a game with extra versions starts.</summary>
    public static void PickVersion(Game g, Action<Game> start)
    {
        var o = AppSettings.Current.For(g.Id);
        var items = new List<ListItem> { new() { Glyph = "", Label = "Main Version", Sub = () => g.Path ?? g.Title, Activate = () => { Shell.Pop(); start(g); } } };
        foreach (var v in o.Versions!)
            items.Add(new() { Glyph = "", Label = VersionName(v), Sub = () => v, Activate = () =>
            {
                Shell.Pop();
                var vg = LibraryService.FromTarget(v, g.Title);
                vg.Id = g.Id; vg.Art = g.Art; vg.Background = g.Background; vg.BgSource = g.BgSource; vg.IsVersion = true;
                start(vg);
            } });
        Audio.Sound.Play(Audio.Sfx.DialogShow);
        Shell.Push(new ListScreen(LibraryService.DisplayTitle(g), items, dialog: true, subtitle: "Choose the version to start."));
    }

    public static void PickStoreApp(Action<string, string> chosen)
    {
        Shell.Toast("Microsoft Store Apps", "Loading the installed apps…", "");
        _ = Task.Run(async () =>
        {
            List<(string name, string target)> apps;
            try { apps = await LibraryService.StoreAppsAsync(); }
            catch (Exception ex) { Shell.Window.Dispatcher.Invoke(() => Shell.Message("Microsoft Store Apps", "The app list couldn't be read: " + ex.Message)); return; }
            Shell.Window.Dispatcher.Invoke(() => Shell.Push(new ListScreen("Add a Microsoft Store App",
                apps.Select(a => new ListItem { Glyph = "", Label = a.name, Activate = () => { Shell.Pop(); chosen(a.name, a.target); } }),
                subtitle: $"{apps.Count} installed apps")));
        });
    }

    /// <summary>Pick the ReShade preset this game starts with. Applied to ReShade.ini right before launch (backed up first).</summary>
    static Screen ReShadeScreen(Game g)
    {
        var o = AppSettings.Current.For(g.Id);
        var (state, ini) = ReShade.Check(g);
        if (state != ReShade.State.Ready)
            return new ListScreen("ReShade Preset", new[] { new ListItem { Glyph = "", Label = state == ReShade.State.AntiCheat
                ? "This game uses anti-cheat, so OrbisUI won't change its ReShade files."
                : "ReShade isn't installed for this game. Install ReShade into its folder first." } }, dialog: true);
        var items = new List<ListItem>
        {
            new() { Label = "Default", Sub = () => "The preset ReShade had before OrbisUI changed it", Check = () => o.ReShadePreset == null,
                Activate = () => { o.ReShadePreset = null; AppSettings.Current.Save(); ReShade.ApplyFor(g); Shell.Pop(); } },
        };
        foreach (var p in ReShade.Presets(ini!))
            items.Add(new() { Label = Path.GetFileNameWithoutExtension(p), Sub = () => p, Check = () => string.Equals(o.ReShadePreset, p, StringComparison.OrdinalIgnoreCase),
                Activate = () => { o.ReShadePreset = p; AppSettings.Current.Save(); Shell.Pop(); } });
        return new ListScreen("ReShade Preset", items, subtitle: $"Presets next to the game and in {ReShade.SharedPresets}");
    }
}

public sealed class NotificationsScreen : ListScreen
{
    public NotificationsScreen() : base("Notifications", Items()) => Notifications.MarkRead();

    static IEnumerable<ListItem> Items()
    {
        if (Notifications.Items.Count == 0)
            return new[] { new ListItem { Glyph = "", Label = "There are no notifications." } };
        return Notifications.Items.Select(n => new ListItem
        {
            Glyph = n.Glyph, Label = n.Title,
            Sub = () => string.IsNullOrEmpty(n.Body) ? null : n.Body,
            Value = () => Ago(n.When),
        });
    }

    static string Ago(DateTime t)
    {
        var d = DateTime.Now - t;
        return d.TotalMinutes < 1 ? "Just now" : d.TotalHours < 1 ? $"{(int)d.TotalMinutes} Minutes Ago" : d.TotalDays < 1 ? $"{(int)d.TotalHours} Hours Ago" : t.ToString("MMM d");
    }
}

/// <summary>A home folder (TV &amp; Video etc.): a row of app tiles.</summary>
public sealed class FolderScreen : ListScreen
{
    public FolderScreen(AppFolder f) : base(f.Name, Items(f), hints: new[] { (Btn.Cross, "Open"), (Btn.Circle, "Back"), (Btn.Options, "Options") })
    {
        OnOptions = it =>
        {
            var app = f.Apps.FirstOrDefault(a => a.Name == it.Label);
            if (app == null) return;
            Shell.Push(AppOptions(f, app, () => { Shell.Pop(); Shell.Push(new FolderScreen(f)); }));
        };
    }

    /// <summary>Options for a TV &amp; Video app: open, change its art (SteamGridDB or an image file), remove.</summary>
    public static Screen AppOptions(AppFolder f, AppEntry app, Action removed) => new ListScreen(app.Name, new[]
    {
        new ListItem { Glyph = "\uE768", Label = "Open", Activate = () => { Shell.Pop(); Shell.Window.StartApp(app); } },
        new ListItem { Glyph = "\uEB9F", Label = "Change Artwork", Sub = () => "SteamGridDB or an image file", Activate = () => { Shell.Pop(); Shell.Push(new ArtPickerScreen(app)); } },
        new ListItem { Glyph = "\uE8AC", Label = "Rename", Activate = () => { Shell.Pop(); Osk.Ask("Rename", app.Name, n =>
            { if (string.IsNullOrWhiteSpace(n)) return; app.Name = n.Trim(); AppSettings.Current.Save(); Shell.Library.NotifyChanged(); removed(); }); } },
        new ListItem { Glyph = "\uE74D", Label = $"Remove from {f.Name}", Activate = () => Shell.Confirm("Remove", $"Remove {app.Name} from {f.Name}?", () =>
            { f.Apps.Remove(app); AppSettings.Current.Save(); Shell.Pop(); removed(); }) },
    }, dialog: true);

    /// <summary>Adds an app and fetches square art for it in the background.</summary>
    public static void Add(AppFolder f, AppEntry a)
    {
        f.Apps.Add(a);
        AppSettings.Current.Save();
        _ = FetchArt(a);
    }

    public static async Task FetchArt(AppEntry a)
    {
        if (a.ArtPath != null && File.Exists(a.ArtPath)) return;
        var file = await Shell.Metadata.AppArtAsync(a.Name);
        if (file == null) return;
        a.ArtPath = file;
        AppSettings.Current.Save();
        Shell.Library.NotifyChanged();
    }
    static IEnumerable<ListItem> Items(AppFolder f)
    {
        foreach (var a in f.Apps)
            yield return new ListItem
            {
                Glyph = a.Target.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? "" : "", Label = a.Name, Sub = () => a.Target,
                Activate = () => Shell.Window.StartApp(a),
            };
        yield return new ListItem { Glyph = "", Label = "Add Application…", Sub = () => "Choose a program with the file browser",
            Activate = () => FilePicker.Pick("Add Application", new[] { ".exe", ".lnk", ".url", ".bat" }, path =>
            {
                Add(f, new AppEntry { Name = Path.GetFileNameWithoutExtension(path), Target = path });
                AppSettings.Current.Save();
                Shell.Pop(); Shell.Push(new FolderScreen(f));
            }) };
    }
}
