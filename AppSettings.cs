using System.Text.Json;
using System.Text.Json.Serialization;

namespace UsbHeadsetTray;

class AppSettings
{
    // Output (playback) switching. Property names predate input switching and are kept for settings compatibility.
    public bool AutoSwitch { get; set; } = true;
    public string? HeadsetDeviceId { get; set; }
    // Ordered by priority, highest first. Empty = restore whatever was default before the headset came online.
    public List<string> FallbackDeviceIds { get; set; } = [];

    // Input (recording) switching. Off by default, and does nothing until a headset mic is picked.
    public bool AutoSwitchInput { get; set; } = false;
    public string? HeadsetInputDeviceId { get; set; }
    public List<string> FallbackInputDeviceIds { get; set; } = [];
    // Legacy single fallback; migrated into FallbackDeviceIds on load.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FallbackDeviceId { get; set; }
    public bool StartWithWindows { get; set; } = false;
    // Tray icon shows the battery percentage as a number instead of the battery bar
    public bool IconShowsPercentage { get; set; } = false;
    // Learned battery drain in percent per hour, maintained by BatteryEstimator. Null until first measured.
    public double? BatteryDrainPerHour { get; set; }
    // Percent at or below which to show a low-battery notification; 0 = off
    public int LowBatteryThreshold { get; set; } = 15;
    public bool NotifyChargeComplete { get; set; } = true;
    public bool CheckForUpdates { get; set; } = true;

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
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new();
                settings.MigrateLegacy();
                return settings;
            }
        }
        catch { }
        return new();
    }

    // Per-direction accessors so output and input switching share one code path
    public bool IsAutoSwitchOn(EDataFlow flow) => flow == EDataFlow.eCapture ? AutoSwitchInput : AutoSwitch;

    public void SetAutoSwitch(EDataFlow flow, bool on)
    {
        if (flow == EDataFlow.eCapture) AutoSwitchInput = on;
        else AutoSwitch = on;
    }

    public string? HeadsetDevice(EDataFlow flow) => flow == EDataFlow.eCapture ? HeadsetInputDeviceId : HeadsetDeviceId;

    public void SetHeadsetDevice(EDataFlow flow, string? id)
    {
        if (flow == EDataFlow.eCapture) HeadsetInputDeviceId = id;
        else HeadsetDeviceId = id;
    }

    public List<string> Fallbacks(EDataFlow flow) => flow == EDataFlow.eCapture ? FallbackInputDeviceIds : FallbackDeviceIds;

    public void MigrateLegacy()
    {
        if (FallbackDeviceId == null) return;
        if (!FallbackDeviceIds.Contains(FallbackDeviceId))
            FallbackDeviceIds.Insert(0, FallbackDeviceId);
        FallbackDeviceId = null;
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
