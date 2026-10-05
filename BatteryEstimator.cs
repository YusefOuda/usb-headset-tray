namespace UsbHeadsetTray;

// Estimates battery time remaining from how fast the reported level drops while discharging.
// Many headsets report coarse steps (5–10%), so the rate is measured between the moments the level
// first reaches a new low rather than across every sample, which would be skewed by the staircase.
class BatteryEstimator
{
    // A longer gap between samples (sleep, polling stalled) means the readings no longer form one discharge run
    static readonly TimeSpan MaxGap = TimeSpan.FromMinutes(2);
    // Minimum time between the first and latest new lows before the live rate is trusted
    static readonly TimeSpan MinSpan = TimeSpan.FromMinutes(15);

    bool _running;
    DateTime _lastSample;
    int _startLevel;
    int _lowest;
    int _lowCount;
    DateTime _latestLowTime;
    (DateTime Time, int Level)? _firstLow;
    double? _liveRate;
    // Learned rate as of the start of this run. The live rate is blended into this rather than into the
    // current learned value, so updating it on every new low doesn't compound the blend.
    double? _baselineRate;

    // Drain in percent per hour, carried across runs. The caller persists it when it changes.
    public double? LearnedRate { get; private set; }

    public BatteryEstimator(double? learnedRate) => LearnedRate = learnedRate;

    // Call on every poll. Returns the estimated time remaining, or null when discharging data is insufficient.
    public TimeSpan? Update(HeadsetStatus status, DateTime now)
    {
        bool discharging = status.State == HeadsetState.Online && !status.IsCharging && status.BatteryLevel >= 0;
        if (!discharging)
        {
            _running = false;
            return null;
        }

        if (!_running || now - _lastSample > MaxGap)
            StartRun(status.BatteryLevel);
        _lastSample = now;

        // Track new lows only, so a level that bounces back up briefly doesn't count as a step
        if (status.BatteryLevel < _lowest)
        {
            _lowest = status.BatteryLevel;
            _lowCount++;
            _latestLowTime = now;
            _firstLow ??= (now, _lowest);
            UpdateLiveRate(now);
        }

        var rate = _liveRate ?? LearnedRate;
        if (rate is not > 0) return null;

        double hours = _lowest / rate.Value;
        if (_lowCount > 0)
        {
            // The level has been draining since it reached _lowest; count down within the current step
            // (average observed step size), but never past where the next step should begin.
            double stepHours = (double)(_startLevel - _lowest) / _lowCount / rate.Value;
            hours -= Math.Min((now - _latestLowTime).TotalHours, stepHours);
        }
        return TimeSpan.FromHours(Math.Max(hours, 0));
    }

    // Rounded to 5 minutes; the estimate isn't precise enough to justify more
    public static string Format(TimeSpan t)
    {
        int minutes = (int)Math.Round(t.TotalMinutes / 5) * 5;
        if (minutes < 5) return "<5m";
        if (minutes < 60) return $"~{minutes}m";
        return minutes % 60 == 0 ? $"~{minutes / 60}h" : $"~{minutes / 60}h {minutes % 60}m";
    }

    void StartRun(int level)
    {
        _running = true;
        _startLevel = level;
        _lowest = level;
        _lowCount = 0;
        _firstLow = null;
        _liveRate = null;
        _baselineRate = LearnedRate;
    }

    void UpdateLiveRate(DateTime now)
    {
        var first = _firstLow!.Value;
        var span = now - first.Time;
        if (span < MinSpan) return;

        _liveRate = (first.Level - _lowest) / span.TotalHours;
        // Lets the next run show an estimate immediately; blended so one unusual run doesn't replace it
        LearnedRate = _baselineRate is double baseline ? (baseline + _liveRate.Value) / 2 : _liveRate;
    }
}
