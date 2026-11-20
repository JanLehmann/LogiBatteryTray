using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using LogiBatteryTray.Models;
using Microsoft.Win32.SafeHandles;

namespace LogiBatteryTray.Services;

/// <summary>
/// Queries Logitech HID++ 2.0/4.2 devices (BLE Unified Battery 0x1004 and USB ADC/Unified Battery 0x1F20/0x1004).
/// </summary>
internal static class HidppBatteryReader
{
    private const ushort LogitechHidppUsagePage = 0xFF43;
    private const byte LongReportId = 0x11;
    private const byte DefaultDeviceIndex = 0xFF;
    private const byte SoftwareId = 0x0A;
    private const byte HidppErrorFeatureId = 0xFF;
    private const int QueryTimeoutMs = 800;

    private const string BleHidServiceToken = "{00001812-0000-1000-8000-00805f9b34fb}";

    // Standard Logitech LiPo discharge curve (mV -> %) used with HID++ feature 0x1F20.
    private static readonly (int Millivolts, int Percent)[] DischargingVoltageCurve =
    [
        (4186, 100),
        (4067, 90),
        (3989, 80),
        (3922, 70),
        (3859, 60),
        (3811, 50),
        (3778, 40),
        (3751, 30),
        (3717, 20),
        (3671, 10),
        (3646, 5),
        (3579, 2),
        (3500, 0)
    ];

    // While charging in Constant-Current (CC) phase (0% -> ~82%), terminal voltage rises up to ~4330 mV
    // (~280-300 mV above resting cell voltage). Above ~82%, the charger enters Constant-Voltage (CV) taper,
    // where current decreases and terminal voltage drops from ~4330 mV back down toward ~4190 mV.
    private static readonly (int Millivolts, int Percent)[] ChargingVoltageCurve =
    [
        (4330, 82),
        (4295, 76),
        (4245, 70),
        (4185, 62),
        (4141, 60),
        (4080, 50),
        (4005, 38),
        (3940, 28),
        (3870, 18),
        (3790, 10),
        (3700, 2),
        (3600, 0)
    ];

    private static readonly ConcurrentDictionary<string, byte> BleUnifiedBatteryCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, UsbFeatureIndices> UsbFeatureCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, CachedUsbDeviceState> UsbDeviceStateCache =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly record struct UsbFeatureIndices(byte UnifiedBatteryIdx, byte AdcIdx, byte DeviceNameIdx);

    private sealed record CachedUsbDeviceState(
        string Name,
        DeviceKind Kind,
        bool ResolvedFromHidppName,
        int? LastPercent,
        DateTime? LastUpdated,
        int? LastMillivolts = null);

    internal static IReadOnlyList<DeviceBatteryInfo> EnrichDevices(IReadOnlyList<DeviceBatteryInfo> devices)
    {
        if (devices.Count == 0 || !devices.Any(d => d.IsConnected))
        {
            return devices;
        }

        List<string> logiHidPaths = EnumerateLogitechHidPaths();
        if (logiHidPaths.Count == 0)
        {
            return devices;
        }

        var updated = new List<DeviceBatteryInfo>(devices.Count);
        foreach (DeviceBatteryInfo dev in devices)
        {
            if (!dev.IsConnected)
            {
                updated.Add(dev);
                continue;
            }

            string? addr = NormalizeAddress(dev.BluetoothAddress, dev.InstanceId);
            if (string.IsNullOrEmpty(addr))
            {
                updated.Add(dev);
                continue;
            }

            updated.Add(TryEnrichBleDevice(dev, addr, logiHidPaths));
        }

        return updated;
    }

    internal static IReadOnlyList<DeviceBatteryInfo> ReadUsbHidppDevices()
    {
        List<string> logiHidPaths = EnumerateLogitechHidPaths();
        if (logiHidPaths.Count == 0)
        {
            return [];
        }

        var results = new List<DeviceBatteryInfo>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string path in logiHidPaths)
        {
            // BLE HID collections are handled by BluetoothBatteryReader + EnrichDevices.
            if (path.Contains(BleHidServiceToken, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!path.Contains("vid_046d", StringComparison.OrdinalIgnoreCase) || !seenPaths.Add(path))
            {
                continue;
            }

            DeviceBatteryInfo? info = TryReadUsbDevice(path);
            if (info is not null)
            {
                results.Add(info);
            }
        }

        return results;
    }

