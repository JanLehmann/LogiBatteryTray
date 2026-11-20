namespace LogiBatteryTray.Models;

/// <summary>
/// Charging status reported by Logitech HID++ 2.0 Unified Battery (feature 0x1004).
/// </summary>
public enum ChargingStatus
{
    Unknown = -1,
    Discharging = 0,
    Recharging = 1,
    ChargingFinalStage = 2,
    ChargingComplete = 3,
    ChargingSlow = 4,
    InvalidBattery = 5,
    ThermalError = 6,
    OtherError = 7
}

/// <summary>
/// German display helpers for device connection and charging states.
/// </summary>
public static class ChargingStatusText
{
    public static string ToShortStatus(ChargingStatus status, bool isConnected)
    {
        if (!isConnected)
        {
            return "Getrennt";
        }

        return status switch
        {
            ChargingStatus.Recharging => "Lädt",
            ChargingStatus.ChargingFinalStage => "Lädt (fast voll)",
            ChargingStatus.ChargingSlow => "Lädt langsam",
            ChargingStatus.ChargingComplete => "Voll geladen",
            ChargingStatus.InvalidBattery or ChargingStatus.ThermalError or ChargingStatus.OtherError => "Ladefehler",
            _ => "Verbunden"
        };
    }

    public static string ToDetailedStatus(ChargingStatus status, bool isConnected)
    {
        if (!isConnected)
        {
            return "Getrennt";
        }

        return status switch
        {
            ChargingStatus.ChargingComplete => "Voll geladen (am Kabel)",
            _ => ToShortStatus(status, isConnected)
        };
    }
}
