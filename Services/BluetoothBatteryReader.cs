using System.Text;
using LogiBatteryTray.Models;

namespace LogiBatteryTray.Services;

/// <summary>
/// Reads Bluetooth Low Energy device battery levels directly from Windows PnP/CfgMgr32 properties.
/// </summary>
public static class BluetoothBatteryReader
{
    private static readonly Guid GuidDevPkeyDevice = new("A45C254E-DF1C-4EFD-8020-67D146A850E0");
    private static readonly Guid GuidDevPkeyName = new("B725F130-47EF-101A-A5F1-02608C9EEBAC");
    private static readonly Guid GuidBatteryProps = new("104EA319-6EE2-4701-BD47-8DDBF425BBE5");
    private static readonly Guid GuidConnectionProps = new("83DA6326-97A6-4088-9453-A1923F573B29");
    private static readonly Guid GuidContainerProps = new("78C34FC8-104A-4ACA-9EA4-524D52996E57");
    private static readonly Guid GuidRelationsProps = new("4340A6C5-93FA-4706-972C-7B648008A5A7");
    private static readonly Guid GuidBluetoothProps = new("2BD67D8B-8BEB-48D5-87E0-6CDA3428040A");

    private static readonly string[] LogitechNameHints =
    [
        "MX KEYS",
        "MX MASTER",
        "MX ANYWHERE",
        "MX VERTICAL",
        "MX ERGO",
        "LOGI",
        "LOGITECH"
    ];

