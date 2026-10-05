namespace UsbHeadsetTray.Tests;

public class BatteryEstimatorTests
{
    static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    // Polls once a minute (inside the 2-minute gap limit) from fromMinute to toMinute inclusive
    static TimeSpan? Feed(BatteryEstimator estimator, int fromMinute, int toMinute, int level)
    {
        TimeSpan? result = null;
        for (int m = fromMinute; m <= toMinute; m++)
            result = estimator.Update(TestStatus.Online(level), T0.AddMinutes(m));
        return result;
    }

    // 80% until minute 9, 75% from minute 10, 70% from minute 40: live rate 5% per 30 min = 10%/h
    static BatteryEstimator WithMeasuredRun(double? learnedRate = null)
    {
        var estimator = new BatteryEstimator(learnedRate);
        Feed(estimator, 0, 9, 80);
        Feed(estimator, 10, 39, 75);
        Feed(estimator, 40, 40, 70);
        return estimator;
    }

    [Fact]
    public void NoEstimateWithoutLearnedRateOrEnoughData()
    {
        var estimator = new BatteryEstimator(null);
        Assert.Null(Feed(estimator, 0, 9, 80));
        // One new low isn't enough to measure a rate
        Assert.Null(Feed(estimator, 10, 30, 75));
    }

    [Fact]
    public void UsesLearnedRateImmediately()
    {
        var estimator = new BatteryEstimator(10);
        Assert.Equal(TimeSpan.FromHours(5), estimator.Update(TestStatus.Online(50), T0));
    }

    [Fact]
    public void MeasuresRateBetweenNewLows()
    {
        var estimator = new BatteryEstimator(null);
        Feed(estimator, 0, 9, 80);
        Feed(estimator, 10, 39, 75);
        var result = Feed(estimator, 40, 40, 70);

        Assert.Equal(10, estimator.LearnedRate!.Value, precision: 6);
        Assert.Equal(7, result!.Value.TotalHours, precision: 6);
    }

    [Fact]
    public void DoesNotTrustRateBeforeMinimumSpan()
    {
        var estimator = new BatteryEstimator(null);
        Feed(estimator, 0, 4, 80);
        Feed(estimator, 5, 9, 75);
        // Second new low only 5 minutes after the first
        Assert.Null(Feed(estimator, 10, 10, 70));
        Assert.Null(estimator.LearnedRate);
    }

    [Fact]
    public void CountsDownWithinStep()
    {
        var estimator = WithMeasuredRun();
        // 15 minutes after reaching 70%: 7h - 15m
        var result = Feed(estimator, 41, 55, 70);
        Assert.Equal(6.75, result!.Value.TotalHours, precision: 6);
    }

    [Fact]
    public void CountdownStopsAtOneAverageStep()
    {
        var estimator = WithMeasuredRun();
        // Average step is 5% = 30 min at 10%/h, so the countdown stops at 6.5h however long the step lasts
        var result = Feed(estimator, 41, 100, 70);
        Assert.Equal(6.5, result!.Value.TotalHours, precision: 6);
    }

    [Fact]
    public void BlendsLiveRateIntoLearnedRateWithoutCompounding()
    {
        var estimator = WithMeasuredRun(learnedRate: 20);
        Assert.Equal(15, estimator.LearnedRate!.Value, precision: 6);

        // Another new low in the same run, still 10%/h overall: blend stays against the run's starting value
        Feed(estimator, 41, 69, 70);
        Feed(estimator, 70, 70, 65);
        Assert.Equal(15, estimator.LearnedRate!.Value, precision: 6);
    }

    [Fact]
    public void IgnoresLevelBouncingBackUp()
    {
        var estimator = new BatteryEstimator(null);
        Feed(estimator, 0, 9, 80);
        Feed(estimator, 10, 19, 75);
        Feed(estimator, 20, 24, 76);
        Feed(estimator, 25, 39, 75);
        Feed(estimator, 40, 40, 70);
        Assert.Equal(10, estimator.LearnedRate!.Value, precision: 6);
    }

    [Fact]
    public void GapStartsNewRunUsingLearnedRate()
    {
        var estimator = WithMeasuredRun(learnedRate: 20); // learned becomes 15
        // 5-minute gap (e.g. sleep) breaks the run; the live 10%/h no longer applies
        var result = estimator.Update(TestStatus.Online(60), T0.AddMinutes(46));
        Assert.Equal(60 / 15.0, result!.Value.TotalHours, precision: 6);
    }

    [Fact]
    public void ChargingAndOfflineGiveNoEstimate()
    {
        var estimator = new BatteryEstimator(10);
        Assert.Null(estimator.Update(TestStatus.Online(50, charging: true), T0));
        Assert.Null(estimator.Update(TestStatus.Offline(), T0.AddMinutes(1)));
    }

    [Theory]
    [InlineData(0, "<5m")]
    [InlineData(2, "<5m")]
    [InlineData(44, "~45m")]
    [InlineData(60, "~1h")]
    [InlineData(9 * 60 + 38, "~9h 40m")]
    [InlineData(9 * 60 + 40, "~9h 40m")]
    public void FormatsRoundedToFiveMinutes(int minutes, string expected) =>
        Assert.Equal(expected, BatteryEstimator.Format(TimeSpan.FromMinutes(minutes)));
}
