namespace UsbHeadsetTray;

// Fallback priority rules, kept free of COM calls so they can be tested. Device state lookups are passed in.
// The headset's dongle endpoint stays active while the headset is off, so it is never treated as a fallback.
static class FallbackPolicy
{
    // Index in the priority list; unlisted devices rank lowest
    public static int Rank(IReadOnlyList<string> fallbacks, string? deviceId)
    {
        int i = deviceId == null ? -1 : fallbacks.IndexOf(deviceId);
        return i < 0 ? int.MaxValue : i;
    }

    public static string? BestAvailable(IReadOnlyList<string> fallbacks, string? headsetId, Func<string, DeviceState?> getState) =>
        fallbacks.FirstOrDefault(id => id != headsetId && getState(id) == DeviceState.Active);

    // A device changed state: switch to it if it just connected and outranks the current default
    public static bool ShouldSwitchToConnected(
        IReadOnlyList<string> fallbacks, string? headsetId, string deviceId, DeviceState newState, string? currentDefault) =>
        newState == DeviceState.Active
        && deviceId != headsetId
        && Rank(fallbacks, deviceId) < Rank(fallbacks, currentDefault);

    // The default changed from oldDefault to newDefault. If the old default disconnected, Windows picked the
    // replacement, so return the best fallback instead (null = keep). A change away from a still-connected
    // device is a manual choice (or ours) and is left alone.
    public static string? ReplacementForDefaultChange(
        IReadOnlyList<string> fallbacks, string? headsetId, string? oldDefault, string? newDefault, Func<string, DeviceState?> getState)
    {
        if (oldDefault == null || oldDefault == newDefault) return null;
        if (getState(oldDefault) == DeviceState.Active) return null;

        var best = BestAvailable(fallbacks, headsetId, getState);
        return best != newDefault ? best : null;
    }

    static int IndexOf(this IReadOnlyList<string> list, string item)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i] == item) return i;
        return -1;
    }
}
