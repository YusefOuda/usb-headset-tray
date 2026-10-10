using Microsoft.Win32;

namespace UsbHeadsetTray;

class TrayApp : ApplicationContext
{
    readonly NotifyIcon _tray;
    readonly System.Windows.Forms.Timer _timer;
    readonly System.Windows.Forms.Timer _updateTimer;
    readonly AudioDeviceWatcher _watcher;
    readonly BatteryEstimator _estimator;
    readonly BatteryNotifier _notifier = new();
    AppSettings _settings;
    // Active devices, refreshed with the menu
    List<AudioDevice> _outputDevices = [];
    List<AudioDevice> _inputDevices = [];
    List<string> _lastCapabilities = [];
    List<string> _lastEqPresetNames = [];
    bool _headsetOnline = false;
    // Values read from the headset JSON when the headset reports them; null if not queryable.
    // Never persisted — cleared when the headset goes offline.
    int? _queriedSidetone;
    int? _queriedMicVolume;
    int? _queriedInactiveTime;
    int? _queriedEqPreset;
    // Output and input switching run the same logic; these hold the per-direction state.
    // Default before the headset came online, restored when no fallback is connected.
    readonly Dictionary<EDataFlow, string?> _previousDefault = new();
    // Last default seen via notifications; used to tell disconnect fallbacks from manual changes.
    readonly Dictionary<EDataFlow, string?> _observedDefault = new();
    HeadsetState _lastState = HeadsetState.Unknown;
    HeadsetStatus? _lastStatus;
    int _batteryLevel;
    bool _isCharging;
    TimeSpan? _remaining;
    // Set while headsetcontrol is missing or failing; replaces the battery status text
    string? _pollProblem;
    int _consecutivePollFailures;
    // Tag of a newer release, if one was found
    string? _availableUpdate;
    string? _notifiedUpdate;
    // What clicking the current balloon does; balloons are shared by all notifications
    Action? _balloonClick;
    ContextMenuStrip? _menu;
    bool _menuDirty;
    ToolStripMenuItem? _statusItem;

    // headsetcontrol occasionally fails transiently; only report "error" after this many failures in a row
    const int PollFailuresBeforeError = 3;
    static readonly EDataFlow[] Flows = [EDataFlow.eRender, EDataFlow.eCapture];
    static readonly TimeSpan FirstUpdateCheckDelay = TimeSpan.FromSeconds(30);
    static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromDays(1);

    public TrayApp()
    {
        _settings = AppSettings.Load();
        _estimator = new BatteryEstimator(_settings.BatteryDrainPerHour);

        _tray = new NotifyIcon
        {
            Icon = IconRenderer.Disconnected(),
            Text = "usb-headset-tray",
            Visible = true,
        };
        _tray.BalloonTipClicked += (_, _) => _balloonClick?.Invoke();

        RebuildMenu();

        // Creating the ContextMenuStrip above installs the WinForms SynchronizationContext
        foreach (var flow in Flows)
            _observedDefault[flow] = AudioDeviceManager.GetDefaultDeviceId(flow);
        _watcher = new AudioDeviceWatcher(SynchronizationContext.Current!);
        _watcher.DeviceStateChanged += OnDeviceStateChanged;
        _watcher.DefaultDeviceChanged += OnDefaultDeviceChanged;

        _timer = new System.Windows.Forms.Timer { Interval = 5000 };
        _timer.Tick += Timer_Tick;
        _timer.Start();

        // Short first delay so startup isn't slowed, then daily
        _updateTimer = new System.Windows.Forms.Timer { Interval = (int)FirstUpdateCheckDelay.TotalMilliseconds };
        _updateTimer.Tick += UpdateTimer_Tick;
        _updateTimer.Start();
    }

    async void Timer_Tick(object? sender, EventArgs e)
    {
        var result = await HeadsetPoller.PollAsync();
        switch (result.Failure)
        {
            case PollFailure.NotFound:
                ShowPollProblem("HeadsetControl not found",
                    "headsetcontrol.exe wasn't found next to UsbHeadsetTray.exe or on PATH. Reinstalling usb-headset-tray restores it.");
                break;
            case PollFailure.Failed:
                if (++_consecutivePollFailures >= PollFailuresBeforeError)
                    ShowPollProblem("HeadsetControl error", null);
                break;
            default:
                _consecutivePollFailures = 0;
                _pollProblem = null;
                ApplyStatus(result.Status!);
                break;
        }
    }

