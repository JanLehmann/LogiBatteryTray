using Microsoft.Win32;

namespace LogiBatteryTray.Services;

/// <summary>
/// Manages per-user Windows autostart via HKCU\...\Run without requiring admin rights.
/// </summary>
public static class AutostartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppValueName = "LogiBatteryTray";

    public static bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(AppValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                return;
            }

            if (enabled)
            {
                string? exePath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exePath))
                {
                    key.SetValue(AppValueName, $"\"{exePath}\"", RegistryValueKind.String);
                }
            }
            else
            {
                key.DeleteValue(AppValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Ignore registry write failures gracefully.
        }
    }
}
