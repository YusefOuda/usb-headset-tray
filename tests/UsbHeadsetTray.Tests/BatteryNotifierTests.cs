namespace UsbHeadsetTray.Tests;

public class BatteryNotifierTests
{
    readonly BatteryNotifier _notifier = new();

    BatteryNotification? Discharging(int level, int threshold = 15, TimeSpan? remaining = null) =>
        _notifier.Update(TestStatus.Online(level), threshold, notifyChargeComplete: true, remaining);

    BatteryNotification? Charging(int level, bool notify = true) =>
        _notifier.Update(TestStatus.Online(level, charging: true), 15, notify, null);

    [Fact]
    public void LowAlertFiresOnceAtThreshold()
    {
        Assert.Null(Discharging(16));
        Assert.Equal("Headset battery low", Discharging(15)?.Title);
        Assert.Null(Discharging(14));
    }

    [Fact]
    public void LowAlertIncludesTimeLeft()
    {
        var note = Discharging(15, remaining: TimeSpan.FromMinutes(80));
        Assert.Equal("Battery at 15% (~1h 20m left)", note?.Text);
        Assert.True(note?.IsWarning);
    }

    [Fact]
    public void CriticalAlertFollowsLowAlert()
    {
        Assert.NotNull(Discharging(15));
        Assert.Equal("Headset battery critical", Discharging(5)?.Title);
        Assert.Null(Discharging(4));
    }

    [Fact]
    public void StartingBelowCriticalOnlyGivesCriticalAlert()
    {
        Assert.Equal("Headset battery critical", Discharging(4)?.Title);
        Assert.Null(Discharging(3));
    }

    [Fact]
    public void ThresholdZeroDisablesAlerts() =>
        Assert.Null(Discharging(3, threshold: 0));

    [Fact]
    public void HoveringAroundThresholdDoesNotRepeat()
    {
        Assert.NotNull(Discharging(15));
        Assert.Null(Discharging(16));
        Assert.Null(Discharging(15));
        // Well above the threshold re-arms it
        Assert.Null(Discharging(21));
        Assert.NotNull(Discharging(15));
    }

    [Fact]
    public void ChargingRearmsLowAlert()
    {
        Assert.NotNull(Discharging(15));
        Charging(16);
        Assert.NotNull(Discharging(15));
    }

    [Fact]
    public void TurningOffAndOnAtSameLevelDoesNotRepeat()
    {
        Assert.NotNull(Discharging(15));
        _notifier.Update(TestStatus.Offline(), 15, true, null);
        Assert.Null(Discharging(15));
    }

    [Fact]
    public void ChargeCompleteAtFullWhileCharging()
    {
        Assert.Null(Charging(99));
        var note = Charging(100);
        Assert.Equal("Battery is fully charged", note?.Text);
        Assert.False(note?.IsWarning);
        Assert.Null(Charging(100));
    }

    [Fact]
    public void ChargeCompleteWhenChargingStopsNearFull()
    {
        // Many headsets report level -1 while charging, then stop charging once full
        Charging(-1);
        Assert.Equal("Battery charged to 98%", Discharging(98)?.Text);
    }

    [Fact]
    public void NoChargeCompleteWhenUnpluggedEarly()
    {
        Charging(-1);
        Assert.Null(Discharging(60));
    }

    [Fact]
    public void ChargeCompleteRespectsSetting() =>
        Assert.Null(Charging(100, notify: false));

    [Fact]
    public void NoChargeCompleteWhenHeadsetTurnedOffWhileCharging()
    {
        Charging(-1);
        _notifier.Update(TestStatus.Offline(), 15, true, null);
        Assert.Null(Discharging(100));
    }
}
