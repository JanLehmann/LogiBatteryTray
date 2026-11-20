using Microsoft.Win32;

namespace LogiBatteryTray.Services;

/// <summary>
/// Queries Windows Light/Dark personalization settings and native tray icon DPI metrics.
/// </summary>
public static class ThemeHelper
{
    private const string PersonalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>
    /// Returns true when the Windows taskbar/shell uses Light Theme (dark tray icons needed).
    /// </summary>
    public static bool IsSystemLightTheme()
    {
        return ReadThemeDword("SystemUsesLightTheme", defaultValue: true);
    }

    /// <summary>
    /// Returns true when Windows apps and flyouts use Light Theme.
    /// </summary>
    public static bool IsAppsLightTheme()
    {
        return ReadThemeDword("AppsUseLightTheme", defaultValue: true);
    }

    /// <summary>
    /// Returns the exact system tray icon pixel width/height for the current display DPI.
    /// </summary>
    public static int GetTrayIconSize()
    {
        try
        {
            uint dpi = NativeMethods.GetDpiForSystem();
            if (dpi == 0)
            {
                dpi = 96;
            }

            int smIcon = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SM_CXSMICON, dpi);
            return Math.Clamp(smIcon, 16, 64);
        }
        catch
        {
            return 16;
        }
    }

    private static bool ReadThemeDword(string valueName, bool defaultValue)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath);
            if (key?.GetValue(valueName) is int raw)
            {
                return raw != 0;
            }
        }
        catch
        {
            // Fall back to default theme if registry access fails.
        }

        return defaultValue;
    }
}
