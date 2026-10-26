using OrbisUI.Audio;
using OrbisUI.Core;
using OrbisUI.Library;
using OrbisUI.Metadata;
using OrbisUI.Services;

namespace OrbisUI.UI;

public static class SettingsScreens
{
    static AppSettings S => AppSettings.Current;
    static void Save() => S.Save();

    public static Screen Root(int select = 0) => new ListScreen("Settings", new[]
    {
        new ListItem { Glyph = "\uE8F1", Label = "Library", Activate = () => Shell.Push(Library()) },
        new ListItem { Glyph = "\uE7F4", Label = "TV & Video", Activate = () => Shell.Push(new FolderScreen(S.Folders[0])) },
        new ListItem { Glyph = "\uE7FC", Label = "Emulators", Activate = () => Shell.Push(EmulatorsScreen()) },
        new ListItem { Glyph = "\uE7F6", Label = "Sound and Screen", Activate = () => Shell.Push(SoundScreen()) },
        new ListItem { Glyph = "\uE790", Label = "Themes", Activate = () => Shell.Push(Theme.Picker()) },
        new ListItem { Glyph = "\uE701", Label = "Network and Devices", Activate = () => Shell.Push(DevicesScreen()) },
        new ListItem { Glyph = "\uE776", Label = "Accessibility", Activate = () => Shell.Push(AccessibilityScreen()) },
        new ListItem { Glyph = "\uE8D7", Label = "Accounts and Services", Activate = () => Shell.Push(AccountsScreen()) },
        new ListItem { Glyph = "\uE7B3", Label = "Lossless Scaling", Activate = () => Shell.Push(LosslessScreen()) },
        new ListItem { Glyph = "\uE7FC", Label = "Controllers", Activate = () => Shell.Push(new ControllerTestScreen()) },
        new ListItem { Glyph = "\uE7FC", Label = "Controller Fixer", Activate = () => Shell.Push(FixerGames()) },
        new ListItem { Glyph = "\uE895", Label = "System Software Update", Activate = () => Shell.Push(UpdateScreen()) },
        new ListItem { Glyph = "\uE8B7", Label = "File Manager", Activate = () => FileManager.Open() },
        new ListItem { Glyph = "\uE770", Label = "System", Activate = () => Shell.Push(SystemScreen()) },
    }, select: select);

    // ---- Controller Fixer: PC games only (emulated games already show PlayStation buttons) ----
    static Screen FixerGames()
    {
        var games = Shell.Library.Games.Where(g => g.Kind is LaunchKind.Steam or LaunchKind.Exe && ControllerFixer.GameFolder(g) != null)
            .OrderBy(LibraryService.DisplayTitle).ToList();
        if (games.Count == 0) return new ListScreen("Controller Fixer", new[] { new ListItem { Glyph = "\uE946", Label = "No installed PC games were found." } }, dialog: true);
        return new ListScreen("Controller Fixer", games.Select(g => new ListItem
        {
            Label = LibraryService.DisplayTitle(g), Sub = () => ControllerFixer.History(g.Id).Count > 0 ? "Mod installed" : null,
            Activate = () => Shell.Push(FixerGame(g)),
        }), subtitle: "Choose a game to give it PlayStation button prompts.");
    }

