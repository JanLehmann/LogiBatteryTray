namespace LogiBatteryTray.Models;

/// <summary>
/// Represents the battery and connection state of a single Logitech Bluetooth LE device.
/// </summary>
public sealed record DeviceBatteryInfo(
    string InstanceId,
    string Name,
    DeviceKind Kind,
    int? BatteryPercent,
    bool IsConnected,
    DateTime? LastUpdated,
    string? BluetoothAddress,
    ChargingStatus ChargingStatus = ChargingStatus.Unknown,
    int? BatteryMillivolts = null,
    string? EstimatedTimeText = null)
{
    public bool IsCharging =>
        IsConnected &&
        ChargingStatus is ChargingStatus.Recharging
            or ChargingStatus.ChargingFinalStage
            or ChargingStatus.ChargingSlow;

    public bool IsChargingComplete =>
        IsConnected && ChargingStatus == ChargingStatus.ChargingComplete;
}
