using System.Diagnostics;
using System.Text.Json;

namespace UsbHeadsetTray;

enum HeadsetState { Unknown, Online, Offline }

record HeadsetStatus(
    HeadsetState State,
    int BatteryLevel,
    bool IsCharging,
    string DeviceName,
    List<string> Capabilities,
    List<string> EqPresetNames,
    // Null when the headset doesn't support reading the value
    int? Sidetone,
    int? MicVolume,
    int? InactiveTime,
    int? EqPreset
);

static class HeadsetPoller
{
    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    static string HeadsetControlExe()
    {
        var dir = Path.GetDirectoryName(Environment.ProcessPath);
        if (dir != null)
        {
            var bundled = Path.Combine(dir, "headsetcontrol.exe");
            if (File.Exists(bundled)) return bundled;
        }
        return "headsetcontrol";
    }

    public static async Task<HeadsetStatus?> PollAsync()
    {
        try
        {
            var psi = new ProcessStartInfo(HeadsetControlExe(), "-o json")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var proc = Process.Start(psi)!;
            var json = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();

            var output = JsonSerializer.Deserialize<HeadsetControlOutput>(json, JsonOptions);
            var device = output?.Devices?.FirstOrDefault();
            if (device?.Battery == null) return null;

            var bat = device.Battery;
            var isCharging = bat.Status == "BATTERY_CHARGING";
            var isOnline = bat.Status is "BATTERY_AVAILABLE" or "BATTERY_CHARGING";
            var capabilities = device.Capabilities ?? [];
            var eqPresets = device.EqualizerPresets?.Keys.ToList() ?? [];

            return new HeadsetStatus(
                isOnline ? HeadsetState.Online : HeadsetState.Offline,
                bat.Level,
                isCharging,
                device.Product ?? device.Device,
                capabilities,
                eqPresets,
                device.Sidetone,
                device.MicrophoneVolume,
                device.InactiveTime,
                device.EqualizerPreset);
        }
        catch
        {
            return null;
        }
    }

    public static async Task RunCommandAsync(string args)
    {
        try
        {
            var psi = new ProcessStartInfo(HeadsetControlExe(), args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var proc = Process.Start(psi)!;
            await proc.WaitForExitAsync();
        }
        catch { }
    }
}
