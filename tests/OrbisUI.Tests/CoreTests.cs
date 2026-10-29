using System.Text;
using OrbisUI.Audio;
using OrbisUI.Core;
using OrbisUI.Library;
using OrbisUI.Metadata;
using OrbisUI.Services;
using Xunit;

namespace OrbisUI.Tests;

public class CoreTests
{
    static CoreTests()
    {
        var root = Path.Combine(Path.GetTempPath(), "orbisui-tests-" + Environment.ProcessId);
        Directory.CreateDirectory(root);
        AppPaths.OverrideRoot(root);
    }

    [Fact]
    public void TextVdf_ParsesNestedLibraryFolders()
    {
        var n = Vdf.ParseText("\"libraryfolders\"\n{\n \"0\"\n {\n  \"path\" \"C:\\\\Steam\"\n  \"apps\" { \"10\" \"5\" }\n }\n}");
        var lib = n.Child("libraryfolders")!.Child("0")!;
        Assert.Equal(@"C:\Steam", lib.Str("path"));
        Assert.Equal("5", lib.Child("apps")!.Str("10"));
    }

    [Fact]
    public void BinaryVdf_RoundTrips()
    {
        var root = new Vdf.Node();
        var sc = new Vdf.Node();
        var entry = new Vdf.Node { ["appid"] = 3000000001u, ["AppName"] = "Game", ["Exe"] = "\"C:\\g.exe\"" };
        var tags = new Vdf.Node { ["0"] = "OrbisUI" };
        entry["tags"] = tags;
        sc["0"] = entry;
        root["shortcuts"] = sc;
        var back = Vdf.ParseBinary(Vdf.WriteBinary(root));
        var e = back.Child("shortcuts")!.Child("0")!;
        Assert.Equal(3000000001u, e["appid"]);
        Assert.Equal("Game", e.Str("AppName"));
        Assert.Equal("OrbisUI", e.Child("tags")!.Str("0"));
    }

    [Fact]
    public void Sfo_ReadsTitleAndId()
    {
        // Minimal PARAM.SFO: 2 entries, utf8 TITLE and TITLE_ID.
        var keys = Encoding.ASCII.GetBytes("TITLE\0TITLE_ID\0\0");
        var title = Encoding.UTF8.GetBytes("My Game\0");
        var id = Encoding.UTF8.GetBytes("CUSA00001\0\0\0");
        int keyTable = 20 + 2 * 16, dataTable = keyTable + keys.Length;
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(new byte[] { 0, (byte)'P', (byte)'S', (byte)'F' }); w.Write(0x0101); w.Write(keyTable); w.Write(dataTable); w.Write(2);
        w.Write((ushort)0); w.Write((ushort)0x0204); w.Write(title.Length); w.Write(title.Length); w.Write(0);
        w.Write((ushort)6); w.Write((ushort)0x0204); w.Write(10); w.Write(id.Length); w.Write(title.Length);
        w.Write(keys); w.Write(title); w.Write(id);
        var p = Sfo.Parse(ms.ToArray());
        Assert.Equal("My Game", p["TITLE"]);
        Assert.Equal("CUSA00001", p["TITLE_ID"]);
    }

    [Theory]
    [InlineData("Shadow Hearts - Covenant (USA) (Disc 1).iso", "Shadow Hearts - Covenant (Disc 1)")]
    [InlineData("Persona 3 FES (Europe).iso", "Persona 3 FES")]
    [InlineData("Legend of Dragoon, The (USA) [!].cue", "The Legend of Dragoon")]
    public void CleanTitle_StripsDumpTags(string file, string expected) => Assert.Equal(expected, RomScanner.CleanTitle(file));

    [Fact]
    public void MultiDisc_GamesAreGroupedIntoOne()
    {
        var games = new List<Game>
        {
            new() { Id = "a", Title = "Shadow Hearts - Covenant (Disc 2)", Path = "d2.iso" },
            new() { Id = "b", Title = "Shadow Hearts - Covenant (Disc 1)", Path = "d1.iso" },
            new() { Id = "c", Title = "Shadow Hearts", Path = "sh.iso" },
        };
        var grouped = RomScanner.GroupDiscs("PS2", games);
        Assert.Equal(2, grouped.Count);
        var multi = grouped.Single(g => g.Discs != null);
        Assert.Equal("Shadow Hearts - Covenant", multi.Title);
        Assert.Equal(new[] { "d1.iso", "d2.iso" }, multi.Discs);
        Assert.Equal("d1.iso", multi.Path);
    }

