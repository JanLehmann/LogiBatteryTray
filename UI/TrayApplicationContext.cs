using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using LogiBatteryTray.Models;
using LogiBatteryTray.Rendering;
using LogiBatteryTray.Services;
using Microsoft.Win32;
using WinFormsTimer = System.Windows.Forms.Timer;

namespace LogiBatteryTray.UI;

/// <summary>
/// Manages tray icons, background polling, Windows event listeners, and the battery flyout.
/// </summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly SynchronizationContext _uiSyncContext;
    private readonly AppSettings _settings;
    private readonly BatteryFlyoutForm _flyout;
    private readonly ContextMenuStrip _contextMenu;
    private readonly MessageListenerWindow _messageWindow;
    private readonly List<NotifyIcon> _trayIcons = [];
    private readonly HashSet<string> _notifiedLowDevices = new(StringComparer.OrdinalIgnoreCase);

    private readonly WinFormsTimer _pollTimer;
    private readonly WinFormsTimer _debounceTimer;
    private readonly WinFormsTimer _resumeTimer;

    private IReadOnlyList<DeviceBatteryInfo> _devices = [];
    private bool _refreshInFlight;
    private bool _disposed;

    public TrayApplicationContext(uint showFlyoutMessage)
    {
        _uiSyncContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(_uiSyncContext);

        _settings = SettingsStore.Load();

        _contextMenu = new ContextMenuStrip();
        _contextMenu.Opening += OnContextMenuOpening;

        _flyout = new BatteryFlyoutForm();
        _flyout.RefreshRequested += (_, _) => _ = RefreshDevicesAsync();
        _flyout.IconModeSelected += (_, mode) => SetIconMode(mode);
        _flyout.AutostartChanged += (_, enabled) => SetAutostart(enabled);
        _flyout.LowBatteryAlertChanged += (_, enabled) => SetLowBatteryAlert(enabled);
        _flyout.OpenTaskbarSettingsRequested += (_, _) => OpenTaskbarSettings();
        _flyout.ExitRequested += (_, _) => ExitThread();

        _pollTimer = new WinFormsTimer
        {
            Interval = Math.Clamp(_settings.PollIntervalSeconds, 10, 3600) * 1000
        };
        _pollTimer.Tick += (_, _) => _ = RefreshDevicesAsync();

        _debounceTimer = new WinFormsTimer { Interval = 1500 };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            _ = RefreshDevicesAsync();
        };

        _resumeTimer = new WinFormsTimer { Interval = 2500 };
        _resumeTimer.Tick += (_, _) =>
        {
            _resumeTimer.Stop();
            _ = RefreshDevicesAsync();
        };

        _messageWindow = new MessageListenerWindow(
            showFlyoutMessage,
            onDeviceChange: () => RestartOneShotTimer(_debounceTimer),
            onThemeChange: RebuildTrayIconsOnly,
            onShowFlyout: () =>
            {
                _ = RefreshDevicesAsync();
                _flyout.ShowNearTray();
            });

        SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        // Read immediately so the first tray frame shows live values instead of placeholders.
        IReadOnlyList<DeviceBatteryInfo> initialDevices = BluetoothBatteryReader.ReadLogitechDevices();
        ApplyDevicesUpdate(initialDevices, checkAlerts: false);

        _pollTimer.Start();
    }

    protected override void ExitThreadCore()
    {
        Dispose(true);
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _disposed = true;

            SystemEvents.UserPreferenceChanged -= OnSystemPreferenceChanged;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;

            _pollTimer.Stop();
            _pollTimer.Dispose();
            _debounceTimer.Stop();
            _debounceTimer.Dispose();
            _resumeTimer.Stop();
            _resumeTimer.Dispose();

            ClearAllTrayIcons();
            _contextMenu.Dispose();
            _flyout.Dispose();
            _messageWindow.DestroyHandle();
        }

        base.Dispose(disposing);
    }

    private async Task RefreshDevicesAsync()
    {
        if (_disposed || _refreshInFlight)
        {
            return;
        }

        _refreshInFlight = true;
        try
        {
            IReadOnlyList<DeviceBatteryInfo> fresh = await Task.Run(BluetoothBatteryReader.ReadLogitechDevices);
            if (!_disposed)
            {
                ApplyDevicesUpdate(fresh, checkAlerts: true);
            }
        }
        finally
        {
            _refreshInFlight = false;
        }
    }

    private void ApplyDevicesUpdate(IReadOnlyList<DeviceBatteryInfo> devices, bool checkAlerts)
    {
        _devices = devices;
        RebuildTrayIconsOnly();
        _flyout.UpdateState(_devices, _settings, AutostartManager.IsEnabled());

        if (checkAlerts)
        {
            CheckLowBatteryAlerts(devices);
        }
    }

    private void RebuildTrayIconsOnly()
    {
        if (_disposed)
        {
            return;
        }

        int iconSize = ThemeHelper.GetTrayIconSize();
        bool isLightTaskbar = ThemeHelper.IsSystemLightTheme();

        if (_settings.IconMode == IconDisplayMode.Combined || _devices.Count == 0)
        {
            EnsureTrayIconCount(1);
            NotifyIcon tray = _trayIcons[0];
            Icon newIcon = _devices.Count == 0
                ? TrayIconRenderer.RenderEmptyIcon(iconSize, isLightTaskbar)
                : TrayIconRenderer.RenderCombinedIcon(_devices, iconSize, isLightTaskbar);
            ReplaceIcon(tray, newIcon);
            tray.Text = BuildCombinedTooltip(_devices);
            tray.Visible = true;
        }
        else
        {
            EnsureTrayIconCount(_devices.Count);
            for (int i = 0; i < _devices.Count; i++)
            {
                DeviceBatteryInfo dev = _devices[i];
                NotifyIcon tray = _trayIcons[i];
                Icon newIcon = _settings.IconMode == IconDisplayMode.NumbersOnly
                    ? TrayIconRenderer.RenderDeviceNumbersOnlyIcon(dev, iconSize, isLightTaskbar)
                    : TrayIconRenderer.RenderDeviceIcon(dev, iconSize, isLightTaskbar);
                ReplaceIcon(tray, newIcon);
                tray.Text = BuildDeviceTooltip(dev);
                tray.Visible = true;
            }
        }
    }

    private void EnsureTrayIconCount(int desiredCount)
    {
        while (_trayIcons.Count > desiredCount)
        {
            int lastIdx = _trayIcons.Count - 1;
            DisposeTrayIcon(_trayIcons[lastIdx]);
            _trayIcons.RemoveAt(lastIdx);
        }

        while (_trayIcons.Count < desiredCount)
        {
            var tray = new NotifyIcon
            {
                ContextMenuStrip = _contextMenu,
                Visible = false
            };
            tray.MouseClick += OnTrayIconMouseClick;
            _trayIcons.Add(tray);
        }
    }

    private void ClearAllTrayIcons()
    {
        foreach (NotifyIcon tray in _trayIcons)
        {
            DisposeTrayIcon(tray);
        }

        _trayIcons.Clear();
    }

    private static void DisposeTrayIcon(NotifyIcon tray)
    {
        tray.Visible = false;
        Icon? old = tray.Icon;
        tray.Icon = null;
        old?.Dispose();
        tray.Dispose();
    }

    private static void ReplaceIcon(NotifyIcon tray, Icon newIcon)
    {
        Icon? old = tray.Icon;
        tray.Icon = newIcon;
        old?.Dispose();
    }

    private void OnTrayIconMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        if (_flyout.Visible)
        {
            _flyout.Hide();
            return;
        }

        // Prevent immediate re-open when clicking the tray icon caused Deactivate just milliseconds earlier.
        if ((DateTime.UtcNow - _flyout.LastDeactivatedUtc).TotalMilliseconds < 250)
        {
            return;
        }

        _ = RefreshDevicesAsync();
        _flyout.ShowNearTray();
    }

    private void OnContextMenuOpening(object? sender, CancelEventArgs e)
    {
        _contextMenu.Items.Clear();

        if (_devices.Count == 0)
        {
            _contextMenu.Items.Add(new ToolStripMenuItem("Keine Logitech-Geräte gefunden") { Enabled = false });
        }
        else
        {
            foreach (DeviceBatteryInfo dev in _devices)
            {
                string label;
                if (!dev.IsConnected || dev.BatteryPercent is not int p)
                {
                    label = $"{dev.Name} — Getrennt";
                }
                else if (dev.IsCharging || dev.IsChargingComplete ||
                         dev.ChargingStatus is ChargingStatus.InvalidBattery
                             or ChargingStatus.ThermalError
                             or ChargingStatus.OtherError)
                {
                    string status = ChargingStatusText.ToShortStatus(dev.ChargingStatus, dev.IsConnected);
                    label = $"{dev.Name} — {p} % ({status})";
                }
                else
                {
                    label = $"{dev.Name} — {p} %";
                }

                _contextMenu.Items.Add(new ToolStripMenuItem(label) { Enabled = false });
            }
        }

        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add("Jetzt aktualisieren", null, (_, _) => _ = RefreshDevicesAsync());

        var modeMenu = new ToolStripMenuItem("Darstellung");
        var splitItem = new ToolStripMenuItem("Zwei Symbole (mit Icon)")
        {
            Checked = _settings.IconMode == IconDisplayMode.Split
        };
        splitItem.Click += (_, _) => SetIconMode(IconDisplayMode.Split);

        var numbersItem = new ToolStripMenuItem("Zwei Symbole (nur Prozent)")
        {
            Checked = _settings.IconMode == IconDisplayMode.NumbersOnly
        };
        numbersItem.Click += (_, _) => SetIconMode(IconDisplayMode.NumbersOnly);

        var combinedItem = new ToolStripMenuItem("Ein Symbol (kombiniert)")
        {
            Checked = _settings.IconMode == IconDisplayMode.Combined
        };
        combinedItem.Click += (_, _) => SetIconMode(IconDisplayMode.Combined);

        modeMenu.DropDownItems.Add(splitItem);
        modeMenu.DropDownItems.Add(numbersItem);
        modeMenu.DropDownItems.Add(combinedItem);
        _contextMenu.Items.Add(modeMenu);

        var autostartItem = new ToolStripMenuItem("Mit Windows starten")
        {
            Checked = AutostartManager.IsEnabled()
        };
        autostartItem.Click += (_, _) => SetAutostart(!AutostartManager.IsEnabled());
        _contextMenu.Items.Add(autostartItem);

        var alertItem = new ToolStripMenuItem($"Akku-Warnung (< {_settings.LowBatteryThreshold} %)")
        {
            Checked = _settings.LowBatteryAlertEnabled
        };
        alertItem.Click += (_, _) => SetLowBatteryAlert(!_settings.LowBatteryAlertEnabled);
        _contextMenu.Items.Add(alertItem);

        _contextMenu.Items.Add("Taskleisten-Einstellungen öffnen", null, (_, _) => OpenTaskbarSettings());
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add("Beenden", null, (_, _) => ExitThread());
    }

    private void SetIconMode(IconDisplayMode mode)
    {
        if (_settings.IconMode == mode)
        {
            return;
        }

        _settings.IconMode = mode;
        SettingsStore.Save(_settings);
        RebuildTrayIconsOnly();
        _flyout.UpdateState(_devices, _settings, AutostartManager.IsEnabled());
    }

    private void SetAutostart(bool enabled)
    {
        AutostartManager.SetEnabled(enabled);
        _flyout.UpdateState(_devices, _settings, AutostartManager.IsEnabled());
    }

    private void SetLowBatteryAlert(bool enabled)
    {
        _settings.LowBatteryAlertEnabled = enabled;
        SettingsStore.Save(_settings);
        _flyout.UpdateState(_devices, _settings, AutostartManager.IsEnabled());
    }

    private static void OpenTaskbarSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:taskbar") { UseShellExecute = true });
        }
        catch
        {
            // Ignore shell launch errors.
        }
    }

    private void CheckLowBatteryAlerts(IReadOnlyList<DeviceBatteryInfo> devices)
    {
        if (!_settings.LowBatteryAlertEnabled || _trayIcons.Count == 0)
        {
            return;
        }

        int threshold = _settings.LowBatteryThreshold;
        foreach (DeviceBatteryInfo dev in devices)
        {
            if (!dev.IsConnected || dev.BatteryPercent is not int pct)
            {
                continue;
            }

            if (dev.IsCharging || dev.IsChargingComplete)
            {
                _notifiedLowDevices.Remove(dev.InstanceId);
                continue;
            }

            if (pct >= threshold + 5)
            {
                _notifiedLowDevices.Remove(dev.InstanceId);
                continue;
            }

            if (pct <= threshold && _notifiedLowDevices.Add(dev.InstanceId))
            {
                _trayIcons[0].ShowBalloonTip(
                    5000,
                    $"{dev.Name} — Akku schwach",
                    $"Akkustand bei {pct} %. Bitte bald aufladen.",
                    ToolTipIcon.Warning);
            }
        }
    }

    private void OnSystemPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        BeginMarshalToUi(() =>
        {
            RebuildTrayIconsOnly();
            _flyout.UpdateState(_devices, _settings, AutostartManager.IsEnabled());
        });
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        BeginMarshalToUi(RebuildTrayIconsOnly);
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            BeginMarshalToUi(() => RestartOneShotTimer(_resumeTimer));
        }
    }

    private void BeginMarshalToUi(Action action)
    {
        if (_disposed)
        {
            return;
        }

        _uiSyncContext.Post(_ =>
        {
            if (!_disposed)
            {
                action();
            }
        }, null);
    }

    private static void RestartOneShotTimer(WinFormsTimer timer)
    {
        timer.Stop();
        timer.Start();
    }

    private static string BuildDeviceTooltip(DeviceBatteryInfo dev)
    {
        string status = ChargingStatusText.ToShortStatus(dev.ChargingStatus, dev.IsConnected);
        string state = dev.IsConnected && dev.BatteryPercent is int p
            ? $"{p} % ({status})"
            : "Getrennt";
        string time = dev.LastUpdated is DateTime dt ? $"\nStand: {dt:HH:mm}" : string.Empty;
        return TruncateTooltip($"{dev.Name}: {state}{time}");
    }

    private static string BuildCombinedTooltip(IReadOnlyList<DeviceBatteryInfo> devices)
    {
        if (devices.Count == 0)
        {
            return "Logitech Akku: Keine Geräte gefunden";
        }

        var lines = devices.Select(d =>
        {
            string shortName = ShortenForCombinedTooltip(d.Name);
            if (!d.IsConnected || d.BatteryPercent is not int p)
            {
                return $"{shortName}: --";
            }

            string suffix = d.IsCharging
                ? " (Lädt)"
                : d.IsChargingComplete
                    ? " (Voll)"
                    : string.Empty;
            return $"{shortName}: {p} %{suffix}";
        });

        return TruncateTooltip(string.Join("\n", lines));
    }

    private static string ShortenForCombinedTooltip(string name)
    {
        return name
            .Replace(" Wireless Gaming Headset", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" Gaming Headset", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    private static string TruncateTooltip(string text)
    {
        // WinForms NotifyIcon.Text throws ArgumentOutOfRangeException for length >= 64.
        return text.Length <= 63 ? text : string.Concat(text.AsSpan(0, 62), "…");
    }

    private sealed class MessageListenerWindow : NativeWindow
    {
        private readonly uint _showFlyoutMessage;
        private readonly Action _onDeviceChange;
        private readonly Action _onThemeChange;
        private readonly Action _onShowFlyout;

        public MessageListenerWindow(
            uint showFlyoutMessage,
            Action onDeviceChange,
            Action onThemeChange,
            Action onShowFlyout)
        {
            _showFlyoutMessage = showFlyoutMessage;
            _onDeviceChange = onDeviceChange;
            _onThemeChange = onThemeChange;
            _onShowFlyout = onShowFlyout;
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_DEVICECHANGE)
            {
                _onDeviceChange();
            }
            else if (m.Msg == NativeMethods.WM_SETTINGCHANGE)
            {
                _onThemeChange();
            }
            else if (_showFlyoutMessage != 0 && m.Msg == (int)_showFlyoutMessage)
            {
                _onShowFlyout();
            }

            base.WndProc(ref m);
        }
    }
}
