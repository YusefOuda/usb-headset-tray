using Microsoft.Win32;

namespace UsbHeadsetTray;

class TrayApp : ApplicationContext
{
    readonly NotifyIcon _tray;
    readonly System.Windows.Forms.Timer _timer;
    AppSettings _settings;
    List<AudioDevice> _devices = [];
    List<string> _lastCapabilities = [];
    List<string> _lastEqPresetNames = [];
    bool _headsetOnline = false;
    // Values read from the headset JSON when the headset reports them; null if not queryable.
    // Never persisted — cleared when the headset goes offline.
    int? _queriedSidetone;
    int? _queriedMicVolume;
    int? _queriedInactiveTime;
    int? _queriedEqPreset;
    string? _previousDefaultDevice;
    HeadsetState _lastState = HeadsetState.Unknown;
    ContextMenuStrip? _menu;

    public TrayApp()
    {
        _settings = AppSettings.Load();
        _devices = AudioDeviceManager.GetPlaybackDevices();

        _tray = new NotifyIcon
        {
            Icon = IconRenderer.Disconnected(),
            Text = "usb-headset-tray",
            Visible = true,
        };

        RebuildMenu();

        _timer = new System.Windows.Forms.Timer { Interval = 3000 };
        _timer.Tick += Timer_Tick;
        _timer.Start();
    }

    async void Timer_Tick(object? sender, EventArgs e)
    {
        var status = await HeadsetPoller.PollAsync();
        if (status != null)
            ApplyStatus(status);
    }

    void RebuildMenu()
    {
        _devices = AudioDeviceManager.GetPlaybackDevices();
        _menu = BuildMenu();
        _tray.ContextMenuStrip = _menu;
    }

    ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        var autoSwitch = new ToolStripMenuItem("Auto-switch audio") { CheckOnClick = true, Checked = _settings.AutoSwitch };
        autoSwitch.CheckedChanged += (_, _) =>
        {
            _settings.AutoSwitch = autoSwitch.Checked;
            _settings.Save();
            if (autoSwitch.Checked && _headsetOnline && _settings.HeadsetDeviceId != null)
            {
                _previousDefaultDevice = AudioDeviceManager.GetDefaultPlaybackDeviceId();
                TrySetDefault(_settings.HeadsetDeviceId);
            }
        };
        menu.Items.Add(autoSwitch);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(BuildDeviceSubmenu("Headset output device", _settings.HeadsetDeviceId,
            id => { _settings.HeadsetDeviceId = id; _settings.Save(); RebuildMenu(); }));
        menu.Items.Add(BuildDeviceSubmenu("Fallback output device", _settings.FallbackDeviceId,
            id => { _settings.FallbackDeviceId = id; _settings.Save(); RebuildMenu(); },
            includeNone: true));

        if (_headsetOnline && _lastCapabilities.Count > 0)
        {
            var controls = BuildHeadsetControlsSubmenu();
            if (controls.DropDownItems.Count > 0)
            {
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add(controls);
            }
        }

        menu.Items.Add(new ToolStripSeparator());

