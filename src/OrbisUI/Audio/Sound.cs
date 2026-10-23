using System.Runtime.InteropServices;
using System.Windows.Media;
using OrbisUI.Core;

namespace OrbisUI.Audio;

public enum Sfx { Cursor, Enter, Back, Decide, Cancel, Option, Boot, GameStart, GameQuit, Notify, Trophy, Login, Error, DialogShow, Yes, No, KeyPress, KeyBack, KeyMove, Spin, Capture }

/// <summary>
/// UI sound effects. WAVs are preloaded into unmanaged memory with the volume baked into the samples and
/// played with winmm PlaySound, which costs nothing while idle. Missing files fall back to short generated tones.
/// </summary>
public static class Sound
{
    // Substring of the source filename for each effect (matches the PS4 sound pack naming, snd_*.wav).
    static readonly Dictionary<Sfx, string> FileKeys = new()
    {
        [Sfx.Cursor] = "snd_cursor1-", [Sfx.Enter] = "snd_enter_menu", [Sfx.Back] = "snd_back_menu",
        [Sfx.Decide] = "snd_cz_cursor_in2", [Sfx.Cancel] = "snd_cz_cursor_in1", [Sfx.Option] = "snd_std_option1",
        [Sfx.Boot] = "snd_boot_miniapp_boot", [Sfx.GameStart] = "snd_boot_game_boot", [Sfx.GameQuit] = "snd_boot_game_quit",
        [Sfx.Notify] = "snd_msg_notification", [Sfx.Trophy] = "snd_msg_trophy", [Sfx.Login] = "snd_log_login",
        [Sfx.Error] = "snd_msg_ng_psbutton", [Sfx.DialogShow] = "snd_dlg_show_positive", [Sfx.Yes] = "snd_dlg_yes",
        [Sfx.No] = "snd_dlg_no", [Sfx.KeyPress] = "snd_osk_key_press", [Sfx.KeyBack] = "snd_osk_backspace",
        [Sfx.KeyMove] = "snd_osk_cursor_move", [Sfx.Spin] = "snd_spinbox", [Sfx.Capture] = "snd_ss_captured",
    };

    sealed class Clip { public byte[] Raw = Array.Empty<byte>(); public IntPtr Mem; public int Len; }

    static readonly Dictionary<Sfx, Clip> Clips = new();
    static readonly Dictionary<Sfx, long> LastPlayed = new();
    static double _volume = 0.7;
    static bool _enabled = true;
    static MediaPlayer? _music;

    public static bool Muted { get; set; }
    public static bool HasPack { get; private set; }

    public static void Init(double volume, bool enabled)
    {
        _enabled = enabled;
        _volume = volume;
        var files = Directory.Exists(AppPaths.Sounds) ? Directory.GetFiles(AppPaths.Sounds, "*.wav") : Array.Empty<string>();
        foreach (var (sfx, key) in FileKeys)
        {
            var f = files.FirstOrDefault(x => Path.GetFileName(x).StartsWith(key, StringComparison.OrdinalIgnoreCase));
            byte[]? raw = null;
            try { if (f != null) raw = File.ReadAllBytes(f); } catch (Exception ex) { Log.Warn($"sound {f}: {ex.Message}"); }
            raw ??= Synth(sfx);
            Clips[sfx] = new Clip { Raw = raw };
        }
        HasPack = files.Length > 0;
        ApplyVolume();
    }

    public static void SetVolume(double v, bool enabled)
    {
        _volume = Math.Clamp(v, 0, 1);
        _enabled = enabled;
        ApplyVolume();
    }

    static void ApplyVolume()
    {
        Native.PlaySound(IntPtr.Zero, IntPtr.Zero, Native.SND_PURGE);
        foreach (var c in Clips.Values)
        {
            var scaled = Scale(c.Raw, _volume);
            if (c.Mem != IntPtr.Zero) Marshal.FreeHGlobal(c.Mem);
            c.Mem = Marshal.AllocHGlobal(scaled.Length);
            Marshal.Copy(scaled, 0, c.Mem, scaled.Length);
            c.Len = scaled.Length;
        }
    }

    /// <summary>inBackground plays even while a game is in front (in-game notifications).</summary>
    public static void Play(Sfx s, bool inBackground = false)
    {
        if (!_enabled || (Muted && !inBackground) || _volume <= 0.001) return;
        if (!Clips.TryGetValue(s, out var c) || c.Mem == IntPtr.Zero) return;
        long now = Environment.TickCount64;
        // Throttle: the same effect at most every 45 ms so held directions don't stack up.
        if (LastPlayed.TryGetValue(s, out var t) && now - t < 45) return;
        LastPlayed[s] = now;
        Native.PlaySound(c.Mem, IntPtr.Zero, Native.SND_MEMORY | Native.SND_ASYNC | Native.SND_NODEFAULT);
    }

