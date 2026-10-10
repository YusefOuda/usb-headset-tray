namespace UsbHeadsetTray;

// Plain-text log next to settings.json (%APPDATA%\usb-headset-tray\usb-headset-tray.log). Rolls over to
// usb-headset-tray.old.log at 1 MB. Any thread; never throws.
static class Log
{
    const long MaxBytes = 1024 * 1024;

    public static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "usb-headset-tray");
    public static readonly string FilePath = Path.Combine(Folder, "usb-headset-tray.log");
    static readonly string OldPath = Path.Combine(Folder, "usb-headset-tray.old.log");
    static readonly object Lock = new();

    public static void Info(string message) => Write("INFO ", message);

    public static void Error(string message, Exception? e = null) =>
        Write("ERROR", e == null ? message : $"{message}\n{e}");

    static void Write(string level, string message)
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(Folder);
                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length > MaxBytes) File.Move(FilePath, OldPath, overwrite: true);
                File.AppendAllText(FilePath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} [{Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}");
            }
        }
        catch { }
    }
}
