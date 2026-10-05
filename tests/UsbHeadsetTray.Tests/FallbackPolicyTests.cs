namespace UsbHeadsetTray.Tests;

public class FallbackPolicyTests
{
    const string Bose = "bose", Speakers = "speakers", Dongle = "dongle", Other = "other";
    static readonly List<string> Fallbacks = [Bose, Speakers];

    static Func<string, DeviceState?> States(params string[] active) =>
        id => active.Contains(id) ? DeviceState.Active : DeviceState.Unplugged;

    [Fact]
    public void BestAvailableSkipsDisconnectedDevices() =>
        Assert.Equal(Speakers, FallbackPolicy.BestAvailable(Fallbacks, Dongle, States(Speakers)));

    [Fact]
    public void BestAvailablePrefersHigherRank() =>
        Assert.Equal(Bose, FallbackPolicy.BestAvailable(Fallbacks, Dongle, States(Bose, Speakers)));

    [Fact]
    public void BestAvailableNeverReturnsHeadset() =>
        Assert.Null(FallbackPolicy.BestAvailable([Dongle], Dongle, States(Dongle)));

    [Fact]
    public void BestAvailableNullWhenNoneConnected() =>
        Assert.Null(FallbackPolicy.BestAvailable(Fallbacks, Dongle, States()));

    [Theory]
    // Bose turned on while speakers are default: switch
    [InlineData(Bose, true, Speakers, true)]
    // Speakers connecting doesn't take over from the higher-ranked Bose
    [InlineData(Speakers, true, Bose, false)]
    // Any listed device outranks an unlisted default
    [InlineData(Speakers, true, Other, true)]
    // Unlisted devices never trigger a switch
    [InlineData(Other, true, null, false)]
    // Disconnects aren't arrivals
    [InlineData(Bose, false, Speakers, false)]
    // Headset dongle reconnecting isn't a fallback arrival
    [InlineData(Dongle, true, Speakers, false)]
    public void ShouldSwitchToConnected(string device, bool connected, string? currentDefault, bool expected)
    {
        List<string> fallbacks = [Bose, Speakers, Dongle];
        var state = connected ? DeviceState.Active : DeviceState.Unplugged;
        Assert.Equal(expected, FallbackPolicy.ShouldSwitchToConnected(fallbacks, Dongle, device, state, currentDefault));
    }

    [Fact]
    public void ReplacesWindowsChoiceWhenDefaultDisconnects() =>
        // Bose (default) turned off, Windows picked Other; Speakers is the best connected fallback
        Assert.Equal(Speakers, FallbackPolicy.ReplacementForDefaultChange(Fallbacks, Dongle, Bose, Other, States(Speakers, Other)));

    [Fact]
    public void KeepsWindowsChoiceWhenItIsAlreadyBest() =>
        Assert.Null(FallbackPolicy.ReplacementForDefaultChange(Fallbacks, Dongle, Bose, Speakers, States(Speakers)));

    [Fact]
    public void LeavesManualChangesAlone() =>
        // Bose still connected, so switching to Other was the user's choice
        Assert.Null(FallbackPolicy.ReplacementForDefaultChange(Fallbacks, Dongle, Bose, Other, States(Bose, Other)));

    [Fact]
    public void IgnoresChangeWithNoPreviousDefault() =>
        Assert.Null(FallbackPolicy.ReplacementForDefaultChange(Fallbacks, Dongle, null, Other, States(Speakers)));

    [Fact]
    public void RankPutsUnlistedLast()
    {
        Assert.Equal(0, FallbackPolicy.Rank(Fallbacks, Bose));
        Assert.Equal(int.MaxValue, FallbackPolicy.Rank(Fallbacks, Other));
        Assert.Equal(int.MaxValue, FallbackPolicy.Rank(Fallbacks, null));
    }
}
