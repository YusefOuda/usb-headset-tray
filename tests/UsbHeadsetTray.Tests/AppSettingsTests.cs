using System.Text.Json;

namespace UsbHeadsetTray.Tests;

public class AppSettingsTests
{
    [Fact]
    public void MigratesLegacyFallbackToFrontOfList()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("""{ "FallbackDeviceId": "speakers" }""")!;
        settings.MigrateLegacy();

        Assert.Equal(["speakers"], settings.FallbackDeviceIds);
        Assert.Null(settings.FallbackDeviceId);
    }

    [Fact]
    public void MigrationDoesNotDuplicate()
    {
        var settings = new AppSettings { FallbackDeviceId = "speakers", FallbackDeviceIds = ["bose", "speakers"] };
        settings.MigrateLegacy();
        Assert.Equal(["bose", "speakers"], settings.FallbackDeviceIds);
    }

    [Fact]
    public void InputSwitchingIsOffAndUnconfiguredByDefault()
    {
        var settings = new AppSettings();
        Assert.True(settings.IsAutoSwitchOn(EDataFlow.eRender));
        Assert.False(settings.IsAutoSwitchOn(EDataFlow.eCapture));
        Assert.Null(settings.HeadsetDevice(EDataFlow.eCapture));
    }

    [Fact]
    public void DirectionAccessorsKeepOutputAndInputSeparate()
    {
        var settings = new AppSettings();
        settings.SetAutoSwitch(EDataFlow.eRender, false);
        settings.SetAutoSwitch(EDataFlow.eCapture, true);
        settings.SetHeadsetDevice(EDataFlow.eRender, "dongle-out");
        settings.SetHeadsetDevice(EDataFlow.eCapture, "dongle-mic");
        settings.Fallbacks(EDataFlow.eCapture).Add("desk-mic");

        Assert.False(settings.AutoSwitch);
        Assert.True(settings.AutoSwitchInput);
        Assert.Equal("dongle-out", settings.HeadsetDeviceId);
        Assert.Equal("dongle-mic", settings.HeadsetInputDeviceId);
        Assert.Empty(settings.FallbackDeviceIds);
        Assert.Equal(["desk-mic"], settings.FallbackInputDeviceIds);
    }

    [Fact]
    public void LegacyFieldNotWrittenOnceMigrated()
    {
        var json = JsonSerializer.Serialize(new AppSettings());
        Assert.DoesNotContain("\"FallbackDeviceId\"", json);
    }
}