    private static DeviceBatteryInfo TryEnrichBleDevice(
        DeviceBatteryInfo dev,
        string addressHex,
        IReadOnlyList<string> logiHidPaths)
    {
        foreach (string path in logiHidPaths)
        {
            if (!path.Contains(addressHex, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                using SafeFileHandle handle = OpenHidHandle(path);
                if (handle.IsInvalid || !TryGetHidppCaps(handle, out NativeMethods.HIDP_CAPS caps))
                {
                    continue;
                }

                if (QueryUnifiedBatteryBle(
                    handle,
                    addressHex,
                    caps.InputReportByteLength,
                    caps.OutputReportByteLength,
                    out int? soc,
                    out ChargingStatus status))
                {
                    return dev with
                    {
                        BatteryPercent = soc ?? dev.BatteryPercent,
                        LastUpdated = DateTime.Now,
                        ChargingStatus = status
                    };
                }
            }
            catch
            {
                // Keep cached PnP data when the BLE link or HID collection is transiently unavailable.
            }
        }

        return dev;
    }

    private static DeviceBatteryInfo? TryReadUsbDevice(string path)
    {
        try
        {
            using SafeFileHandle handle = OpenHidHandle(path);
            if (handle.IsInvalid || !TryGetHidppCaps(handle, out NativeMethods.HIDP_CAPS caps))
            {
                return null;
            }

            using var fs = new FileStream(handle, FileAccess.ReadWrite, bufferSize: 64, isAsync: true);

            UsbFeatureIndices features = GetOrDiscoverUsbFeatures(
                fs,
                path,
                caps.InputReportByteLength,
                caps.OutputReportByteLength);

            if (features.UnifiedBatteryIdx == 0 && features.AdcIdx == 0)
            {
                return null;
            }

            UsbDeviceStateCache.TryGetValue(path, out CachedUsbDeviceState? cached);
            string name = cached?.Name ?? string.Empty;
            DeviceKind kind = cached?.Kind ?? DeviceKind.Other;

            if ((cached is null || !cached.ResolvedFromHidppName) && features.DeviceNameIdx != 0)
            {
                if (TryReadHidppDeviceName(
                    fs,
                    features.DeviceNameIdx,
                    caps.InputReportByteLength,
                    caps.OutputReportByteLength,
                    out string? hidppName,
                    out DeviceKind? hidppKind))
                {
                    name = hidppName!;
                    kind = hidppKind ?? ClassifyUsbDeviceByName(name);
                    cached = new CachedUsbDeviceState(
                        name,
                        kind,
                        ResolvedFromHidppName: true,
                        cached?.LastPercent,
                        cached?.LastUpdated,
                        cached?.LastMillivolts);
                    UsbDeviceStateCache[path] = cached;
                }
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = ReadProductString(handle) ?? "Logitech USB-Gerät";
                kind = ClassifyUsbDeviceByName(name);
                cached = new CachedUsbDeviceState(
                    name,
                    kind,
                    ResolvedFromHidppName: false,
                    cached?.LastPercent,
                    cached?.LastUpdated,
                    cached?.LastMillivolts);
                UsbDeviceStateCache[path] = cached;
            }

            bool isConnected = false;
            int? batteryPercent = cached?.LastPercent;
            int? batteryMillivolts = cached?.LastMillivolts;
            DateTime? lastUpdated = cached?.LastUpdated;
            ChargingStatus chargingStatus = ChargingStatus.Unknown;

            if (features.UnifiedBatteryIdx != 0)
            {
                if (TryQueryUnifiedBattery(
                    fs,
                    features.UnifiedBatteryIdx,
                    caps.InputReportByteLength,
                    caps.OutputReportByteLength,
                    out int? soc,
                    out ChargingStatus status,
                    out bool featureInvalid))
                {
                    isConnected = true;
                    batteryPercent = soc ?? batteryPercent;
                    chargingStatus = status;
                    lastUpdated = DateTime.Now;
                }
                else if (featureInvalid)
                {
                    UsbFeatureCache.TryRemove(path, out _);
                }
            }
            else if (features.AdcIdx != 0)
            {
                if (TryQueryAdcBattery(
                    fs,
                    name,
                    features.AdcIdx,
                    caps.InputReportByteLength,
                    caps.OutputReportByteLength,
                    out int? soc,
                    out int? mv,
                    out ChargingStatus status,
                    out bool featureInvalid))
                {
                    isConnected = true;
                    batteryPercent = soc ?? batteryPercent;
                    batteryMillivolts = mv ?? batteryMillivolts;
                    chargingStatus = status;
                    lastUpdated = DateTime.Now;
                }
                else if (featureInvalid)
                {
                    UsbFeatureCache.TryRemove(path, out _);
                }
            }

            if (isConnected)
            {
                UsbDeviceStateCache[path] = new CachedUsbDeviceState(
                    name,
                    kind,
                    cached?.ResolvedFromHidppName ?? false,
                    batteryPercent,
                    lastUpdated,
                    batteryMillivolts);
            }

            return new DeviceBatteryInfo(
                path,
                name,
                kind,
                batteryPercent,
                isConnected,
                lastUpdated,
                BluetoothAddress: null,
                ChargingStatus: chargingStatus,
                BatteryMillivolts: batteryMillivolts);
        }
        catch
        {
            return null;
        }
    }

    private static UsbFeatureIndices GetOrDiscoverUsbFeatures(
        FileStream fs,
        string path,
        int inLen,
        int outLen)
    {
        if (UsbFeatureCache.TryGetValue(path, out UsbFeatureIndices cached))
        {
            return cached;
        }

        byte unifiedIdx = QueryFeatureIndex(fs, 0x1004, inLen, outLen);
        byte adcIdx = unifiedIdx == 0 ? QueryFeatureIndex(fs, 0x1F20, inLen, outLen) : (byte)0;
        byte nameIdx = unifiedIdx != 0 || adcIdx != 0 ? QueryFeatureIndex(fs, 0x0005, inLen, outLen) : (byte)0;

        var resolved = new UsbFeatureIndices(unifiedIdx, adcIdx, nameIdx);
        UsbFeatureCache[path] = resolved;
        return resolved;
    }

    private static byte QueryFeatureIndex(FileStream fs, ushort featureId, int inLen, int outLen)
    {
        byte[] req = new byte[outLen];
        req[0] = LongReportId;
        req[1] = DefaultDeviceIndex;
        req[2] = 0x00;
        req[3] = SoftwareId;
        req[4] = (byte)(featureId >> 8);
        req[5] = (byte)(featureId & 0xFF);

        byte[]? resp = SendAndReceive(fs, req, inLen, 0x00, SoftwareId);
        if (resp is null || resp[2] == HidppErrorFeatureId)
        {
            return 0;
        }

        return resp[4];
    }

    private static bool TryReadHidppDeviceName(
        FileStream fs,
        byte nameFeatureIdx,
        int inLen,
        int outLen,
        out string? name,
        out DeviceKind? kind)
    {
        name = null;
        kind = null;

        byte[] lenReq = new byte[outLen];
        lenReq[0] = LongReportId;
        lenReq[1] = DefaultDeviceIndex;
        lenReq[2] = nameFeatureIdx;
        lenReq[3] = SoftwareId;

        byte[]? lenResp = SendAndReceive(fs, lenReq, inLen, nameFeatureIdx, SoftwareId);
        if (lenResp is null || lenResp[2] == HidppErrorFeatureId)
        {
            return false;
        }

        int nameLen = lenResp[4];
        if (nameLen <= 0 || nameLen > 64)
        {
            return false;
        }

        var nameBytes = new List<byte>(nameLen);
        while (nameBytes.Count < nameLen)
        {
            const byte getNameFuncSw = 0x10 | SoftwareId;
            byte[] chunkReq = new byte[outLen];
            chunkReq[0] = LongReportId;
            chunkReq[1] = DefaultDeviceIndex;
            chunkReq[2] = nameFeatureIdx;
            chunkReq[3] = getNameFuncSw;
            chunkReq[4] = (byte)nameBytes.Count;

            byte[]? chunkResp = SendAndReceive(fs, chunkReq, inLen, nameFeatureIdx, getNameFuncSw);
            if (chunkResp is null || chunkResp[2] == HidppErrorFeatureId)
            {
                return false;
            }

            for (int i = 4; i < chunkResp.Length && nameBytes.Count < nameLen; i++)
            {
                nameBytes.Add(chunkResp[i]);
            }
        }

        string decoded = Encoding.UTF8.GetString(nameBytes.ToArray()).TrimEnd('\0').Trim();
        if (string.IsNullOrWhiteSpace(decoded))
        {
            return false;
        }

        name = decoded;

        const byte getTypeFuncSw = 0x20 | SoftwareId;
        byte[] typeReq = new byte[outLen];
        typeReq[0] = LongReportId;
        typeReq[1] = DefaultDeviceIndex;
        typeReq[2] = nameFeatureIdx;
        typeReq[3] = getTypeFuncSw;

        byte[]? typeResp = SendAndReceive(fs, typeReq, inLen, nameFeatureIdx, getTypeFuncSw);
        if (typeResp is not null && typeResp[2] == nameFeatureIdx)
        {
            kind = typeResp[4] switch
            {
                0 => DeviceKind.Keyboard,
                3 => DeviceKind.Mouse,
                8 => DeviceKind.Headset,
                _ => null
            };
        }

        return true;
    }

    private static bool QueryUnifiedBatteryBle(
        SafeFileHandle handle,
        string addressHex,
        int inLen,
        int outLen,
        out int? soc,
        out ChargingStatus chargingStatus)
    {
        soc = null;
        chargingStatus = ChargingStatus.Unknown;

        using var fs = new FileStream(handle, FileAccess.ReadWrite, bufferSize: 64, isAsync: true);

        for (int attempt = 0; attempt < 2; attempt++)
        {
            if (!BleUnifiedBatteryCache.TryGetValue(addressHex, out byte featureIdx))
            {
                featureIdx = QueryFeatureIndex(fs, 0x1004, inLen, outLen);
                if (featureIdx == 0)
                {
                    return false;
                }

                BleUnifiedBatteryCache[addressHex] = featureIdx;
            }

            if (TryQueryUnifiedBattery(fs, featureIdx, inLen, outLen, out soc, out chargingStatus, out bool featureInvalid))
            {
                return true;
            }

            if (featureInvalid)
            {
                BleUnifiedBatteryCache.TryRemove(addressHex, out _);
                continue;
            }

            return false;
        }

        return false;
    }

    private static bool TryQueryUnifiedBattery(
        FileStream fs,
        byte featureIdx,
        int inLen,
        int outLen,
        out int? soc,
        out ChargingStatus chargingStatus,
        out bool featureInvalid)
    {
        soc = null;
        chargingStatus = ChargingStatus.Unknown;
        featureInvalid = false;

        const byte getStatusFuncSw = 0x10 | SoftwareId;
        byte[] batReq = new byte[outLen];
        batReq[0] = LongReportId;
        batReq[1] = DefaultDeviceIndex;
        batReq[2] = featureIdx;
        batReq[3] = getStatusFuncSw;

        byte[]? batResp = SendAndReceive(fs, batReq, inLen, featureIdx, getStatusFuncSw);
        if (batResp is null)
        {
            return false;
        }

        if (batResp[2] == HidppErrorFeatureId)
        {
            // Error 0x05 (HW_ERROR) means device is offline/powered off; other errors invalidate cached index.
            featureInvalid = batResp[5] != 0x05;
            return false;
        }

        if (batResp[4] <= 100)
        {
            soc = batResp[4];
        }

        int rawStatus = batResp[6];
        chargingStatus = Enum.IsDefined(typeof(ChargingStatus), rawStatus)
            ? (ChargingStatus)rawStatus
            : ChargingStatus.Unknown;

        return true;
    }

    private static bool TryQueryAdcBattery(
        FileStream fs,
        string deviceName,
        byte adcFeatureIdx,
        int inLen,
        int outLen,
        out int? soc,
        out int? millivoltsOut,
        out ChargingStatus chargingStatus,
        out bool featureInvalid)
    {
        soc = null;
        millivoltsOut = null;
        chargingStatus = ChargingStatus.Unknown;
        featureInvalid = false;

        byte[] req = new byte[outLen];
        req[0] = LongReportId;
        req[1] = DefaultDeviceIndex;
        req[2] = adcFeatureIdx;
        req[3] = SoftwareId;

        byte[]? resp = SendAndReceive(fs, req, inLen, adcFeatureIdx, SoftwareId);
        if (resp is null)
        {
            return false;
        }

        if (resp[2] == HidppErrorFeatureId)
        {
            featureInvalid = resp[5] != 0x05;
            return false;
        }

        int millivolts = (resp[4] << 8) | resp[5];
        if (millivolts <= 0)
        {
            return false;
        }

        millivoltsOut = millivolts;
        chargingStatus = MapAdcFlags(resp[6]);
        bool isCharging = chargingStatus is ChargingStatus.Recharging
            or ChargingStatus.ChargingFinalStage
            or ChargingStatus.ChargingSlow;
        int rawPercent = chargingStatus == ChargingStatus.ChargingComplete
            ? 100
            : VoltageToPercent(millivolts, isCharging);
        soc = BatteryHistoryTracker.ProcessAdcReading(deviceName, millivolts, chargingStatus, rawPercent);

        return true;
    }

    private static ChargingStatus MapAdcFlags(byte flags)
    {
        return flags switch
        {
            0x01 => ChargingStatus.Discharging,
            0x03 => ChargingStatus.Recharging,
            0x07 => ChargingStatus.ChargingComplete,
            _ when (flags & 0x08) != 0 => ChargingStatus.OtherError,
            _ when (flags & 0x04) != 0 => ChargingStatus.ChargingComplete,
            _ when (flags & 0x02) != 0 => ChargingStatus.Recharging,
            _ when (flags & 0x01) != 0 => ChargingStatus.Discharging,
            _ => ChargingStatus.Unknown
        };
    }

    private static int VoltageToPercent(int millivolts, bool isCharging)
    {
        (int Millivolts, int Percent)[] curve = isCharging ? ChargingVoltageCurve : DischargingVoltageCurve;
        if (millivolts >= curve[0].Millivolts)
        {
            return curve[0].Percent;
        }

        for (int i = 0; i < curve.Length - 1; i++)
        {
            (int highMv, int highPct) = curve[i];
            (int lowMv, int lowPct) = curve[i + 1];
            if (millivolts >= lowMv)
            {
                double fraction = (double)(millivolts - lowMv) / (highMv - lowMv);
                return Math.Clamp((int)Math.Round(lowPct + fraction * (highPct - lowPct)), 0, 100);
            }
        }

        return 0;
    }

    private static byte[]? SendAndReceive(
        FileStream fs,
        byte[] request,
        int inLen,
        byte expectedFeatureIdx,
        byte expectedFuncSw)
    {
        using var cts = new CancellationTokenSource(QueryTimeoutMs);
        try
        {
            fs.WriteAsync(request.AsMemory(), cts.Token).AsTask().GetAwaiter().GetResult();

            byte[] buffer = new byte[inLen];
            while (!cts.IsCancellationRequested)
            {
                int read = fs.ReadAsync(buffer.AsMemory(0, inLen), cts.Token)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();

                if (read < 7 || buffer[0] != LongReportId)
                {
                    continue;
                }

                if (buffer[2] == expectedFeatureIdx && buffer[3] == expectedFuncSw)
                {
                    return buffer;
                }

                if (buffer[2] == HidppErrorFeatureId &&
                    buffer[3] == expectedFeatureIdx &&
                    buffer[4] == expectedFuncSw)
                {
                    return buffer;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout on sleeping radio; caller falls back gracefully.
        }

        return null;
    }

    private static SafeFileHandle OpenHidHandle(string path)
    {
        return NativeMethods.CreateFileW(
            path,
            NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero,
            NativeMethods.OPEN_EXISTING,
            NativeMethods.FILE_FLAG_OVERLAPPED,
            IntPtr.Zero);
    }

    private static bool TryGetHidppCaps(SafeFileHandle handle, out NativeMethods.HIDP_CAPS caps)
    {
        caps = default;
        if (!NativeMethods.HidD_GetPreparsedData(handle, out IntPtr preparsed) || preparsed == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            return NativeMethods.HidP_GetCaps(preparsed, out caps) >= 0 &&
                   caps.UsagePage == LogitechHidppUsagePage &&
                   caps.InputReportByteLength >= 20 &&
                   caps.OutputReportByteLength >= 20;
        }
        finally
        {
            NativeMethods.HidD_FreePreparsedData(preparsed);
        }
    }

    private static string? ReadProductString(SafeFileHandle handle)
    {
        byte[] buffer = new byte[256];
        if (!NativeMethods.HidD_GetProductString(handle, buffer, (uint)buffer.Length))
        {
            return null;
        }

        string text = Encoding.Unicode.GetString(buffer);
        int nullIdx = text.IndexOf('\0');
        string trimmed = (nullIdx >= 0 ? text[..nullIdx] : text).Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static DeviceKind ClassifyUsbDeviceByName(string name)
    {
        if (ContainsAny(name, ["HEADSET", "PRO X", "G733", "G535", "G435", "G935", "G933", "G533", "ZONE", "ASTRO"]))
        {
            return DeviceKind.Headset;
        }

        if (ContainsAny(name, ["KEYBOARD", "KEYS", "TASTATUR", "G915", "G715"]))
        {
            return DeviceKind.Keyboard;
        }

        if (ContainsAny(name, ["MOUSE", "MAUS", "MASTER", "SUPERLIGHT", "ANYWHERE", "VERTICAL", "LIFT", "ERGO"]))
        {
            return DeviceKind.Mouse;
        }

        return DeviceKind.Other;
    }

    private static bool ContainsAny(string text, IEnumerable<string> tokens)
    {
        foreach (string token in tokens)
        {
            if (text.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> EnumerateLogitechHidPaths()
    {
        var paths = new List<string>();
        Guid hidGuid = NativeMethods.GUID_DEVINTERFACE_HID;
        IntPtr devInfo = NativeMethods.SetupDiGetClassDevsW(
            ref hidGuid,
            IntPtr.Zero,
            IntPtr.Zero,
            NativeMethods.DIGCF_PRESENT | NativeMethods.DIGCF_DEVICEINTERFACE);

        if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1))
        {
            return paths;
        }

        try
        {
            uint index = 0;
            int interfaceStructSize = Marshal.SizeOf<NativeMethods.SP_DEVICE_INTERFACE_DATA>();
            int detailHeaderSize = IntPtr.Size == 8 ? 8 : 6;

            while (true)
            {
                var interfaceData = new NativeMethods.SP_DEVICE_INTERFACE_DATA
                {
                    cbSize = (uint)interfaceStructSize
                };

                if (!NativeMethods.SetupDiEnumDeviceInterfaces(
                    devInfo,
                    IntPtr.Zero,
                    ref hidGuid,
                    index++,
                    ref interfaceData))
                {
                    break;
                }

                NativeMethods.SetupDiGetDeviceInterfaceDetailW(
                    devInfo,
                    ref interfaceData,
                    IntPtr.Zero,
                    0,
                    out uint requiredSize,
                    IntPtr.Zero);

                if (requiredSize <= 4)
                {
                    continue;
                }

                IntPtr detailMem = Marshal.AllocHGlobal((int)requiredSize);
                try
                {
                    Marshal.WriteInt32(detailMem, detailHeaderSize);
                    if (!NativeMethods.SetupDiGetDeviceInterfaceDetailW(
                        devInfo,
                        ref interfaceData,
                        detailMem,
                        requiredSize,
                        out _,
                        IntPtr.Zero))
                    {
                        continue;
                    }

                    string? path = Marshal.PtrToStringUni(new IntPtr(detailMem.ToInt64() + 4));
                    if (!string.IsNullOrEmpty(path) &&
                        path.Contains("046d", StringComparison.OrdinalIgnoreCase))
                    {
                        paths.Add(path);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(detailMem);
                }
            }
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(devInfo);
        }

        return paths;
    }

    private static string? NormalizeAddress(string? btAddress, string instanceId)
    {
        if (!string.IsNullOrWhiteSpace(btAddress))
        {
            string cleaned = btAddress.Replace(":", string.Empty, StringComparison.Ordinal)
                                      .Replace("-", string.Empty, StringComparison.Ordinal)
                                      .Trim();
            if (cleaned.Length == 12)
            {
                return cleaned;
            }
        }

        const string prefix = "BTHLE\\Dev_";
        int start = instanceId.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (start >= 0)
        {
            start += prefix.Length;
            int end = instanceId.IndexOf('\\', start);
            string candidate = end > start ? instanceId[start..end] : instanceId[start..];
            if (candidate.Length == 12)
            {
                return candidate;
            }
        }

        return null;
    }
}
