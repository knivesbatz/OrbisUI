using System.Windows;
using System.Windows.Threading;
using OrbisUI.Audio;
using OrbisUI.Core;
using OrbisUI.Input;
using OrbisUI.Services;
using OrbisUI.UI;

namespace OrbisUI;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        using var single = new Mutex(true, "OrbisUI.SingleInstance", out bool first);
        using var wake = new EventWaitHandle(false, EventResetMode.AutoReset, "OrbisUI.Wake");
        // Scripted test runs may sit beside the user's own copy.
        if (!first && Environment.GetEnvironmentVariable("ORBISUI_SCRIPT") == null)
        {
            wake.Set(); // ask the running copy to come to the front
            return 0;
        }

        Log.Info($"OrbisUI {Updater.CurrentText} starting from {AppPaths.Root}");
        Updater.CleanupOld();
        SonyResources.Import(); // before sounds and icons load, so a dropped-in backup is used at once
        var settings = AppSettings.Load();
        Sound.Init(settings.UiVolume, settings.UiSounds);
        // The console sound pack is used whenever it can be found next to the app.
        if (!Sound.HasPack && SoundPack.AutoFind() is string pack) SoundPack.Import(pack);
        IconPack.EnsureImported();

        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.DispatcherUnhandledException += (_, e) =>
        {
            Log.Error("Unhandled UI exception", e.Exception);
            e.Handled = true; // keep the shell alive; the error is in the log and shown as a notification
            Notifications.Post("Something went wrong", e.Exception.Message, "");
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Fatal", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { Log.Error("Background task", e.Exception); e.SetObserved(); };

        var win = new MainWindow();
        var pad = new Gamepad();
        Shell.Pad = pad;
        pad.Button += win.OnPadButton;
        // Like the console: the controller's name and battery when it connects, then the toast fades out.
        pad.Connected += name => win.Dispatcher.BeginInvoke(() =>
            Notifications.Post(name ?? "Controller disconnected", name == null ? "" :
                pad.Battery < 0 ? "Connected" : $"Connected · Battery {pad.Battery}%{(pad.Charging ? " (charging)" : "")}", ""));

        // Second launch -> bring this window back.
        ThreadPool.RegisterWaitForSingleObject(wake, (_, _) => win.Dispatcher.BeginInvoke(() =>
        {
            win.WindowState = WindowState.Maximized; win.Show(); win.Activate();
        }), null, -1, false);

        int code = app.Run(win);
        pad.Dispose();
        Log.Info("OrbisUI exited");
        return code;
    }
}
