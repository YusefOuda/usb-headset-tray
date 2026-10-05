namespace UsbHeadsetTray.Tests;

static class TestStatus
{
    public static HeadsetStatus Online(int level, bool charging = false) =>
        new(HeadsetState.Online, level, charging, "Test Headset", [], [], null, null, null, null);

    public static HeadsetStatus Offline() =>
        new(HeadsetState.Offline, -1, false, "Test Headset", [], [], null, null, null, null);
}
