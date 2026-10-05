using System.ComponentModel;
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
)
{
    // Used when headsetcontrol runs fine but finds no supported device (e.g. dongle unplugged)
    public static readonly HeadsetStatus NoDevice =
        new(HeadsetState.Offline, -1, false, "", [], [], null, null, null, null);
}

enum PollFailure
{
    None,
    // headsetcontrol ran but reported no usable device; Status is NoDevice
    NoDevice,
    // headsetcontrol.exe couldn't be started
    NotFound,
    // headsetcontrol ran but its output couldn't be read
    Failed,
}

// Status is non-null for None and NoDevice
record PollResult(HeadsetStatus? Status, PollFailure Failure);

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

    public static async Task<PollResult> PollAsync()
    {
        string json;
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
            json = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 2) // ERROR_FILE_NOT_FOUND
        {
            return new(null, PollFailure.NotFound);
        }
        catch
        {
            return new(null, PollFailure.Failed);
        }
        return Parse(json);
    }

    // Exit code is ignored: the JSON is what tells us whether a device was found.
    public static PollResult Parse(string json)
    {
        HeadsetControlOutput? output;
        try
        {
            output = JsonSerializer.Deserialize<HeadsetControlOutput>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return new(null, PollFailure.Failed);
        }
        if (output == null) return new(null, PollFailure.Failed);

        var device = output.Devices?.FirstOrDefault();
        if (device?.Battery == null) return new(HeadsetStatus.NoDevice, PollFailure.NoDevice);

        var bat = device.Battery;
        var isCharging = bat.Status == "BATTERY_CHARGING";
        var isOnline = bat.Status is "BATTERY_AVAILABLE" or "BATTERY_CHARGING";
        var capabilities = device.Capabilities ?? [];
        var eqPresets = device.EqualizerPresets?.Keys.ToList() ?? [];

        return new(new HeadsetStatus(
            isOnline ? HeadsetState.Online : HeadsetState.Offline,
            bat.Level,
            isCharging,
            device.Product ?? device.Device,
            capabilities,
            eqPresets,
            device.Sidetone,
            device.MicrophoneVolume,
            device.InactiveTime,
            device.EqualizerPreset), PollFailure.None);
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