    public static IReadOnlyList<DeviceBatteryInfo> ReadLogitechDevices()
    {
        var results = new List<DeviceBatteryInfo>();

        foreach (string instanceId in EnumerateBthleDeviceIds())
        {
            if (!instanceId.StartsWith("BTHLE\\Dev_", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (NativeMethods.CM_Locate_DevNodeW(
                out uint devInst,
                instanceId,
                NativeMethods.CM_LOCATE_DEVNODE_PHANTOM) != NativeMethods.CR_SUCCESS)
            {
                continue;
            }

            string? name = ReadStringProperty(devInst, GuidDevPkeyDevice, 14)
                        ?? ReadStringProperty(devInst, GuidDevPkeyName, 10);

            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            string? children = ReadMultiStringAsJoined(devInst, GuidRelationsProps, 9);
            bool hasLogiVid = children is not null &&
                              children.Contains("046D", StringComparison.OrdinalIgnoreCase);

            if (!hasLogiVid && !ContainsAnyHint(name, LogitechNameHints))
            {
                continue;
            }

            byte[]? batteryBytes = GetDevNodeProperty(devInst, GuidBatteryProps, 2);
            int? batteryPercent = batteryBytes is { Length: >= 1 } && batteryBytes[0] <= 100
                ? batteryBytes[0]
                : null;

            byte[]? updatedBytes = GetDevNodeProperty(devInst, GuidBatteryProps, 7);
            DateTime? lastUpdated = null;
            if (updatedBytes is { Length: 8 })
            {
                long fileTime = BitConverter.ToInt64(updatedBytes, 0);
                if (fileTime > 0)
                {
                    try
                    {
                        lastUpdated = DateTime.FromFileTime(fileTime);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        lastUpdated = null;
                    }
                }
            }

            byte[]? connectedBytes = GetDevNodeProperty(devInst, GuidConnectionProps, 15);
            bool isConnected = connectedBytes is { Length: >= 1 } && connectedBytes[0] != 0;

            string? categories = ReadMultiStringAsJoined(devInst, GuidContainerProps, 90);
            DeviceKind kind = ClassifyDevice(categories, name);

            string? btAddress = ReadStringProperty(devInst, GuidBluetoothProps, 1);

            results.Add(new DeviceBatteryInfo(
                instanceId,
                name.Trim(),
                kind,
                batteryPercent,
                isConnected,
                lastUpdated,
                btAddress));
        }

        var combined = new List<DeviceBatteryInfo>(HidppBatteryReader.EnrichDevices(results));
        combined.AddRange(HidppBatteryReader.ReadUsbHidppDevices());

        combined.Sort((a, b) =>
        {
            int kindCmp = ((int)a.Kind).CompareTo((int)b.Kind);
            return kindCmp != 0
                ? kindCmp
                : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        return combined;
    }

    private static IEnumerable<string> EnumerateBthleDeviceIds()
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            if (NativeMethods.CM_Get_Device_ID_List_SizeW(
                out uint len,
                "BTHLE",
                NativeMethods.CM_GETIDLIST_FILTER_ENUMERATOR) != NativeMethods.CR_SUCCESS || len == 0)
            {
                yield break;
            }

            var buffer = new char[len];
            uint cr = NativeMethods.CM_Get_Device_ID_ListW(
                "BTHLE",
                buffer,
                len,
                NativeMethods.CM_GETIDLIST_FILTER_ENUMERATOR);

            if (cr == NativeMethods.CR_BUFFER_SMALL)
            {
                continue;
            }

            if (cr != NativeMethods.CR_SUCCESS)
            {
                yield break;
            }

            string raw = new(buffer);
            foreach (string id in raw.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                yield return id;
            }
            yield break;
        }
    }

    private static byte[]? GetDevNodeProperty(uint devInst, Guid fmtid, uint pid)
    {
        var key = new NativeMethods.DEVPROPKEY { fmtid = fmtid, pid = pid };
        uint size = 0;
        uint cr = NativeMethods.CM_Get_DevNode_PropertyW(
            devInst,
            ref key,
            out _,
            null,
            ref size,
            0);

        if ((cr != NativeMethods.CR_SUCCESS && cr != NativeMethods.CR_BUFFER_SMALL) || size == 0)
        {
            return null;
        }

        byte[] buffer = new byte[size];
        cr = NativeMethods.CM_Get_DevNode_PropertyW(
            devInst,
            ref key,
            out _,
            buffer,
            ref size,
            0);

        return cr == NativeMethods.CR_SUCCESS ? buffer : null;
    }

    private static string? ReadStringProperty(uint devInst, Guid fmtid, uint pid)
    {
        byte[]? bytes = GetDevNodeProperty(devInst, fmtid, pid);
        if (bytes is null || bytes.Length < 2)
        {
            return null;
        }

        return Encoding.Unicode.GetString(bytes).TrimEnd('\0');
    }

    private static string? ReadMultiStringAsJoined(uint devInst, Guid fmtid, uint pid)
    {
        byte[]? bytes = GetDevNodeProperty(devInst, fmtid, pid);
        if (bytes is null || bytes.Length < 2)
        {
            return null;
        }

        return Encoding.Unicode.GetString(bytes).Replace('\0', ';');
    }

    private static DeviceKind ClassifyDevice(string? categories, string name)
    {
        if (!string.IsNullOrEmpty(categories))
        {
            if (categories.Contains("Input.Keyboard", StringComparison.OrdinalIgnoreCase))
            {
                return DeviceKind.Keyboard;
            }

            if (categories.Contains("Input.Mouse", StringComparison.OrdinalIgnoreCase))
            {
                return DeviceKind.Mouse;
            }
        }

        if (ContainsAnyHint(name, ["KEYS", "KEYBOARD", "TASTATUR"]))
        {
            return DeviceKind.Keyboard;
        }

        if (ContainsAnyHint(name, ["MASTER", "MOUSE", "MAUS", "ANYWHERE", "VERTICAL", "LIFT", "ERGO"]))
        {
            return DeviceKind.Mouse;
        }

        return DeviceKind.Other;
    }

    private static bool ContainsAnyHint(string text, IEnumerable<string> hints)
    {
        foreach (string hint in hints)
        {
            if (text.Contains(hint, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
