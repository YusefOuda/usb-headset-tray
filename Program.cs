namespace UsbHeadsetTray;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Used by the installer: asks the running instance to exit before its files are replaced
        if (args.Contains("--quit"))
        {
            QuitSignal.Send();
            return;
        }

        using var mutex = new Mutex(true, "usb-headset-tray-{B3E2C3D4-5F6A-7B8C-9D0E}", out var firstInstance);
        if (!firstInstance) return;

        Log.Info($"usb-headset-tray {UpdateChecker.CurrentVersion} starting");
        // Log UI-thread exceptions and keep running instead of showing WinForms' Continue/Quit dialog
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Error("Unhandled exception on the UI thread", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Error($"Unhandled exception (terminating: {e.IsTerminating})", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => Log.Error("Unobserved task exception", e.Exception);

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        // Lets QuitSignal post to this thread; no Control exists yet to install it
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        var app = new TrayApp();
        QuitSignal.Listen(app.ExitApp);
        Application.Run(app);
        Log.Info("Exiting");
    }
}
