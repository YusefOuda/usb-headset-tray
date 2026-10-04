using System.Text.Json;
using System.Text.Json.Serialization;

namespace UsbHeadsetTray;

record HeadsetControlOutput(
    [property: JsonPropertyName("device_count")] int DeviceCount,
    [property: JsonPropertyName("devices")] List<DeviceInfo> Devices
);

record DeviceInfo(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("device")] string Device,
    [property: JsonPropertyName("product")] string? Product,
    [property: JsonPropertyName("capabilities")] List<string>? Capabilities,
    [property: JsonPropertyName("battery")] BatteryInfo? Battery,
    [property: JsonPropertyName("equalizer_presets")] Dictionary<string, JsonElement>? EqualizerPresets,
    // Present only on headsets that support reading these values
    [property: JsonPropertyName("sidetone")] int? Sidetone,
    [property: JsonPropertyName("microphone_volume")] int? MicrophoneVolume,
    [property: JsonPropertyName("inactive_time")] int? InactiveTime,
    [property: JsonPropertyName("equalizer_preset")] int? EqualizerPreset
);

record BatteryInfo(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("level")] int Level
);