    static Screen FixerGame(Game g)
    {
        var title = LibraryService.DisplayTitle(g);
        var items = ControllerFixer.Searches(title).Select(s => new ListItem
        {
            Glyph = "\uE721", Label = s.label, Sub = () => "Opens in the browser. Save the mod to your Downloads folder.", Activate = () => Shell.Push(new BrowserScreen(s.url)),
        }).ToList();
        items.Add(new ListItem
        {
            Glyph = "\uE896", Label = "Install Downloaded Mod", Sub = () => "Pick the .zip, .7z or .rar. Files it replaces are backed up first.",
            Activate = () => FilePicker.Pick("Choose the Mod", ControllerFixer.ArchiveTypes, archive =>
            {
                var target = ControllerFixer.GameFolder(g)!;
                try
                {
                    var root = ControllerFixer.ContentRoot(ControllerFixer.Stage(archive), target);
                    var files = ControllerFixer.Files(root);
                    Shell.Confirm("Install Mod", $"Copy {files.Length} files from {Path.GetFileName(archive)} into {target}? You can undo this later.", () =>
                    {
                        try
                        {
                            var inst = ControllerFixer.Apply(g.Id, archive, root, target);
                            Shell.Message("Controller Fixer", $"Installed: {inst.Added.Count} new files, {inst.Replaced.Count} replaced (backed up).");
                        }
                        catch (Exception ex) { Shell.Message("Controller Fixer", "The mod wasn't installed and the game was restored: " + ex.Message); }
                    });
                }
                catch (Exception ex) { Shell.Message("Controller Fixer", ex.Message); }
            }, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads"),
        });
        foreach (var (inst, dir) in ControllerFixer.History(g.Id))
            items.Add(new ListItem
            {
                Glyph = "\uE7A7", Label = $"Undo {Path.GetFileName(inst.Archive)}", Sub = () => $"Installed {inst.When:g}",
                Activate = () => Shell.Confirm("Undo Mod", "Remove the mod's files and put the originals back?", () =>
                {
                    try { ControllerFixer.Undo(inst, dir); Shell.Pop(); Shell.Message("Controller Fixer", "The game's original files are back."); }
                    catch (Exception ex) { Shell.Message("Controller Fixer", ex.Message); }
                }),
            });
        return new ListScreen("Controller Fixer", items, subtitle: title);
    }

    static Screen Library() => new ListScreen("Library", new[]
    {
        new ListItem { Glyph = "\uE72C", Label = "Rescan Library Now", Sub = () => $"{Shell.Library.Games.Count} games found", Activate = () => Shell.Window.Rescan() },
        new ListItem { Glyph = "\uE7FC", Label = "Add Games to Steam Automatically", Sub = () => "Emulated and added games appear in Steam with SteamGridDB art, before Steam starts",
            Check = () => S.AddGamesToSteam, Activate = () => { S.AddGamesToSteam = !S.AddGamesToSteam; Save(); } },
        new ListItem { Glyph = "\uE895", Label = "Add Games to Steam Now", Sub = () => "Closes and restarts Steam if it is running",
            Activate = async () => { Shell.Toast("Steam", "Adding your games to Steam…", "\uE895"); Shell.Message("Steam", await SteamShortcuts.SyncNowAsync(Shell.Library.Games.ToList())); } },
        new ListItem { Glyph = "\uE8B9", Label = "Refresh All Artwork and Information", Activate = () =>
            {
                foreach (var g in Shell.Library.Games) { g.MetaTried = false; }
                Shell.Toast("Refreshing artwork", "This runs in the background.", "\uE8B9");
                _ = Shell.Metadata.RunAsync(force: false);
            } },
        new ListItem { Glyph = "\uE8B7", Label = "ROM Folder", Sub = () => (S.RomRoot ?? AppPaths.DefaultGames) + "  ·  subfolders PS1, PS2, PS3, PS4, PSP, VITA",
            Activate = () => FilePicker.Pick("ROM Folder", null, d => { S.RomRoot = d; Save(); Shell.Window.Rescan(); }) },
        new ListItem { Glyph = "\uE8F4", Label = "Extra Game Folders", Sub = () => S.ExtraRomDirs.Count == 0 ? "None" : string.Join("   ", S.ExtraRomDirs.Select(x => x.Replace("|", ": "))),
            Activate = () => Shell.Push(ExtraFolders()) },
        new ListItem { Glyph = "\uE710", Label = "Add a PC Game", Sub = () => "Pick its .exe or a shortcut (.lnk, .url)", Activate = () => FilePicker.Pick("Add a PC Game", GameMenu.GameFileTypes, f => { Shell.Library.AddManual(f); Shell.Window.Rescan(); }) },
        new ListItem { Glyph = "\uE719", Label = "Add a Microsoft Store App", Sub = () => "Xbox app and other Store games", Activate = () => GameMenu.PickStoreApp((name, t) => { Shell.Library.AddManual(t, name); Shell.Window.Rescan(); }) },
        new ListItem { Glyph = "\uE890", Label = "Show Hidden Games", Activate = () => Shell.Push(new LibraryScreen()) },
    });

    static Screen ExtraFolders()
    {
        var items = S.ExtraRomDirs.Select(e => new ListItem
        {
            Glyph = "\uE8B7", Label = e.Split('|').Last(), Value = () => e.Split('|')[0], Sub = () => "Press ✕ to remove",
            Activate = () => Shell.Confirm("Remove Folder", "Stop scanning this folder? Nothing is deleted.", () => { S.ExtraRomDirs.Remove(e); Save(); Shell.Pop(); Shell.Push(ExtraFolders()); Shell.Window.Rescan(); }),
        }).ToList();
        foreach (var def in Emulators.All)
            items.Add(new ListItem
            {
                Glyph = "\uE710", Label = $"Add {Tiles.PlatformLabel(def.System)} Folder…",
                Activate = () => FilePicker.Pick($"{Tiles.PlatformLabel(def.System)} Folder", null, d => { S.ExtraRomDirs.Add($"{def.System}|{d}"); Save(); Shell.Pop(); Shell.Push(ExtraFolders()); Shell.Window.Rescan(); }),
            });
        return new ListScreen("Extra Game Folders", items);
    }

    static Screen EmulatorsScreen() => new ListScreen("Emulators", Emulators.All.Select(def => new ListItem
    {
        Glyph = "\uE7FC", Label = $"{Tiles.PlatformLabel(def.System)}  ·  {def.Name}",
        Value = () => Emulators.ExePath(def.System) is string p ? Path.GetFileName(p) : "Not set up",
        Sub = () => Emulators.ExePath(def.System),
        Activate = () => Shell.Push(EmulatorScreen(def)),
    }).Append(new ListItem { Glyph = "\uE721", Label = "Detect Emulators Automatically", Activate = () =>
    {
        var found = Emulators.AutoDetect(); Save();
        Shell.Message("Emulators", found.Count == 0 ? "No new emulators were found. Choose them manually or put them in the Emulators folder." : "Found: " + string.Join(", ", found));
    } }));

    static Screen EmulatorScreen(EmulatorDef def)
    {
        EmulatorSetting E() { if (!S.Emulators.TryGetValue(def.System, out var e)) S.Emulators[def.System] = e = new(); return e; }
        return new ListScreen($"{def.Name} ({Tiles.PlatformLabel(def.System)})", new[]
        {
            new ListItem { Glyph = "\uE8E5", Label = "Emulator Program", Sub = () => Emulators.ExePath(def.System) ?? "Not set up",
                Activate = () => FilePicker.Pick($"Choose {def.Name}", new[] { ".exe" }, f => { E().ExePath = f; Save(); }) },
            new ListItem { Glyph = "\uE756", Label = "Launch Arguments", Sub = () => Emulators.Args(def.System),
                Activate = () => Osk.Ask("Launch Arguments ({rom} = game, {id} = title ID)", Emulators.Args(def.System), v => { E().Args = string.IsNullOrWhiteSpace(v) ? null : v; Save(); }) },
            new ListItem { Glyph = "\uE777", Label = "Reset Launch Arguments", Sub = () => def.DefaultArgs, Activate = () => { E().Args = null; Save(); } },
            new ListItem { Glyph = "\uE713", Label = $"Open {def.Name} Settings", Sub = () => "Controls, graphics and mods are configured in the emulator itself",
                Enabled = () => Emulators.ExePath(def.System) != null,
                Activate = () => Shell.Window.StartExternal(Emulators.ExePath(def.System)!, "") },
        });
    }

    static Screen SoundScreen() => new ListScreen("Sound and Screen", new[]
    {
        new ListItem { Glyph = "\uE995", Label = "UI Sound Volume", Value = () => $"{S.UiVolume * 100:0}%",
            Adjust = d => { S.UiVolume = Math.Clamp(Math.Round(S.UiVolume + d * 0.1, 1), 0, 1); Sound.SetVolume(S.UiVolume, S.UiSounds); Save(); Sound.Play(Sfx.Cursor); } },
        new ListItem { Glyph = "\uE8D6", Label = "Import Sound Pack", Sub = () => Sound.HasPack ? "Sound pack installed" : "Using built-in tones. Choose a folder of snd_*.wav files",
            Activate = () => FilePicker.Pick("Sound Pack Folder", null, d => { var n = SoundPack.Import(d); Shell.Message("Sound Pack", n > 0 ? $"Imported {n} sounds." : "No snd_*.wav files were found there."); }) },
        new ListItem { Glyph = "\uE768", Label = "Startup Animation", Check = () => S.BootAnimation, Activate = () => { S.BootAnimation = !S.BootAnimation; Save(); } },
        new ListItem { Glyph = "\uE790", Label = "Animated Background", Check = () => S.AnimatedBackground, Activate = () => { S.AnimatedBackground = !S.AnimatedBackground; Save(); Shell.Window.Background_.SetRunning(false); Shell.Window.Background_.SetRunning(true); } },
        new ListItem { Glyph = "\uE7F4", Label = "Display Settings", Sub = () => "Resolution and refresh rate", Activate = () => Shell.Push(DisplayScreen()) },
    });

    static Screen AccessibilityScreen() => new ListScreen("Accessibility", new[]
    {
        new ListItem { Glyph = "\uE7B3", Label = "Reduce Motion", Check = () => S.ReducedMotion, Activate = () => { S.ReducedMotion = !S.ReducedMotion; Save(); Shell.Window.Background_.SetRunning(!S.ReducedMotion); } },
        new ListItem { Glyph = "\uE706", Label = "High Contrast Text", Sub = () => "Applies to newly opened screens", Check = () => S.HighContrast, Activate = () => { S.HighContrast = !S.HighContrast; Save(); } },
        new ListItem { Glyph = "\uE776", Label = "Windows Accessibility Settings", Sub = () => "Text size, narrator, magnifier", Activate = () => WinSettings("ms-settings:easeofaccess") },
    });

    static Screen DevicesScreen() => new ListScreen("Network and Devices", new[]
    {
        new ListItem { Glyph = "\uE701", Label = "Wi-Fi", Sub = () => "Choose a network", Activate = () => Shell.Push(WifiScreen()) },
        new ListItem { Glyph = "\uE767", Label = "Audio Output", Sub = () => "Speakers, headphones, TV", Activate = () => Shell.Push(AudioScreen()) },
        new ListItem { Glyph = "\uE702", Label = "Bluetooth Devices", Sub = () => "Pair controllers and headsets in Windows Settings", Activate = () => WinSettings("ms-settings:bluetooth") },
        new ListItem { Glyph = "\uE720", Label = "Audio Input and Advanced Sound", Activate = () => WinSettings("ms-settings:sound") },
        new ListItem { Glyph = "\uE83F", Label = "Power and Battery", Activate = () => WinSettings("ms-settings:powersleep") },
    });

    /// <summary>Shows the cached list at once, then scans in the background and refreshes; <paramref name="scanned"/> stops the loop.</summary>
    internal static Screen WifiScreen(bool scanned = false)
    {
        List<Devices.Network> nets;
        try { nets = Devices.WifiNetworks(); }
        catch (Exception ex) { return new ListScreen("Wi-Fi", new[] { new ListItem { Glyph = "\uE783", Label = "Wi-Fi isn't available", Sub = () => ex.Message } }); }
        void Reopen() { Shell.Pop(); Shell.Push(WifiScreen(true)); }
        void Scan() => Task.Run(() =>
        {
            try { Devices.ScanWifi(); } catch (Exception ex) { Log.Warn("Wi-Fi scan: " + ex.Message); }
            Shell.Window.Dispatcher.Invoke(() => { if (Shell.Top is ListScreen { Tag: "wifi" }) Reopen(); });
        });
        if (!scanned) Scan();
        void Join(Devices.Network n, string? pw)
        {
            Shell.Toast("Wi-Fi", "Connecting to " + n.Ssid, "\uE701");
            Task.Run(() =>
            {
                var r = Devices.Connect(n, pw);
                Thread.Sleep(4000);
                Shell.Window.Dispatcher.Invoke(() => { Shell.Toast("Wi-Fi", r, "\uE701"); if (Shell.Top is ListScreen { Tag: "wifi" }) Reopen(); });
            });
        }
        var items = nets.Select(n => new ListItem
        {
            // Segoe signal-bar glyphs: one, two, three bars, full.
            Glyph = n.Signal < 25 ? "\uE872" : n.Signal < 50 ? "\uE873" : n.Signal < 75 ? "\uE874" : "\uE701", Label = n.Ssid,
            Sub = () => (n.Connected ? "Connected \u00B7 " : n.Saved ? "Saved \u00B7 " : "") + (Devices.IsOpen(n) ? "Open" : "Secured \u00B7 " + n.Auth), Value = () => $"{n.Signal}%",
            Activate = () =>
            {
                if (n.Connected) Shell.Confirm("Wi-Fi", $"Disconnect from {n.Ssid}?", () => { Devices.Disconnect(); Reopen(); }, "Disconnect", "Cancel");
                else if (n.Saved || Devices.IsOpen(n)) Join(n, null);
                else Osk.Ask("Password for " + n.Ssid, "", pw => Join(n, pw), secret: true);
            },
        }).ToList();
        items.Add(new ListItem { Glyph = "\uE72C", Label = "Scan Again", Activate = () => { Shell.Toast("Wi-Fi", "Scanning for networks", ""); Scan(); } });
        return new ListScreen("Wi-Fi", items, subtitle: scanned ? $"{nets.Count} networks" : "Scanning…") { Tag = "wifi" };
    }

    internal static Screen AudioScreen()
    {
        List<Devices.AudioDevice> outs;
        try { outs = Devices.AudioOutputs(); }
        catch (Exception ex) { return new ListScreen("Audio Output", new[] { new ListItem { Glyph = "\uE783", Label = "Audio devices couldn't be read", Sub = () => ex.Message } }); }
        return new ListScreen("Audio Output", outs.Select(d => new ListItem
        {
            Glyph = "\uE767", Label = d.Name, Check = () => d.IsDefault,
            Activate = () =>
            {
                try { Devices.SetDefaultOutput(d.Id); Shell.Pop(); Shell.Push(AudioScreen()); }
                catch (Exception ex) { Shell.Message("Audio Output", ex.Message); }
            },
        }), subtitle: "Choose where sound plays");
    }

    static Screen DisplayScreen()
    {
        return new ListScreen("Display Settings", new[]
        {
            new ListItem { Glyph = "\uE7F4", Label = "Resolution", Value = () => Display.Current().ToString(), Activate = () =>
            {
                var cur = Display.Current();
                var list = Display.Resolutions();
                Shell.Push(new ListScreen("Resolution", list.Select(m => new ListItem
                {
                    Label = m.ToString(), Check = () => m.Width == cur.Width && m.Height == cur.Height,
                    // Keep the refresh rate if the new resolution supports it, otherwise its fastest.
                    Activate = () => { Shell.Pop(); var hz = Display.RefreshRates(m.Width, m.Height); TryDisplay(m with { Hz = hz.Contains(cur.Hz) ? cur.Hz : hz.FirstOrDefault() }); },
                }), dialog: true, select: Math.Max(0, list.FindIndex(m => m.Width == cur.Width && m.Height == cur.Height))));
            } },
            new ListItem { Glyph = "\uE916", Label = "Refresh Rate", Value = () => $"{Display.Current().Hz} Hz", Activate = () =>
            {
                var cur = Display.Current();
                var rates = Display.RefreshRates(cur.Width, cur.Height);
                Shell.Push(new ListScreen("Refresh Rate", rates.Select(hz => new ListItem
                {
                    Label = $"{hz} Hz", Check = () => hz == cur.Hz,
                    Activate = () => { Shell.Pop(); TryDisplay(cur with { Hz = hz }); },
                }), dialog: true, select: Math.Max(0, rates.IndexOf(cur.Hz))));
            } },
        });
    }

    /// <summary>Applies a display mode for 15 seconds; it stays only if the user confirms, like the console and Windows do.</summary>
    static void TryDisplay(Display.Mode m)
    {
        if (m == Display.Current()) return;
        if (Display.TryApply(m) is string err) { Shell.Message("Display Settings", err); return; }
        int left = 15;
        bool done = false;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        ListScreen? dlg = null;
        void Finish(bool keep)
        {
            if (done) return;
            done = true;
            timer.Stop();
            if (keep) Display.Keep(m); else Display.Revert();
            Shell.Pop();
            Sound.Play(keep ? Sfx.Decide : Sfx.Back);
        }
        dlg = new ListScreen("Keep These Display Settings?", new[]
        {
            new ListItem { Glyph = "\uE73E", Label = "Keep", Sub = () => $"Going back to the previous settings in {left} s", Activate = () => Finish(true) },
            new ListItem { Glyph = "\uE7A7", Label = "Revert", Activate = () => Finish(false) },
        }, dialog: true, subtitle: $"{m} at {m.Hz} Hz") { OnBack = () => Finish(false) };
        timer.Tick += (_, _) => { if (--left <= 0) Finish(false); else dlg.Refresh(); };
        Shell.Push(dlg);
        Sound.Play(Sfx.DialogShow);
        timer.Start();
    }

    static void WinSettings(string uri)
    {
        try { Native.OpenShell(uri); }
        catch (Exception ex) { Shell.Message("Settings", "Windows Settings couldn't be opened: " + ex.Message); }
    }

    static Screen AccountsScreen() => new ListScreen("Accounts and Services", new[]
    {
        new ListItem { Glyph = "\uE7FC", Label = "Steam", Value = () => Steam.FindInstall() is string p ? (Steam.CurrentUser(p).persona ?? "Installed") : "Not installed" },
        new ListItem { Glyph = "\uE768", Label = "Start Steam in the Background", Sub = () => "So games launch quickly and the overlay works", Check = () => S.StartSteamSilently, Activate = () => { S.StartSteamSilently = !S.StartSteamSilently; Save(); } },
        KeyItem("SteamGridDB API Key", "Square artwork. Free key: steamgriddb.com > Preferences > API", Secrets.SteamGridDb, async v => await MetadataService.ValidateSteamGridDb(v)),
        KeyItem("IGDB Client ID", "Console game details. dev.twitch.tv > Applications", Secrets.IgdbClientId, null),
        KeyItem("IGDB Client Secret", "Pairs with the Client ID above", Secrets.IgdbClientSecret, async v =>
            Secrets.Get(Secrets.IgdbClientId) is string id && await MetadataService.ValidateIgdb(id, v)),
        KeyItem("Steam Web API Key", "Friends list and achievements. steamcommunity.com/dev/apikey", Secrets.SteamWebApi, null),
        new ListItem { Glyph = "", Label = "RetroAchievements User", Sub = () => "Your RetroAchievements user name, for Trophies", Value = () => Secrets.Get(Secrets.RetroAchievementsUser) ?? "Not set",
            Activate = () => Osk.Ask("RetroAchievements User", Secrets.Get(Secrets.RetroAchievementsUser) ?? "", v => Secrets.Set(Secrets.RetroAchievementsUser, v)) },
        KeyItem("RetroAchievements Web API Key", "Trophies from your emulated games. retroachievements.org > Settings > Keys", Secrets.RetroAchievementsKey, null),
    });

    static ListItem KeyItem(string label, string help, string name, Func<string, Task<bool>>? validate) => new()
    {
        Glyph = "\uE192", Label = label, Sub = () => help, Value = () => Secrets.Has(name) ? "Saved" : "Not set",
        Activate = () => Osk.Ask(label, "", async v =>
        {
            if (string.IsNullOrWhiteSpace(v)) { Shell.Confirm(label, "Remove the saved key?", () => Secrets.Set(name, null)); return; }
            if (validate != null && !await validate(v.Trim())) { Sound.Play(Sfx.Error); Shell.Message(label, "That key didn't work. Check it and try again. Nothing was saved."); return; }
            Secrets.Set(name, v.Trim());
            Shell.Toast(label, "Saved securely on this PC.", "\uE192");
            foreach (var g in Shell.Library.Games) g.MetaTried = false;
            _ = Shell.Metadata.RunAsync();
        }, secret: true),
    };

    static Screen LosslessScreen() => new ListScreen("Lossless Scaling", new[]
    {
        new ListItem { Glyph = "\uE7B3", Label = "Turn On Automatically for Games", Sub = () => "Each game can override this in its Game Settings", Check = () => S.LosslessEnabled, Activate = () => { S.LosslessEnabled = !S.LosslessEnabled; Save(); } },
        new ListItem { Glyph = "\uE916", Label = "Delay After the Game Appears", Value = () => $"{S.LosslessDelaySec} s", Adjust = d => { S.LosslessDelaySec = Math.Clamp(S.LosslessDelaySec + d * 5, 0, 120); Save(); } },
        new ListItem { Glyph = "\uE8E5", Label = "Lossless Scaling Program", Sub = () => LosslessScaling.FindExe() ?? "Not found. It is sold on Steam.",
            Activate = () => FilePicker.Pick("Lossless Scaling", new[] { ".exe" }, f => { S.LosslessPath = f; Save(); }) },
        new ListItem { Glyph = "\uE765", Label = "Scale Hotkey", Sub = () => "Read from Lossless Scaling's own settings", Value = () => string.Join("+", LosslessScaling.Hotkey().Select(k => k switch { 0x11 => "Ctrl", 0x12 => "Alt", 0x10 => "Shift", 0x5B => "Win", _ => ((char)k).ToString() })) },
    });

    static Screen UpdateScreen()
    {
        UpdateInfo? found = null;
        string status = "Not checked yet";
        ListScreen? scr = null;
        scr = new ListScreen("System Software Update", new[]
        {
            new ListItem { Glyph = "\uE946", Label = "Installed Version", Value = () => Updater.CurrentText },
            new ListItem { Glyph = "\uE895", Label = found == null ? "Check for Update" : $"Download and Install {found.Tag}", Sub = () => status, Activate = async () =>
                {
                    if (found == null)
                    {
                        status = "Checking…"; scr!.Refresh();
                        found = await Updater.CheckAsync();
                        status = found == null ? "You have the latest version." : $"Version {found.Tag} is available. Press ✕ again to install.";
                        scr.Refresh();
                        return;
                    }
                    await Shell.Window.InstallUpdate(found, p => { status = $"Downloading… {p * 100:0}%"; scr!.Refresh(); });
                } },
            new ListItem { Glyph = "\uE895", Label = "Check Automatically", Check = () => S.AutoCheckUpdates, Activate = () => { S.AutoCheckUpdates = !S.AutoCheckUpdates; Save(); } },
        });
        return scr;
    }

    static Screen SystemScreen() => new ListScreen("System", new[]
    {
        new ListItem { Glyph = "\uE7C3", Label = "Run Initial Setup Again", Activate = () => Shell.Push(new SetupWizard(rerun: true)) },
        new ListItem { Glyph = "\uE9F9", Label = "Logs Folder", Sub = () => AppPaths.Logs, Activate = () => Shell.Window.StartExternal("explorer.exe", $"\"{AppPaths.Logs}\"") },
        new ListItem { Glyph = "\uE946", Label = "About OrbisUI", Value = () => "Version " + Updater.CurrentText, Sub = () => "github.com/" + Updater.Repo },
        new ListItem { Glyph = "\uE7E8", Label = "Exit to Windows Desktop", Activate = () => Shell.Window.ExitToDesktop() },
    });
}

public static class SoundPack
{
    /// <summary>Copies snd_*.wav and the startup jingle from a folder into the portable Data\Sounds folder.</summary>
    public static int Import(string dir)
    {
        int n = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "snd_*.wav"))
            {
                File.Copy(f, Path.Combine(AppPaths.Sounds, Path.GetFileName(f)), true);
                n++;
            }
            foreach (var f in Directory.EnumerateFiles(dir, "*Startup*.mp3"))
                File.Copy(f, Path.Combine(AppPaths.Sounds, "startup.mp3"), true);
        }
        catch (Exception ex) { Log.Warn("sound import: " + ex.Message); }
        Sound.Init(AppSettings.Current.UiVolume, AppSettings.Current.UiSounds);
        return n;
    }

    public static string? AutoFind()
    {
        var candidates = new[]
        {
            Path.Combine(AppPaths.Root, "UI Sounds"),
            Path.Combine(Directory.GetParent(AppPaths.Root)?.FullName ?? AppPaths.Root, "UI Sounds"),
            @"C:\Sony\OrbisUI\UI Sounds",
        };
        return candidates.FirstOrDefault(d => Directory.Exists(d) && Directory.EnumerateFiles(d, "snd_*.wav").Any());
    }
}
