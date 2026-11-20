namespace LogiBatteryTray.Models;

/// <summary>
/// Controls whether devices appear as separate tray icons, a cycling single icon, or a combined icon.
/// </summary>
public enum IconDisplayMode
{
    Split,
    NumbersOnly,
    Cycling,
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
    public int CycleIntervalMs { get; set; } = 3500;
    public HashSet<string> HiddenDeviceIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static string GetStableDeviceKey(DeviceBatteryInfo device)
    {
        if (!string.IsNullOrWhiteSpace(device.BluetoothAddress))
        {
            string cleaned = device.BluetoothAddress
                .Replace(":", string.Empty, StringComparison.Ordinal)
                .Replace("-", string.Empty, StringComparison.Ordinal)
                .Trim()
                .ToLowerInvariant();
            if (!string.IsNullOrEmpty(cleaned))
            {
                return $"bt:{cleaned}";
            }
        }

        return $"name:{device.Name.Trim().ToLowerInvariant()}";
    }

    public bool IsDeviceVisible(DeviceBatteryInfo device)
    {
        return !HiddenDeviceIds.Contains(GetStableDeviceKey(device));
    }

    public void SetDeviceVisible(DeviceBatteryInfo device, bool visible)
    {
        string key = GetStableDeviceKey(device);
        if (visible)
        {
            HiddenDeviceIds.Remove(key);
        }
        else
        {
            HiddenDeviceIds.Add(key);
        }
    }
}