    /// <summary>Longer clips such as the boot jingle (mp3) go through MediaPlayer.</summary>
    public static void PlayFile(string pattern)
    {
        if (!_enabled || Muted) return;
        var f = Directory.Exists(AppPaths.Sounds) ? Directory.GetFiles(AppPaths.Sounds, pattern).FirstOrDefault() : null;
        if (f == null) return;
        _music ??= new MediaPlayer();
        _music.Open(new Uri(f));
        _music.Volume = _volume;
        _music.Play();
    }

    public static void StopFile() { _music?.Stop(); _music?.Close(); }

    /// <summary>UI volume after the on/off switch and focus mute, for media that plays its own audio (the boot video).</summary>
    public static double EffectiveVolume => _enabled && !Muted ? _volume : 0;

    static MediaPlayer? _bgm;
    static bool _bgmOn;

    /// <summary>Home screen music (Data\Media\music.mp3), looped. Pausing keeps the position so it resumes where it left off.</summary>
    public static void SetMusic(bool play, double volume)
    {
        if (_bgm != null) _bgm.Volume = volume;
        if (play == _bgmOn) return;
        _bgmOn = play;
        if (!play) { _bgm?.Pause(); return; }
        if (_bgm == null)
        {
            if (AppPaths.MediaFile("music.mp3") is not string f) return;
            _bgm = new MediaPlayer { Volume = volume };
            _bgm.MediaEnded += (_, _) => { _bgm.Position = TimeSpan.Zero; _bgm.Play(); };
            _bgm.MediaFailed += (_, e) => Log.Warn("home music: " + e.ErrorException?.Message);
            _bgm.Open(new Uri(f));
        }
        _bgm.Play();
    }

    /// <summary>Scale 16-bit PCM samples in a RIFF/WAVE buffer (and repair its header). Other formats are returned unchanged.</summary>
    internal static byte[] Scale(byte[] wav, double vol)
    {
        var copy = (byte[])wav.Clone();
        if (!TryFindPcm16(copy, out int dataOff, out int dataLen)) return copy;
        for (int i = dataOff; i + 1 < dataOff + dataLen; i += 2)
        {
            int s = (short)(copy[i] | copy[i + 1] << 8);
            s = (int)Math.Clamp(s * vol, short.MinValue, short.MaxValue);
            copy[i] = (byte)s; copy[i + 1] = (byte)(s >> 8);
        }
        return copy;
    }

    internal static bool TryFindPcm16(byte[] b, out int dataOff, out int dataLen)
    {
        dataOff = dataLen = 0;
        if (b.Length < 44 || b[0] != 'R' || b[1] != 'I' || b[8] != 'W') return false;
        bool pcm16 = false;
        int p = 12;
        while (p + 8 <= b.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(b, p, 4);
            int len = BitConverter.ToInt32(b, p + 4);
            if (len < 0) return false;
            if (id == "fmt ")
            {
                pcm16 = BitConverter.ToUInt16(b, p + 8) == 1 && BitConverter.ToUInt16(b, p + 22) == 16;
                // The PS4 pack's WAVs declare block align 16 instead of 2, which winmm refuses to play; repair it in place.
                int ch = BitConverter.ToUInt16(b, p + 10), rate = BitConverter.ToInt32(b, p + 12);
                if (pcm16 && ch > 0) { BitConverter.TryWriteBytes(b.AsSpan(p + 20, 2), (ushort)(ch * 2)); BitConverter.TryWriteBytes(b.AsSpan(p + 16, 4), rate * ch * 2); }
            }
            else if (id == "data") { dataOff = p + 8; dataLen = Math.Min(len, b.Length - dataOff); return pcm16; }
            p += 8 + len + (len & 1);
        }
        return false;
    }

    /// <summary>Original fallback tones so the shell still sounds like a console without the sound pack.</summary>
    internal static byte[] Synth(Sfx s)
    {
        (double f1, double f2, int ms) = s switch
        {
            Sfx.Cursor or Sfx.KeyMove => (1900, 1900, 28),
            Sfx.Back or Sfx.Cancel or Sfx.No or Sfx.KeyBack => (880, 660, 90),
            Sfx.Error => (300, 240, 160),
            Sfx.Notify or Sfx.Trophy => (1320, 1760, 220),
            Sfx.Boot or Sfx.GameStart or Sfx.Login => (660, 1320, 420),
            Sfx.GameQuit => (1320, 660, 300),
            _ => (1175, 1568, 90),
        };
        const int rate = 44100;
        int n = rate * ms / 1000;
        var ms2 = new MemoryStream();
        var w = new BinaryWriter(ms2);
        w.Write("RIFF"u8); w.Write(36 + n * 2); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(n * 2);
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;
            double f = f1 + (f2 - f1) * t;
            phase += 2 * Math.PI * f / rate;
            double env = Math.Min(1, i / (rate * 0.004)) * Math.Pow(1 - t, 2);
            w.Write((short)(Math.Sin(phase) * env * 9000));
        }
        return ms2.ToArray();
    }
}
