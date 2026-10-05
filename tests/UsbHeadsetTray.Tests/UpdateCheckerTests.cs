namespace UsbHeadsetTray.Tests;

public class UpdateCheckerTests
{
    [Theory]
    [InlineData("v1.0.3", "1.0.2.0", true)]
    [InlineData("v1.0.2", "1.0.2.0", false)] // tag parses without a revision; must still equal 1.0.2.0
    [InlineData("v1.0.1", "1.0.2.0", false)]
    [InlineData("v1.1.0", "1.0.9.0", true)]
    [InlineData("v2.0", "1.9.9.0", true)]
    [InlineData("1.2.0", "1.1.0.0", true)]
    [InlineData("v1.1.0-beta", "1.0.0.0", false)] // pre-release tags are ignored
    [InlineData("latest", "1.0.0.0", false)]
    public void IsNewer(string tag, string current, bool expected) =>
        Assert.Equal(expected, UpdateChecker.IsNewer(tag, Version.Parse(current)));
}