        var startWithWindows = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true, Checked = _settings.StartWithWindows };
        startWithWindows.CheckedChanged += (_, _) =>
        {
            _settings.StartWithWindows = startWithWindows.Checked;
            _settings.Save();
            ApplyStartWithWindows(startWithWindows.Checked);
        };
        menu.Items.Add(startWithWindows);

        var exit = new ToolStripMenuItem("Exit");
        exit.Click += (_, _) => ExitApp();
        menu.Items.Add(exit);

        return menu;
    }

    ToolStripMenuItem BuildHeadsetControlsSubmenu()
    {
        var parent = new ToolStripMenuItem("Headset Controls");
        bool needsSep = false;
        void Sep() { if (needsSep) parent.DropDownItems.Add(new ToolStripSeparator()); needsSep = true; }

        if (_lastCapabilities.Contains("CAP_SIDETONE"))
        {
            Sep();
            // Prefer value read from headset; fall back to last value set by this app
            parent.DropDownItems.Add(BuildSliderItem("Sidetone", 0, 128,
                _queriedSidetone ?? _settings.Sidetone,
                v => { _settings.Sidetone = v; _settings.Save(); _ = HeadsetPoller.RunCommandAsync($"-s {v}"); }));
        }

        if (_lastCapabilities.Contains("CAP_MICROPHONE_VOLUME"))
        {
            Sep();
            parent.DropDownItems.Add(BuildSliderItem("Mic Volume", 0, 128,
                _queriedMicVolume ?? _settings.MicVolume,
                v => { _settings.MicVolume = v; _settings.Save(); _ = HeadsetPoller.RunCommandAsync($"--microphone-volume {v}"); }));
        }

        if (_lastCapabilities.Contains("CAP_INACTIVE_TIME"))
        {
            Sep();
            parent.DropDownItems.Add(BuildInactiveTimeSubmenu(_queriedInactiveTime));
        }

        if (_lastCapabilities.Contains("CAP_EQUALIZER_PRESET"))
        {
            Sep();
            parent.DropDownItems.Add(BuildEqPresetSubmenu(_queriedEqPreset));
        }

        return parent;
    }

    static ToolStripItem BuildSliderItem(string label, int min, int max, int? current, Action<int> onCommit)
    {
        // null = headset value unknown; show "–" and position at min to avoid implying a known state
        string labelText = current.HasValue ? $"{label}: {current}" : $"{label}: –";
        var panel = new Panel { Width = 200, Height = 44, BackColor = SystemColors.Menu };
        var lbl = new Label
        {
            Text = labelText,
            AutoSize = true,
            Location = new Point(6, 2),
            ForeColor = SystemColors.MenuText,
            BackColor = Color.Transparent,
        };
        var slider = new TrackBar
        {
            Minimum = min, Maximum = max,
            Value = current.HasValue ? Math.Clamp(current.Value, min, max) : min,
            TickFrequency = (max - min) / 4,
            Location = new Point(2, 18),
            Width = 196, Height = 24,
            AutoSize = false,
        };
        slider.ValueChanged += (_, _) => lbl.Text = $"{label}: {slider.Value}";
        slider.MouseUp += (_, _) => onCommit(slider.Value);
        slider.KeyUp += (_, _) => onCommit(slider.Value);
        panel.Controls.AddRange([lbl, slider]);
        return new ToolStripControlHost(panel) { AutoSize = false, Size = new Size(200, 46) };
    }

    ToolStripMenuItem BuildInactiveTimeSubmenu(int? queried = null)
    {
        var effective = queried ?? _settings.InactiveTime;
        var parent = new ToolStripMenuItem("Inactive Time");
        (int min, string label)[] options =
        [
            (0, "Off"), (5, "5 min"), (10, "10 min"), (15, "15 min"),
            (30, "30 min"), (60, "60 min"), (90, "90 min"),
        ];
        foreach (var (min, label) in options)
        {
            var m = min;
            var item = new ToolStripMenuItem(label) { Checked = effective == m };
            item.Click += (_, _) =>
            {
                _settings.InactiveTime = m;
                _settings.Save();
                _ = HeadsetPoller.RunCommandAsync($"-i {m}");
                RebuildMenu();
            };
            parent.DropDownItems.Add(item);
        }
        return parent;
    }

    ToolStripMenuItem BuildEqPresetSubmenu(int? queried = null)
    {
        var effective = queried ?? _settings.EqPreset;
        var parent = new ToolStripMenuItem("EQ Preset");
        var names = _lastEqPresetNames.Count > 0
            ? _lastEqPresetNames
            : ["flat", "bass", "focus", "smiley"];
        for (int i = 0; i < names.Count; i++)
        {
            var idx = i;
            var item = new ToolStripMenuItem(Capitalize(names[i])) { Checked = effective == idx };
            item.Click += (_, _) =>
            {
                _settings.EqPreset = idx;
                _settings.Save();
                _ = HeadsetPoller.RunCommandAsync($"-p {idx}");
                RebuildMenu();
            };
            parent.DropDownItems.Add(item);
        }
        return parent;
    }

    ToolStripMenuItem BuildDeviceSubmenu(string title, string? selectedId, Action<string?> onSelect, bool includeNone = false)
    {
        var parent = new ToolStripMenuItem(title);

        if (includeNone)
        {
            var none = new ToolStripMenuItem("(restore previous default)") { Checked = selectedId == null };
            none.Click += (_, _) => onSelect(null);
            parent.DropDownItems.Add(none);
            if (_devices.Count > 0) parent.DropDownItems.Add(new ToolStripSeparator());
        }

        foreach (var d in _devices)
        {
            var item = new ToolStripMenuItem(d.Name) { Checked = d.Id == selectedId };
            var id = d.Id;
            item.Click += (_, _) => onSelect(id);
            parent.DropDownItems.Add(item);
        }

        if (!includeNone && _devices.Count == 0)
            parent.DropDownItems.Add(new ToolStripMenuItem("(no playback devices found)") { Enabled = false });

        return parent;
    }

    void ApplyStatus(HeadsetStatus status)
    {
        var oldIcon = _tray.Icon;
        _tray.Icon = IconRenderer.ForStatus(status);
        oldIcon?.Dispose();

        _tray.Text = status.State == HeadsetState.Online
            ? Truncate($"usb-headset-tray\n{status.DeviceName}\n" +
                       (status.IsCharging ? "Charging" : $"Battery: {status.BatteryLevel}%"), 63)
            : "usb-headset-tray\nHeadset offline";

        if (_settings.HeadsetDeviceId == null && status.State == HeadsetState.Online)
            TryAutoDetectHeadsetDevice(status.DeviceName);

        if (status.State != _lastState)
        {
            _lastCapabilities = status.Capabilities;
            _lastEqPresetNames = status.EqPresetNames;
            _headsetOnline = status.State == HeadsetState.Online;
            if (_headsetOnline)
            {
                _queriedSidetone = status.Sidetone;
                _queriedMicVolume = status.MicVolume;
                _queriedInactiveTime = status.InactiveTime;
                _queriedEqPreset = status.EqPreset;
            }
            else
            {
                _queriedSidetone = _queriedMicVolume = _queriedInactiveTime = _queriedEqPreset = null;
            }
            RebuildMenu();
        }

        HandleAutoSwitch(status);
        _lastState = status.State;
    }

    void TryAutoDetectHeadsetDevice(string productName)
    {
        var match = FindBestMatch(productName, _devices);
        if (match == null) return;

        _settings.HeadsetDeviceId = match.Id;
        _settings.Save();
        RebuildMenu();
        _tray.ShowBalloonTip(4000, "usb-headset-tray",
            $"Auto-detected headset output: {match.Name}\nChange it in the tray menu if wrong.",
            ToolTipIcon.Info);
    }

    static AudioDevice? FindBestMatch(string productName, List<AudioDevice> devices)
    {
        var needles = Tokenize(productName);
        if (needles.Length == 0) return null;

        AudioDevice? best = null;
        double bestScore = 0;

        foreach (var device in devices)
        {
            var haystack = new HashSet<string>(Tokenize(device.Name));
            double score = (double)needles.Count(t => haystack.Contains(t)) / needles.Length;
            if (score > bestScore) { bestScore = score; best = device; }
        }

        return bestScore >= 0.5 ? best : null;
    }

    static string[] Tokenize(string s) =>
        s.ToLowerInvariant()
         .Split([' ', '(', ')', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries)
         .Where(t => t.Length > 1)
         .ToArray();

    void HandleAutoSwitch(HeadsetStatus status)
    {
        if (!_settings.AutoSwitch || _settings.HeadsetDeviceId == null) return;

        bool wasOnline = _lastState == HeadsetState.Online;
        bool isOnline = status.State == HeadsetState.Online;

        if (!wasOnline && isOnline)
        {
            _previousDefaultDevice = AudioDeviceManager.GetDefaultPlaybackDeviceId();
            TrySetDefault(_settings.HeadsetDeviceId);
        }
        else if (wasOnline && !isOnline)
        {
            var target = _settings.FallbackDeviceId ?? _previousDefaultDevice;
            if (target != null) TrySetDefault(target);
        }
    }

    static void TrySetDefault(string deviceId)
    {
        try { AudioDeviceManager.SetDefaultPlaybackDevice(deviceId); }
        catch { }
    }

    static void ApplyStartWithWindows(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
        if (key == null) return;
        if (enable)
            key.SetValue("usb-headset-tray", $"\"{Application.ExecutablePath}\"");
        else
            key.DeleteValue("usb-headset-tray", throwOnMissingValue: false);
    }

    static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];

    static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    void ExitApp()
    {
        _timer.Stop();
        _tray.Visible = false;
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _tray.Icon?.Dispose();
            _tray.Dispose();
            _menu?.Dispose();
        }
        base.Dispose(disposing);
    }
}
