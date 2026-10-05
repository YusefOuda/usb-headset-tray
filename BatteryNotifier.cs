namespace UsbHeadsetTray;

record BatteryNotification(string Title, string Text, bool IsWarning);

// Decides when to show low-battery and charge-complete notifications; TrayApp displays them.
// Each alert fires once, then re-arms when charging starts (or, for low battery, when the level
// climbs well above the threshold), so a level hovering around a threshold doesn't repeat it.
class BatteryNotifier
{
    public const int CriticalLevel = 5;
    // How far above the threshold the level must rise to re-arm the low alert without charging
    const int RearmMargin = 5;
    // Some headsets stop reporting "charging" once full instead of reporting 100% while charging
    const int ChargedLevel = 95;

    bool _lowAlerted;
    bool _criticalAlerted;
    bool _chargeAlerted;
    bool _wasCharging;

    // lowThreshold 0 disables low/critical alerts.
    public BatteryNotification? Update(HeadsetStatus status, int lowThreshold, bool notifyChargeComplete, TimeSpan? remaining)
    {
        // Low-alert state is kept across off/on so turning the headset back on at the same level doesn't repeat it
        if (status.State != HeadsetState.Online)
        {
            _wasCharging = false;
            return null;
        }

        int level = status.BatteryLevel;

        if (status.IsCharging)
        {
            if (!_wasCharging) _chargeAlerted = false;
            _wasCharging = true;
            _lowAlerted = _criticalAlerted = false;

            if (notifyChargeComplete && !_chargeAlerted && level >= 100)
                return ChargeComplete(level);
            return null;
        }

        bool stoppedCharging = _wasCharging;
        _wasCharging = false;
        if (stoppedCharging && notifyChargeComplete && !_chargeAlerted && level >= ChargedLevel)
            return ChargeComplete(level);

        if (level < 0 || lowThreshold <= 0) return null;

        if (level > lowThreshold + RearmMargin) _lowAlerted = false;
        if (level > CriticalLevel + RearmMargin) _criticalAlerted = false;

        string left = remaining is TimeSpan t ? $" ({BatteryEstimator.Format(t)} left)" : "";

        if (level <= CriticalLevel && !_criticalAlerted)
        {
            _criticalAlerted = _lowAlerted = true;
            return new("Headset battery critical", $"Battery at {level}%{left}", IsWarning: true);
        }
        if (level <= lowThreshold && !_lowAlerted)
        {
            _lowAlerted = true;
            return new("Headset battery low", $"Battery at {level}%{left}", IsWarning: true);
        }
        return null;
    }

    BatteryNotification ChargeComplete(int level)
    {
        _chargeAlerted = true;
        return new("Headset charged",
            level >= 100 ? "Battery is fully charged" : $"Battery charged to {level}%", IsWarning: false);
    }
}
