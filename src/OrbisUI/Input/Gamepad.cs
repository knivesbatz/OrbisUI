using System.Runtime.InteropServices;
using OrbisUI.Core;

namespace OrbisUI.Input;

public enum Btn
{
    Up, Down, Left, Right,
    Cross, Circle, Square, Triangle,
    L1, R1, L2, R2, L3, R3,
    Options, Share, PS, Touchpad,
}

/// <summary>
/// Native DualShock 4 / DualSense / XInput support through SDL3's HIDAPI drivers (USB and Bluetooth), no DS4Windows needed.
/// Runs on its own thread, polling at 120 Hz only while the shell is in the foreground and 4 Hz otherwise.
/// </summary>
public sealed unsafe class Gamepad : IDisposable
{
    const string Lib = "SDL3";
    const uint SDL_INIT_JOYSTICK = 0x200, SDL_INIT_GAMEPAD = 0x2000, SDL_INIT_EVENTS = 0x4000;
    const uint EV_AXIS = 0x650, EV_DOWN = 0x651, EV_UP = 0x652, EV_ADDED = 0x653, EV_REMOVED = 0x654;

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern bool SDL_Init(uint flags);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern void SDL_Quit();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern bool SDL_SetHint([MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern bool SDL_PollEvent(byte* ev);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr SDL_OpenGamepad(uint id);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern void SDL_CloseGamepad(IntPtr pad);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr SDL_GetGamepadName(IntPtr pad);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr SDL_GetError();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern ushort SDL_GetGamepadVendor(IntPtr pad);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern ushort SDL_GetGamepadProduct(IntPtr pad);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern int SDL_GetGamepadPowerInfo(IntPtr pad, out int percent);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern bool SDL_SetGamepadLED(IntPtr pad, byte r, byte g, byte b);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern bool SDL_RumbleGamepad(IntPtr pad, ushort lo, ushort hi, uint ms);

    public event Action<Btn, bool>? Button;      // raised on the gamepad thread
    public event Action<string?>? Connected;      // name, or null when the last pad disconnects
    public event Action<int, float>? Axis;        // raw axis for the controller test screen

    public volatile bool Foreground = true;
    public bool Available { get; private set; }
    public string? PadName { get; private set; }
    /// <summary>Battery of the controller that connected last, in percent (-1 unknown); Charging while on USB power.</summary>
    public int Battery { get; private set; } = -1;
    public bool Charging { get; private set; }
    IntPtr _announce;
    long _announceAt;

    readonly Dictionary<uint, IntPtr> _pads = new();
    readonly Thread? _thread;
    volatile bool _run = true;
    // Stick-to-dpad state with hysteresis so a resting stick never repeats.
    readonly bool[] _stickDir = new bool[4];

    public Gamepad()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "Gamepad", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    // SDL is initialised on the thread that pumps its events (it owns a hidden RawInput window there).
    bool Init()
    {
        try
        {
            SDL_SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");
            SDL_SetHint("SDL_JOYSTICK_HIDAPI_PS4", "1");
            SDL_SetHint("SDL_JOYSTICK_HIDAPI_PS5", "1");
            SDL_SetHint("SDL_JOYSTICK_ENHANCED_REPORTS", "auto");
            if (!SDL_Init(SDL_INIT_JOYSTICK | SDL_INIT_GAMEPAD | SDL_INIT_EVENTS))
            {
                Log.Warn("SDL_Init failed: " + Marshal.PtrToStringUTF8(SDL_GetError()));
                return false;
            }
            return Available = true;
        }
        catch (DllNotFoundException)
        {
            Log.Warn("SDL3.dll missing; controller input disabled (keyboard still works).");
        }
        catch (Exception ex) { Log.Error("Gamepad init", ex); }
        return false;
    }

    void Loop()
    {
        if (!Init()) return;
        byte* ev = stackalloc byte[128];
        while (_run)
        {
            while (SDL_PollEvent(ev)) Handle(ev);
            // The battery level arrives with the controller's first input reports, so announce it a moment after connecting.
            if (_announce != IntPtr.Zero && Environment.TickCount64 >= _announceAt)
            {
                if (_pads.ContainsValue(_announce)) { ReadBattery(_announce); Connected?.Invoke(PadName); }
                _announce = IntPtr.Zero;
            }
            Thread.Sleep(Foreground ? 8 : 250);
        }
    }

    void Handle(byte* ev)
    {
        uint type = *(uint*)ev;
        uint which = *(uint*)(ev + 16);
        switch (type)
        {
            case EV_ADDED:
                var pad = SDL_OpenGamepad(which);
                if (pad != IntPtr.Zero)
                {
                    _pads[which] = pad;
                    PadName = Name(pad);
                    _announce = pad; _announceAt = Environment.TickCount64 + 1500;
                    SDL_SetGamepadLED(pad, 0, 40, 255); // PS4 blue
                    Log.Info("Controller connected: " + PadName);
                }
                break;
            case EV_REMOVED:
                if (_pads.Remove(which, out var p)) SDL_CloseGamepad(p);
                PadName = _pads.Count > 0 ? PadName : null;
                Log.Info("Controller removed");
                Connected?.Invoke(PadName);
                break;
            case EV_DOWN:
            case EV_UP:
                if (Map(ev[20]) is Btn b) Raise(b, type == EV_DOWN);
                break;
            case EV_AXIS:
                int axis = ev[20];
                short value = *(short*)(ev + 24);
                float v = value / 32767f;
                Axis?.Invoke(axis, v);
                if (axis == 0) Stick(v, Btn.Left, Btn.Right, 0, 1);
                else if (axis == 1) Stick(v, Btn.Up, Btn.Down, 2, 3);
                else if (axis == 4) Trigger(v, Btn.L2, 0);
                else if (axis == 5) Trigger(v, Btn.R2, 1);
                break;
        }
    }

    /// <summary>Sony DualShock 4 (both revisions and the wireless adapter) shows as on the console: "DUALSHOCK 4".</summary>
    static string Name(IntPtr pad)
    {
        if (SDL_GetGamepadVendor(pad) == 0x054C && SDL_GetGamepadProduct(pad) is 0x05C4 or 0x09CC or 0x0BA0) return "DUALSHOCK 4";
        return Marshal.PtrToStringUTF8(SDL_GetGamepadName(pad)) ?? "Controller";
    }

    void ReadBattery(IntPtr pad)
    {
        // SDL_PowerState: 1 on battery, 3 charging, 4 charged; anything else has no level to show.
        var state = SDL_GetGamepadPowerInfo(pad, out var p);
        Battery = state == 4 ? 100 : state is 1 or 3 && p >= 0 ? Math.Min(p, 100) : -1;
        Charging = state is 3 or 4;
    }

    readonly bool[] _trig = new bool[2];
    void Trigger(float v, Btn b, int i)
    {
        bool on = _trig[i] ? v > 0.3f : v > 0.6f;
        if (on != _trig[i]) { _trig[i] = on; Raise(b, on); }
    }

    void Stick(float v, Btn neg, Btn pos, int ni, int pi)
    {
        bool n = _stickDir[ni] ? v < -0.35f : v < -0.6f;
        bool p = _stickDir[pi] ? v > 0.35f : v > 0.6f;
        if (n != _stickDir[ni]) { _stickDir[ni] = n; Raise(neg, n); }
        if (p != _stickDir[pi]) { _stickDir[pi] = p; Raise(pos, p); }
    }

    void Raise(Btn b, bool down)
    {
        if (!Foreground && b != Btn.PS) return;
        Button?.Invoke(b, down);
    }

    static Btn? Map(byte sdl) => sdl switch
    {
        0 => Btn.Cross, 1 => Btn.Circle, 2 => Btn.Square, 3 => Btn.Triangle,
        4 => Btn.Share, 5 => Btn.PS, 6 => Btn.Options, 7 => Btn.L3, 8 => Btn.R3,
        9 => Btn.L1, 10 => Btn.R1, 11 => Btn.Up, 12 => Btn.Down, 13 => Btn.Left, 14 => Btn.Right,
        20 => Btn.Touchpad,
        _ => null,
    };

    public void Rumble(ushort lo = 0x4000, ushort hi = 0x4000, uint ms = 120)
    {
        foreach (var p in _pads.Values) SDL_RumbleGamepad(p, lo, hi, ms);
    }

    public void Dispose()
    {
        _run = false;
        _thread?.Join(500);
        if (Available) { foreach (var p in _pads.Values) SDL_CloseGamepad(p); SDL_Quit(); }
    }
}
