using System.Text.Json.Nodes;

namespace UsbHeadsetTray.Tests;

public class HeadsetPollerTests
{
    // Real headsetcontrol 4.1.0 output for an Arctis Nova 3P with the dongle plugged in and the headset off
    static string OfflineJson() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "nova3p-off.json"));

    static string WithBattery(string status, int level)
    {
        var root = JsonNode.Parse(OfflineJson())!;
        var device = root["devices"]![0]!;
        device["status"] = "success";
        device["battery"]!["status"] = status;
        device["battery"]!["level"] = level;
        return root.ToJsonString();
    }

    [Fact]
    public void ParsesHeadsetOff()
    {
        var result = HeadsetPoller.Parse(OfflineJson());

        Assert.Equal(PollFailure.None, result.Failure);
        var status = result.Status!;
        Assert.Equal(HeadsetState.Offline, status.State);
        Assert.Equal(-1, status.BatteryLevel);
        Assert.Equal("Arctis Nova 3X Wireless", status.DeviceName);
        Assert.Contains("CAP_SIDETONE", status.Capabilities);
        Assert.Equal(["flat", "bass", "focus", "smiley"], status.EqPresetNames);
    }

    [Fact]
    public void ParsesHeadsetOn()
    {
        var status = HeadsetPoller.Parse(WithBattery("BATTERY_AVAILABLE", 62)).Status!;
        Assert.Equal(HeadsetState.Online, status.State);
        Assert.Equal(62, status.BatteryLevel);
        Assert.False(status.IsCharging);
    }

    [Fact]
    public void ParsesCharging()
    {
        var status = HeadsetPoller.Parse(WithBattery("BATTERY_CHARGING", -1)).Status!;
        Assert.Equal(HeadsetState.Online, status.State);
        Assert.True(status.IsCharging);
    }

    [Fact]
    public void NoDevicesReportsNoDeviceAsOffline()
    {
        var result = HeadsetPoller.Parse("""{ "device_count": 0, "devices": [] }""");
        Assert.Equal(PollFailure.NoDevice, result.Failure);
        Assert.Equal(HeadsetState.Offline, result.Status!.State);
    }

    [Theory]
    [InlineData("")]
    [InlineData("No supported device found")]
    [InlineData("{ \"devices\": ")]
    public void UnreadableOutputIsFailure(string output)
    {
        var result = HeadsetPoller.Parse(output);
        Assert.Equal(PollFailure.Failed, result.Failure);
        Assert.Null(result.Status);
    }
}
