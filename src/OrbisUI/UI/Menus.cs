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
        if (g.Kind == LaunchKind.Exe)
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
        if (g.Kind == LaunchKind.Emulator && o.Args != null)
            items.Add(new() { Glyph = "", Label = "Reset Launch Arguments to Default", Activate = () => { o.Args = null; Save(); } });
        return new ListScreen("Game Settings", items, subtitle: LibraryService.DisplayTitle(g));
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
