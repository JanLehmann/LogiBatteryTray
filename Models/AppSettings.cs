namespace LogiBatteryTray.Models;

/// <summary>
/// Controls whether devices appear as separate tray icons or one combined icon.
/// </summary>
public enum IconDisplayMode
{
    Split,
    NumbersOnly,
    Combined
}

/// <summary>
/// Persisted user preferences for the tray application.
/// </summary>
public sealed class AppSettings
{
    public IconDisplayMode IconMode { get; set; } = IconDisplayMode.Split;
    public bool LowBatteryAlertEnabled { get; set; } = true;
    public int LowBatteryThreshold { get; set; } = 20;
    public int PollIntervalSeconds { get; set; } = 30;
}
