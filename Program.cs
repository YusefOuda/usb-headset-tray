namespace UsbHeadsetTray;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, "usb-headset-tray-{B3E2C3D4-5F6A-7B8C-9D0E}", out var firstInstance);
        if (!firstInstance) return;

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.Run(new TrayApp());
    }
}
