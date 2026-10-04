using System.Text.Json;

namespace UsbHeadsetTray;

class AppSettings
{
    public bool AutoSwitch { get; set; } = true;
    public string? HeadsetDeviceId { get; set; }
    public string? FallbackDeviceId { get; set; }
    public bool StartWithWindows { get; set; } = false;

    // Null = never set by this app; slider will show "–" until the user explicitly moves it.
    // We never apply these to the headset automatically — only when the user interacts.
    public int? Sidetone { get; set; } = null;
    public int? MicVolume { get; set; } = null;
    public int InactiveTime { get; set; } = 0;
    public int EqPreset { get; set; } = 0;

    static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "usb-headset-tray",
        "settings.json");

    static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new();
        }
        catch { }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, WriteOptions));
        }
        catch { }
    }
}
