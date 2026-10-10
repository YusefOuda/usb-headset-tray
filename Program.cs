namespace UsbHeadsetTray;

static class Program
{
    [STAThread]
    static void Main()
    {
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
        Application.Run(new TrayApp());
        Log.Info("Exiting");
    }
}