    // Shows a headsetcontrol problem in place of battery status. Auto-switch state is left alone,
    // since we can't tell whether the headset is on.
    void ShowPollProblem(string message, string? balloonDetail)
    {
        if (_pollProblem == message) return;
        _pollProblem = message;
        Log.Error($"{message}{(balloonDetail != null ? $": {balloonDetail}" : "")}");

        var oldIcon = _tray.Icon;
        _tray.Icon = IconRenderer.Disconnected();
        oldIcon?.Dispose();
        _tray.Text = $"usb-headset-tray\n{message}";
        if (_statusItem != null) _statusItem.Text = message;

        if (balloonDetail != null)
            ShowBalloon(message, balloonDetail, ToolTipIcon.Error);
    }

    async void UpdateTimer_Tick(object? sender, EventArgs e)
    {
        _updateTimer.Interval = (int)UpdateCheckInterval.TotalMilliseconds;
        await CheckForUpdateAsync();
    }

    async Task CheckForUpdateAsync()
    {
        if (!_settings.CheckForUpdates) return;
        var tag = await UpdateChecker.CheckAsync();
        if (tag == null || tag == _availableUpdate) return;

        _availableUpdate = tag;
        RebuildMenuWhenClosed();
        if (_notifiedUpdate != tag)
        {
            _notifiedUpdate = tag;
            ShowBalloon("Update available", $"usb-headset-tray {tag} is available. Click to open the download page.",
                ToolTipIcon.Info, OpenReleasesPage);
        }
    }