    [Fact]
    public void MultiTrackBin_GetsGeneratedCueOutsideRomFolder()
    {
        var dir = Path.Combine(AppPaths.Root, "roms");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "Game (USA) (Track 1).bin"), new byte[16]);
        File.WriteAllBytes(Path.Combine(dir, "Game (USA) (Track 2).bin"), new byte[16]);
        var cue = RomScanner.MakeCue(Path.Combine(dir, "Game (USA) (Track 1).bin"))!;
        Assert.StartsWith(AppPaths.Cache, cue);
        var text = File.ReadAllText(cue);
        Assert.Contains("TRACK 01 MODE2/2352", text);
        Assert.Contains("TRACK 02 AUDIO", text);
    }

    [Theory]
    [InlineData("NARUTO: Ultimate Ninja STORM", "Naruto Ultimate Ninja Storm", true)]
    [InlineData("Portal 2", "Portal", false)]
    [InlineData("Shadow Hearts - Covenant (Disc 1)", "Shadow Hearts: Covenant", true)]
    public void TitlesMatch_IgnoresPunctuationAndCase(string a, string b, bool expected) => Assert.Equal(expected, MetadataService.TitlesMatch(a, b));

    [Fact]
    public void LibretroName_ReplacesForbiddenCharacters()
        => Assert.Equal("Ratchet _ Clank (USA)", MetadataService.LibretroName("Ratchet & Clank (USA) (Track 1)"));

    [Theory]
    [InlineData("v0.2.0", 0, 2, 0)]
    [InlineData("1.4.3-beta", 1, 4, 3)]
    public void Updater_ParsesTags(string tag, int a, int b, int c) => Assert.Equal(new Version(a, b, c), Updater.ParseTag(tag));

    [Fact]
    public void Sound_SynthIsValidPcm16AndScales()
    {
        var wav = Sound.Synth(Sfx.Cursor);
        Assert.True(Sound.TryFindPcm16(wav, out var off, out var len));
        Assert.True(len > 0);
        var silent = Sound.Scale(wav, 0);
        for (int i = off; i < off + len; i++) Assert.Equal(0, silent[i]);
    }

    [Fact]
    public void SteamShortcuts_MergeAddsOnceAndRoundTrips()
    {
        var g = new Game { Id = "PS2:abc", Title = "Test Game", Platform = "PS2" };
        var root = new Vdf.Node();
        var e = new SteamShortcuts.Entry(g, "Test Game", @"C:\emu\pcsx2-qt.exe", "-batch -- \"x.iso\"", @"C:\emu");
        var (added, _) = SteamShortcuts.Merge(root, new[] { e });
        Assert.Single(added);
        var back = Vdf.ParseBinary(Vdf.WriteBinary(root));
        Assert.Equal(1, back.Child("shortcuts")!.Count);
        var (again, repaired) = SteamShortcuts.Merge(back, new[] { e });
        Assert.Empty(again);
        Assert.Equal(0, repaired);
        Assert.True((SteamShortcuts.AppId(g) & 0x80000000u) != 0);
    }

    [Fact]
    public void ControllerFixer_ApplyThenUndoRestoresGame()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "orbis-fixer-" + Guid.NewGuid().ToString("N"));
        AppPaths.OverrideRoot(Path.Combine(tmp, "root"));
        var game = Directory.CreateDirectory(Path.Combine(tmp, "game")).FullName;
        var mod = Directory.CreateDirectory(Path.Combine(tmp, "mod", "data")).FullName;
        File.WriteAllText(Path.Combine(game, "keep.txt"), "original");
        Directory.CreateDirectory(Path.Combine(game, "data"));
        File.WriteAllText(Path.Combine(game, "data", "prompts.pak"), "xbox");
        File.WriteAllText(Path.Combine(mod, "prompts.pak"), "ps4");
        File.WriteAllText(Path.Combine(mod, "new.pak"), "extra");
        var inst = Services.ControllerFixer.Apply("g1", "mod.zip", Path.Combine(tmp, "mod"), game);
        Assert.Equal("ps4", File.ReadAllText(Path.Combine(game, "data", "prompts.pak")));
        var (saved, dir) = Services.ControllerFixer.History("g1").Single();
        Services.ControllerFixer.Undo(saved, dir);
        Assert.Equal("xbox", File.ReadAllText(Path.Combine(game, "data", "prompts.pak")));
        Assert.False(File.Exists(Path.Combine(game, "data", "new.pak")));
        Assert.Equal("original", File.ReadAllText(Path.Combine(game, "keep.txt")));
        Directory.Delete(tmp, true);
    }

    [Fact]
    public void Uninstaller_RecyclesRomWithTracksOnly()
    {
        var dir = Path.Combine(Path.GetTempPath(), "orbis-uninst-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var f in new[] { "Game.cue", "Game (Track 1).bin", "Game (Track 2).bin", "Other.cue" }) File.WriteAllText(Path.Combine(dir, f), "");
        var g = new Game { Id = "emu:PS1:x", Platform = "PS1", Kind = LaunchKind.Emulator, Path = Path.Combine(dir, "Game.cue") };
        var paths = Services.Uninstaller.RecyclePaths(g).Select(Path.GetFileName).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "Game (Track 1).bin", "Game (Track 2).bin", "Game.cue" }, paths);
        Assert.True(Services.Uninstaller.Protected(@"C:\"));
        Assert.True(Services.Uninstaller.Protected(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)));
        Assert.False(Services.Uninstaller.Protected(dir));
        Assert.False(LibraryService.StillInstalled(new Game { Path = Path.Combine(dir, "gone.exe") }));
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Lossless_CoversNeedsMostOfTheGame()
    {
        var game = new Native.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
        Assert.True(Services.LosslessScaling.Covers(new Native.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 }, game));
        Assert.False(Services.LosslessScaling.Covers(new Native.RECT { Left = 0, Top = 0, Right = 800, Bottom = 600 }, game));
    }

    [Fact]
    public void Display_CurrentModeIsListed()
    {
        var cur = Services.Display.Current();
        Assert.True(cur.Width > 0 && cur.Height > 0);
        Assert.Contains(Services.Display.Resolutions(), m => m.Width == cur.Width && m.Height == cur.Height);
        Assert.Contains(cur.Hz, Services.Display.RefreshRates(cur.Width, cur.Height));
    }

    [Fact]
    public void Sound_RepairsBadBlockAlign()
    {
        var wav = Sound.Synth(Sfx.Cursor);
        wav[32] = 16; // what the PS4 pack ships
        var fixedWav = Sound.Scale(wav, 1);
        Assert.Equal(2, BitConverter.ToUInt16(fixedWav, 32));
        Assert.Equal(44100 * 2, BitConverter.ToInt32(fixedWav, 28));
    }

    [Fact]
    public void ImageTools_MakesSquareTilesFromPortraitArt()
    {
        var px = new byte[60 * 90 * 4];
        for (int i = 3; i < px.Length; i += 4) px[i] = 255;
        var src = System.Windows.Media.Imaging.BitmapSource.Create(60, 90, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, px, 60 * 4);
        var png = ImageTools.Encode(src, jpeg: false);
        var sq = ImageTools.Square(ImageTools.Load(png));
        Assert.Equal(ImageTools.TileSize, sq.PixelWidth);
        Assert.Equal(ImageTools.TileSize, sq.PixelHeight);
    }

    [Fact]
    public void Settings_SaveAndLoadRoundTrip()
    {
        var s = AppSettings.Load();
        s.RomRoot = @"D:\roms";
        s.For("steam:1").Hidden = true;
        s.Save();
        var back = AppSettings.Load();
        Assert.Equal(@"D:\roms", back.RomRoot);
        Assert.True(back.Games["STEAM:1"].Hidden);
    }

    [Fact]
    public void Secrets_AreEncryptedAtRest()
    {
        Secrets.Set("test", "super-secret-value");
        Assert.Equal("super-secret-value", Secrets.Get("test"));
        var raw = File.ReadAllBytes(Path.Combine(AppPaths.Config, "secrets.dat"));
        Assert.DoesNotContain("super-secret-value", Encoding.UTF8.GetString(raw));
    }

    [Fact]
    public void SetIniKeys_ReplacesAddsAndBacksUp()
    {
        var ini = Path.Combine(AppPaths.Root, "emu-test", "PCSX2.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(ini)!);
        File.WriteAllText(ini, "[EmuCore]\r\nEnableFastBoot = false\r\n[EmuCore/GS]\r\nRendererX = 1\r\nRenderer = 12\r\n");
        Assert.True(EmulatorProfiles.SetIniKeys(ini, "EmuCore/GS", ("Renderer", "14"), ("OsdMessagesPos", "0")));
        Assert.True(EmulatorProfiles.SetIniKeys(ini, "EmuCore", ("EnableFastBoot", "true")));
        var text = File.ReadAllText(ini);
        Assert.Contains("RendererX = 1\r\nRenderer = 14\r\n", text);
        Assert.Contains("[EmuCore/GS]\r\nOsdMessagesPos = 0", text);
        Assert.Contains("EnableFastBoot = true\r\n", text);
        Assert.False(EmulatorProfiles.SetIniKeys(ini, "EmuCore/GS", ("Renderer", "14")));
        Assert.NotEmpty(Directory.GetFiles(AppPaths.Backups, "emu-test-PCSX2.ini-*"));
    }

    [Fact]
    public void FromTarget_ExeShortcutUrlAndStoreApp()
    {
        var exe = LibraryService.FromTarget(@"C:\Games\X\x.exe");
        Assert.Equal(LaunchKind.Exe, exe.Kind);
        Assert.Equal(@"C:\Games\X", exe.WorkDir);
        var url = LibraryService.FromTarget(@"C:\Users\me\Desktop\Game.url");
        Assert.Equal(LaunchKind.Url, url.Kind);
        Assert.Equal("Game", url.Title);
        var store = LibraryService.FromTarget(@"shell:AppsFolder\Microsoft.Foo_8wekyb3d8bbwe!App", "Foo");
        Assert.Equal(LaunchKind.Url, store.Kind);
        Assert.StartsWith("exe:", store.Id);
        Assert.Equal("Microsoft.Foo", OrbisUI.UI.GameMenu.VersionName(store.Path!));
    }

    [Theory]
    [InlineData("example.com", "https://example.com")]
    [InlineData("http://a.org/x", "http://a.org/x")]
    [InlineData("ps4 games", "https://www.google.com/search?q=ps4%20games")]
    public void BrowserAddressOrGoogleSearch(string typed, string url) => Assert.Equal(url, OrbisUI.UI.BrowserScreen.ToUrl(typed));

    [Fact]
    public void GameProgress_GradesAndTitleMatching()
    {
        Assert.Equal(1, GameProgress.GradeFromRarity(4.2));
        Assert.Equal(2, GameProgress.GradeFromRarity(20));
        Assert.Equal(3, GameProgress.GradeFromRarity(80));
        Assert.Equal(1, GameProgress.GradeFromPoints(25));
        Assert.Equal(3, GameProgress.GradeFromPoints(5));
        Assert.Equal(GameProgress.Normalize("Shadow Hearts"), GameProgress.Normalize("shadow-hearts!"));
    }

    [Fact]
    public async Task SteamLibraryArt_FallsBackToOwnArt_WithoutSteamGridDb()
    {
        var dir = Path.Combine(AppPaths.Root, "grid-" + Guid.NewGuid().ToString("N"));
        var art = Path.Combine(AppPaths.Root, "cover.png");
        var px = new int[64 * 64];
        File.WriteAllBytes(art, ImageTools.Encode(System.Windows.Media.Imaging.BitmapSource.Create(64, 64, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, px, 256), jpeg: false));
        var g = new Game { Id = "t:1", Title = "Test", Art = art };
        await MetadataService.SteamLibraryArtAsync(g, "Test", dir, 123u, CancellationToken.None);
        foreach (var (file, w, h) in new[] { ("123p.jpg", 600, 900), ("123.jpg", 920, 430), ("123_hero.jpg", 1920, 620) })
        {
            var b = ImageTools.Load(File.ReadAllBytes(Path.Combine(dir, file)));
            Assert.Equal((w, h), (b.PixelWidth, b.PixelHeight));
        }
        Assert.True(new FileInfo(Path.Combine(dir, "123_logo.png")).Length > 100);
    }
}