    static void OpenReleasesPage()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(UpdateChecker.ReleasesPage) { UseShellExecute = true }); }
        catch { }
    }

    void ShowBalloon(string title, string text, ToolTipIcon icon, Action? onClick = null)
    {
        _balloonClick = onClick;
        _tray.ShowBalloonTip(5000, title, text, icon);
    }

    void RebuildMenu()
    {
        _outputDevices = AudioDeviceManager.GetDevices(EDataFlow.eRender);
        _inputDevices = AudioDeviceManager.GetDevices(EDataFlow.eCapture);
        _menu = BuildMenu();
        _tray.ContextMenuStrip = _menu;
    }

    // Replacing the menu while it is open would close it under the user, so defer until it closes.
    void RebuildMenuWhenClosed()
    {
        if (_menu?.Visible == true) _menuDirty = true;
        else RebuildMenu();
    }

    string GetStatusText() =>
        _pollProblem ?? (_headsetOnline
            ? (_isCharging ? "Battery: Charging" : BatteryText())
            : "Headset offline");

    string BatteryText() =>
        $"Battery: {_batteryLevel}%" + (_remaining is TimeSpan t ? $" ({BatteryEstimator.Format(t)})" : "");

    ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Closed += (_, _) =>
        {
            if (!_menuDirty) return;
            _menuDirty = false;
            RebuildMenu();
        };

        _statusItem = new ToolStripMenuItem(GetStatusText()) { Enabled = false };
        menu.Items.Add(_statusItem);
        if (_availableUpdate != null)
        {
            var update = new ToolStripMenuItem($"Update available ({_availableUpdate})...");
            update.Click += (_, _) => OpenReleasesPage();
            menu.Items.Add(update);
        }
        menu.Items.Add(new ToolStripSeparator());

        // Top level stays short: everyday items here, set-once options under Settings
        foreach (var flow in Flows)
        {
            var direction = new ToolStripMenuItem(flow == EDataFlow.eCapture ? "Input" : "Output");
            var autoSwitch = BuildAutoSwitchItem(flow);
            var headset = BuildHeadsetDeviceSubmenu(flow);
            var fallbacks = BuildFallbackSubmenu(flow);
            // Pickers have no effect while auto-switch is off, so grey them out (their values stay saved)
            void SyncEnabled() => headset.Enabled = fallbacks.Enabled = autoSwitch.Checked;
            SyncEnabled();
            autoSwitch.CheckedChanged += (_, _) => SyncEnabled();

            direction.DropDownItems.Add(autoSwitch);
            direction.DropDownItems.Add(new ToolStripSeparator());
            direction.DropDownItems.Add(headset);
            direction.DropDownItems.Add(fallbacks);
            menu.Items.Add(direction);
        }

        if (_headsetOnline && _lastCapabilities.Count > 0)
        {
            var controls = BuildHeadsetControlsSubmenu();
            if (controls.DropDownItems.Count > 0)
                menu.Items.Add(controls);
        }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(BuildSettingsSubmenu());

        var exit = new ToolStripMenuItem("Exit");
        exit.Click += (_, _) => ExitApp();
        menu.Items.Add(exit);

        return menu;
    }

    ToolStripMenuItem BuildSettingsSubmenu()
    {
        var parent = new ToolStripMenuItem("Settings");

        var icon = new ToolStripMenuItem("Tray icon");
        foreach (var (percentage, label) in new[] { (false, "Battery bar"), (true, "Percentage") })
        {
            var p = percentage;
            var item = new ToolStripMenuItem(label) { Checked = _settings.IconShowsPercentage == p };
            item.Click += (_, _) =>
            {
                _settings.IconShowsPercentage = p;
                _settings.Save();
                RefreshIcon();
                RebuildMenu();
            };
            icon.DropDownItems.Add(item);
        }
        parent.DropDownItems.Add(icon);

        var low = new ToolStripMenuItem("Low battery alert");
        foreach (var threshold in new[] { 0, 10, 15, 20, 25 })
        {
            var t = threshold;
            var item = new ToolStripMenuItem(t == 0 ? "Off" : $"At {t}%") { Checked = _settings.LowBatteryThreshold == t };
            item.Click += (_, _) => { _settings.LowBatteryThreshold = t; _settings.Save(); RebuildMenu(); };
            low.DropDownItems.Add(item);
        }
        parent.DropDownItems.Add(low);

        var charged = new ToolStripMenuItem("Charge complete alert") { CheckOnClick = true, Checked = _settings.NotifyChargeComplete };
        charged.CheckedChanged += (_, _) => { _settings.NotifyChargeComplete = charged.Checked; _settings.Save(); };
        parent.DropDownItems.Add(charged);

        parent.DropDownItems.Add(new ToolStripSeparator());

        var checkUpdates = new ToolStripMenuItem("Check for updates") { CheckOnClick = true, Checked = _settings.CheckForUpdates };
        checkUpdates.CheckedChanged += (_, _) =>
        {
            _settings.CheckForUpdates = checkUpdates.Checked;
            _settings.Save();
            if (checkUpdates.Checked) _ = CheckForUpdateAsync();
            else { _availableUpdate = null; RebuildMenuWhenClosed(); }
        };
        parent.DropDownItems.Add(checkUpdates);

        var startWithWindows = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true, Checked = _settings.StartWithWindows };
        startWithWindows.CheckedChanged += (_, _) =>
        {
            _settings.StartWithWindows = startWithWindows.Checked;
            _settings.Save();
            ApplyStartWithWindows(startWithWindows.Checked);
        };
        parent.DropDownItems.Add(startWithWindows);

        parent.DropDownItems.Add(new ToolStripSeparator());
        var openLog = new ToolStripMenuItem("Open log folder");
        openLog.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Log.Folder) { UseShellExecute = true }); }
            catch { }
        };
        parent.DropDownItems.Add(openLog);

        return parent;
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

    ToolStripMenuItem BuildAutoSwitchItem(EDataFlow flow)
    {
        bool input = flow == EDataFlow.eCapture;
        bool on = _settings.IsAutoSwitchOn(flow);
        // Switching does nothing until a headset device is picked; say so rather than silently doing nothing
        var label = "Auto-switch";
        if (on && _settings.HeadsetDevice(flow) == null)
            label += input ? " (pick a headset mic)" : " (pick a headset device)";

        var item = new ToolStripMenuItem(label) { CheckOnClick = true, Checked = on };
        item.CheckedChanged += (_, _) =>
        {
            _settings.SetAutoSwitch(flow, item.Checked);
            _settings.Save();
            var headset = _settings.HeadsetDevice(flow);
            if (item.Checked && _headsetOnline && headset != null)
            {
                _previousDefault[flow] = AudioDeviceManager.GetDefaultDeviceId(flow);
                TrySetDefault(headset);
            }
            RebuildMenuWhenClosed();
        };
        return item;
    }

    ToolStripMenuItem BuildHeadsetDeviceSubmenu(EDataFlow flow)
    {
        bool input = flow == EDataFlow.eCapture;
        var parent = new ToolStripMenuItem(input ? "Headset microphone" : "Headset device");
        var selectedId = _settings.HeadsetDevice(flow);
        var devices = input ? _inputDevices : _outputDevices;

        void Select(string? id)
        {
            _settings.SetHeadsetDevice(flow, id);
            _settings.Save();
            RebuildMenu();
        }

        // Output is auto-detected, so only input offers "none" (which turns input switching into a no-op)
        if (input)
        {
            var none = new ToolStripMenuItem("(none)") { Checked = selectedId == null };
            none.Click += (_, _) => Select(null);
            parent.DropDownItems.Add(none);
            if (devices.Count > 0) parent.DropDownItems.Add(new ToolStripSeparator());
        }

        foreach (var d in devices)
        {
            var item = new ToolStripMenuItem(d.Name) { Checked = d.Id == selectedId };
            var id = d.Id;
            item.Click += (_, _) => Select(id);
            parent.DropDownItems.Add(item);
        }

        if (devices.Count == 0)
            parent.DropDownItems.Add(new ToolStripMenuItem(input ? "(no recording devices found)" : "(no playback devices found)") { Enabled = false });

        return parent;
    }

    ToolStripMenuItem BuildFallbackSubmenu(EDataFlow flow)
    {
        var parent = new ToolStripMenuItem("Fallback devices");
        var fallbacks = _settings.Fallbacks(flow);
        var headsetId = _settings.HeadsetDevice(flow);
        // Include disconnected devices so e.g. Bluetooth headphones can be ranked while they are off
        var all = AudioDeviceManager.GetDevices(flow, DeviceState.All);

        void Update(Action<List<string>> change)
        {
            change(fallbacks);
            _settings.Save();
            RebuildMenu();
        }

        if (fallbacks.Count == 0)
            parent.DropDownItems.Add(new ToolStripMenuItem("(none: restore previous default)") { Enabled = false });

        for (int i = 0; i < fallbacks.Count; i++)
        {
            var idx = i;
            var id = fallbacks[i];
            var device = all.FirstOrDefault(d => d.Id == id);
            var name = device?.Name ?? "Unknown device";
            var suffix = device?.State == DeviceState.Active ? "" : " (disconnected)";
            var item = new ToolStripMenuItem($"{i + 1}. {name}{suffix}");

            var up = new ToolStripMenuItem("Move up") { Enabled = idx > 0 };
            up.Click += (_, _) => Update(l => (l[idx - 1], l[idx]) = (l[idx], l[idx - 1]));
            var down = new ToolStripMenuItem("Move down") { Enabled = idx < fallbacks.Count - 1 };
            down.Click += (_, _) => Update(l => (l[idx + 1], l[idx]) = (l[idx], l[idx + 1]));
            var remove = new ToolStripMenuItem("Remove");
            remove.Click += (_, _) => Update(l => l.RemoveAt(idx));
            item.DropDownItems.AddRange([up, down, remove]);

            parent.DropDownItems.Add(item);
        }

        parent.DropDownItems.Add(new ToolStripSeparator());

        var add = new ToolStripMenuItem("Add device");
        foreach (var d in all)
        {
            if (d.State is not (DeviceState.Active or DeviceState.Unplugged)) continue;
            if (d.Id == headsetId || fallbacks.Contains(d.Id)) continue;
            var id = d.Id;
            var item = new ToolStripMenuItem(d.State == DeviceState.Active ? d.Name : $"{d.Name} (disconnected)");
            item.Click += (_, _) => Update(l => l.Add(id));
            add.DropDownItems.Add(item);
        }
        if (add.DropDownItems.Count == 0)
            add.DropDownItems.Add(new ToolStripMenuItem("(no other devices)") { Enabled = false });
        parent.DropDownItems.Add(add);

        return parent;
    }

    void ApplyStatus(HeadsetStatus status)
    {
        _batteryLevel = status.BatteryLevel;
        _isCharging = status.IsCharging;
        _remaining = _estimator.Update(status, DateTime.UtcNow);
        if (_estimator.LearnedRate != _settings.BatteryDrainPerHour)
        {
            _settings.BatteryDrainPerHour = _estimator.LearnedRate;
            _settings.Save();
        }

        if (_notifier.Update(status, _settings.LowBatteryThreshold, _settings.NotifyChargeComplete, _remaining) is { } note)
            ShowBalloon(note.Title, note.Text, note.IsWarning ? ToolTipIcon.Warning : ToolTipIcon.Info);

        _lastStatus = status;
        RefreshIcon();

        // Battery line before the device name so a long name is what gets truncated
        _tray.Text = status.State == HeadsetState.Online
            ? Truncate($"usb-headset-tray\n{(status.IsCharging ? "Charging" : BatteryText())}\n{status.DeviceName}", 63)
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

        if (_statusItem != null)
            _statusItem.Text = GetStatusText();

        HandleAutoSwitch(status);
        _lastState = status.State;
    }

    void RefreshIcon()
    {
        // A poll problem keeps the disconnected icon until polling recovers
        if (_lastStatus == null || _pollProblem != null) return;
        var oldIcon = _tray.Icon;
        _tray.Icon = IconRenderer.ForStatus(_lastStatus, _settings.IconShowsPercentage);
        oldIcon?.Dispose();
    }

    void TryAutoDetectHeadsetDevice(string productName)
    {
        var match = FindBestMatch(productName, _outputDevices);
        if (match == null) return;

        _settings.HeadsetDeviceId = match.Id;
        _settings.Save();
        RebuildMenu();
        ShowBalloon("usb-headset-tray",
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
        bool wasOnline = _lastState == HeadsetState.Online;
        bool isOnline = status.State == HeadsetState.Online;
        if (wasOnline == isOnline) return;
        Log.Info($"Headset {(isOnline ? "online" : "offline")}");

        foreach (var flow in Flows)
        {
            var headset = _settings.HeadsetDevice(flow);
            if (!_settings.IsAutoSwitchOn(flow) || headset == null) continue;

            if (isOnline)
            {
                _previousDefault[flow] = AudioDeviceManager.GetDefaultDeviceId(flow);
                TrySetDefault(headset);
            }
            else
            {
                var target = FallbackPolicy.BestAvailable(_settings.Fallbacks(flow), headset, AudioDeviceManager.GetDeviceState)
                    ?? _previousDefault.GetValueOrDefault(flow);
                if (target != null) TrySetDefault(target);
            }
        }
    }

    // Fallback rules apply only while auto-switch is on for that direction and the headset isn't holding the default
    bool FallbackRulesActive(EDataFlow flow) =>
        _settings.IsAutoSwitchOn(flow) && !(_headsetOnline && _settings.HeadsetDevice(flow) != null);

    void OnDeviceStateChanged(string deviceId, DeviceState newState)
    {
        Log.Info($"Device {deviceId} is now {newState}");
        RebuildMenuWhenClosed();

        // Device ids are specific to one direction, so at most one direction's list can match
        foreach (var flow in Flows)
        {
            if (!FallbackRulesActive(flow) || !_settings.Fallbacks(flow).Contains(deviceId)) continue;

            // A higher-priority fallback just connected (e.g. Bluetooth headphones turned on)
            if (FallbackPolicy.ShouldSwitchToConnected(_settings.Fallbacks(flow), _settings.HeadsetDevice(flow),
                    deviceId, newState, AudioDeviceManager.GetDefaultDeviceId(flow)))
                TrySetDefault(deviceId);
        }
    }

    void OnDefaultDeviceChanged(EDataFlow flow, string? newDefault)
    {
        var oldDefault = _observedDefault.GetValueOrDefault(flow);
        _observedDefault[flow] = newDefault;
        Log.Info($"Default {flow} device is now {newDefault ?? "none"}");

        if (!FallbackRulesActive(flow)) return;

        var replacement = FallbackPolicy.ReplacementForDefaultChange(_settings.Fallbacks(flow), _settings.HeadsetDevice(flow),
            oldDefault, newDefault, AudioDeviceManager.GetDeviceState);
        if (replacement != null) TrySetDefault(replacement);
    }

    static void TrySetDefault(string deviceId)
    {
        Log.Info($"Setting default device: {deviceId}");
        try { AudioDeviceManager.SetDefaultDevice(deviceId); }
        catch (Exception e) { Log.Error($"Setting default device {deviceId} failed", e); }
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

    public void ExitApp()
    {
        _timer.Stop();
        _updateTimer.Stop();
        _tray.Visible = false;
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _updateTimer.Dispose();
            _watcher.Dispose();
            _tray.Icon?.Dispose();
            _tray.Dispose();
            _menu?.Dispose();
        }
        base.Dispose(disposing);
    }
}
