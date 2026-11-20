using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LogiBatteryTray.Models;

namespace LogiBatteryTray.Services;

/// <summary>
/// Persists battery history to CSV and provides self-calibrating voltage smoothing/anchoring
/// for ADC-based headsets (HID++ 0x1F20) to prevent charging jumps and voltage jitter.
/// </summary>
public static class BatteryHistoryTracker
{
    private static readonly object SyncLock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly Dictionary<string, Queue<int>> RollingMillivoltWindows =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, int> LastRawPercentByDevice =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, LoggedDeviceSnapshot> LastLoggedSnapshots =
        new(StringComparer.OrdinalIgnoreCase);

    private static CalibrationStore? _store;

    public static string DataDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LogiBatteryTray");

    public static string LogFilePath => Path.Combine(DataDirectory, "battery_log.csv");

    public static string CalibrationFilePath => Path.Combine(DataDirectory, "battery_calibration.json");

    /// <summary>
    /// Smooths raw ADC voltage readings, anchors charging transitions to the true pre-charge percentage,
    /// learns charging/discharging voltage pairs, and enforces monotonic direction locks.
    /// </summary>
    public static int ProcessAdcReading(
        string deviceName,
        int rawMillivolts,
        ChargingStatus status,
        int rawCurvePercent)
    {
        lock (SyncLock)
        {
            string key = NormalizeKey(deviceName);
            LastRawPercentByDevice[key] = rawCurvePercent;

            CalibrationStore store = LoadStoreLocked();
            if (!store.Devices.TryGetValue(key, out DeviceCalibrationState? state))
            {
                state = new DeviceCalibrationState();
                store.Devices[key] = state;
            }

            DateTime now = DateTime.UtcNow;

            if (status == ChargingStatus.ChargingComplete)
            {
                GetOrResetWindowLocked(key, status != state.LastChargingStatus).Clear();
                state.LastDisplayedPercent = 100;
                state.LastChargingStatus = status;
                state.LastMillivolts = rawMillivolts;
                state.LastReadingUtc = now;
                state.LastChargingMv = rawMillivolts;
                state.LastChargingUtc = now;
                SaveStoreLocked(store);
                return 100;
            }

            bool statusChanged = status != state.LastChargingStatus;
            Queue<int> window = GetOrResetWindowLocked(key, statusChanged);
            window.Enqueue(rawMillivolts);
            while (window.Count > 4)
            {
                window.Dequeue();
            }

            int smoothedMv = (int)Math.Round(window.Average());
            bool isCharging = status is ChargingStatus.Recharging
                or ChargingStatus.ChargingFinalStage
                or ChargingStatus.ChargingSlow;
            bool wasCharging = state.LastChargingStatus is ChargingStatus.Recharging
                or ChargingStatus.ChargingFinalStage
                or ChargingStatus.ChargingSlow;

            // Detect Discharging -> Charging transition: anchor to the true pre-charge percentage
            if (isCharging && state.LastChargingStatus == ChargingStatus.Discharging)
            {
                if (state.LastDischargingPercent is int preChargePct &&
                    state.LastDischargingUtc is DateTime preChargeUtc &&
                    (now - preChargeUtc).TotalMinutes <= 10)
                {
                    state.ChargeAnchorPercent = preChargePct;
                    state.ChargeAnchorMv = smoothedMv;
                    state.ChargeAnchorUtc = now;
                    UpsertLearnedPointLocked(state, smoothedMv, preChargePct, now);
                }
            }

            // Detect Charging -> Discharging transition: learn resting percentage for the last charging voltage
            if (status == ChargingStatus.Discharging && (wasCharging || state.LastChargingStatus == ChargingStatus.ChargingComplete))
            {
                state.ChargeAnchorPercent = null;
                state.ChargeAnchorMv = null;
                state.ChargeAnchorUtc = null;

                if (state.LastChargingStatus == ChargingStatus.ChargingComplete &&
                    rawMillivolts is >= 4080 and <= 4230)
                {
                    state.FullRestingMillivolts = (int)Math.Round(state.FullRestingMillivolts * 0.7 + rawMillivolts * 0.3);
                }
                else if (wasCharging &&
                         state.LastChargingMv is int lastChgMv &&
                         state.LastChargingUtc is DateTime lastChgUtc &&
                         (now - lastChgUtc).TotalMinutes <= 5)
                {
                    UpsertLearnedPointLocked(state, lastChgMv, rawCurvePercent, now);
                }
            }

            int candidate = rawCurvePercent;

            if (status == ChargingStatus.Discharging)
            {
                // Adjust top-end if learned full resting voltage is lower than factory 4186 mV
                if (state.FullRestingMillivolts < 4175 && smoothedMv >= state.FullRestingMillivolts)
                {
                    candidate = 100;
                }

                // Monotonic discharge lock: while continuously discharging, percentage cannot jump upward
                if (state.LastChargingStatus == ChargingStatus.Discharging &&
                    state.LastDisplayedPercent is int prevDisPct &&
                    state.LastReadingUtc is DateTime prevDisReadUtc &&
                    (now - prevDisReadUtc).TotalMinutes <= 15)
                {
                    candidate = Math.Min(prevDisPct, candidate);
                }

                candidate = Math.Clamp(candidate, 0, 100);
                state.LastDischargingPercent = candidate;
                state.LastDischargingMv = smoothedMv;
                state.LastDischargingUtc = now;
            }
            else if (isCharging)
            {
                candidate = ApplyLearnedChargingPoints(state, smoothedMv, candidate);

                // If we have an active plug-in anchor from this charging session, interpolate smoothly from it
                if (state.ChargeAnchorPercent is int anchorPct &&
                    state.ChargeAnchorMv is int anchorMv &&
                    state.ChargeAnchorUtc is DateTime anchorUtc &&
                    (now - anchorUtc).TotalHours <= 5)
                {
                    const int ceilingMv = 4210;
                    int spanMv = Math.Max(40, ceilingMv - anchorMv);
                    double progress = Math.Clamp((double)(smoothedMv - anchorMv) / spanMv, 0.0, 1.0);
                    int anchoredEstimate = (int)Math.Round(anchorPct + progress * (99 - anchorPct));

                    // Blend anchored trajectory with curve estimate
                    candidate = (int)Math.Round(anchoredEstimate * 0.7 + candidate * 0.3);

                    // Physical charge-rate cap (~1.4% per minute max on USB-C) prevents instant jump on plug-in
                    double elapsedMin = Math.Max(0.0, (now - anchorUtc).TotalMinutes);
                    int maxAllowedByTime = Math.Min(99, anchorPct + (int)Math.Floor(elapsedMin * 1.4) + 1);
                    candidate = Math.Clamp(candidate, anchorPct, maxAllowedByTime);
                }

                // Monotonic charge lock: while continuously charging, percentage cannot drop backward
                if (wasCharging &&
                    state.LastDisplayedPercent is int prevChgPct &&
                    state.LastReadingUtc is DateTime prevChgReadUtc &&
                    (now - prevChgReadUtc).TotalMinutes <= 15)
                {
                    candidate = Math.Max(prevChgPct, candidate);
                }

                candidate = Math.Clamp(candidate, 1, 99);
                state.LastChargingMv = smoothedMv;
                state.LastChargingUtc = now;
            }

            state.LastDisplayedPercent = candidate;
            state.LastChargingStatus = status;
            state.LastMillivolts = smoothedMv;
            state.LastReadingUtc = now;

            SaveStoreLocked(store);
            return candidate;
        }
    }

    /// <summary>
    /// Appends a CSV row to battery_log.csv whenever a device's state, percentage, or voltage meaningfully changes.
    /// </summary>
    public static void LogReadings(IReadOnlyList<DeviceBatteryInfo> devices)
    {
        if (devices.Count == 0)
        {
            return;
        }

        lock (SyncLock)
        {
            try
            {
                Directory.CreateDirectory(DataDirectory);
                bool writeHeader = !File.Exists(LogFilePath);
                var sb = new StringBuilder();

                if (writeHeader)
                {
                    sb.AppendLine("Timestamp,DeviceName,Kind,Connected,ChargingStatus,Millivolts,RawPercent,DisplayedPercent");
                }

                DateTime nowLocal = DateTime.Now;
                DateTime nowUtc = DateTime.UtcNow;

                foreach (DeviceBatteryInfo dev in devices)
                {
                    string key = NormalizeKey(dev.Name);
                    int? rawPct = LastRawPercentByDevice.TryGetValue(key, out int r) ? r : dev.BatteryPercent;

                    if (!ShouldLogDeviceLocked(key, dev, nowUtc))
                    {
                        continue;
                    }

                    LastLoggedSnapshots[key] = new LoggedDeviceSnapshot(
                        dev.IsConnected,
                        dev.ChargingStatus,
                        dev.BatteryPercent,
                        dev.BatteryMillivolts,
                        nowUtc);

                    string ts = nowLocal.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                    string safeName = EscapeCsv(dev.Name);
                    string mvStr = dev.BatteryMillivolts?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
                    string rawStr = rawPct?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
                    string dispStr = dev.BatteryPercent?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

                    sb.AppendLine($"{ts},{safeName},{dev.Kind},{dev.IsConnected},{dev.ChargingStatus},{mvStr},{rawStr},{dispStr}");
                }

                if (sb.Length > 0)
                {
                    File.AppendAllText(LogFilePath, sb.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                // Ignore transient I/O errors so logging never interrupts tray updates.
            }
        }
    }

    private static bool ShouldLogDeviceLocked(string key, DeviceBatteryInfo dev, DateTime nowUtc)
    {
        if (!LastLoggedSnapshots.TryGetValue(key, out LoggedDeviceSnapshot? prev))
        {
            return true;
        }

        if (prev.IsConnected != dev.IsConnected ||
            prev.ChargingStatus != dev.ChargingStatus ||
            prev.DisplayedPercent != dev.BatteryPercent)
        {
            return true;
        }

        if (prev.Millivolts.HasValue && dev.BatteryMillivolts.HasValue &&
            Math.Abs(prev.Millivolts.Value - dev.BatteryMillivolts.Value) >= 5)
        {
            return true;
        }

        if (dev.IsConnected && (nowUtc - prev.LoggedUtc).TotalMinutes >= 10)
        {
            return true;
        }

        return false;
    }

    private static int ApplyLearnedChargingPoints(DeviceCalibrationState state, int smoothedMv, int baseCandidate)
    {
        if (state.LearnedChargingPoints.Count == 0)
        {
            return baseCandidate;
        }

        LearnedVoltagePoint? nearest = null;
        int minDiff = int.MaxValue;
        foreach (LearnedVoltagePoint pt in state.LearnedChargingPoints)
        {
            int diff = Math.Abs(pt.ChargingMv - smoothedMv);
            if (diff < minDiff)
            {
                minDiff = diff;
                nearest = pt;
            }
        }

        if (nearest is null || minDiff > 110)
        {
            return baseCandidate;
        }

        // Weight goes from 0.85 at 0 mV distance down to 0.0 at 110 mV distance
        double weight = 0.85 * (1.0 - (minDiff / 110.0));
        int offset = nearest.TruePercent - baseCandidate;
        return Math.Clamp((int)Math.Round(baseCandidate + offset * weight), 1, 99);
    }

    private static void UpsertLearnedPointLocked(
        DeviceCalibrationState state,
        int chargingMv,
        int truePercent,
        DateTime nowUtc)
    {
        if (chargingMv is < 3550 or > 4250 || truePercent is < 0 or > 100)
        {
            return;
        }

        for (int i = 0; i < state.LearnedChargingPoints.Count; i++)
        {
            LearnedVoltagePoint existing = state.LearnedChargingPoints[i];
            if (Math.Abs(existing.ChargingMv - chargingMv) <= 20)
            {
                int blendedMv = (int)Math.Round(existing.ChargingMv * 0.5 + chargingMv * 0.5);
                int blendedPct = (int)Math.Round(existing.TruePercent * 0.4 + truePercent * 0.6);
                state.LearnedChargingPoints[i] = new LearnedVoltagePoint(blendedMv, blendedPct, nowUtc);
                return;
            }
        }

        state.LearnedChargingPoints.Add(new LearnedVoltagePoint(chargingMv, truePercent, nowUtc));
        state.LearnedChargingPoints.Sort((a, b) => a.ChargingMv.CompareTo(b.ChargingMv));

        if (state.LearnedChargingPoints.Count > 24)
        {
            state.LearnedChargingPoints.RemoveAt(0);
        }
    }

    private static Queue<int> GetOrResetWindowLocked(string key, bool reset)
    {
        if (!RollingMillivoltWindows.TryGetValue(key, out Queue<int>? q))
        {
            q = new Queue<int>(4);
            RollingMillivoltWindows[key] = q;
        }
        else if (reset)
        {
            q.Clear();
        }

        return q;
    }

    private static CalibrationStore LoadStoreLocked()
    {
        if (_store is not null)
        {
            return _store;
        }

        try
        {
            if (File.Exists(CalibrationFilePath))
            {
                string json = File.ReadAllText(CalibrationFilePath);
                _store = JsonSerializer.Deserialize<CalibrationStore>(json, JsonOptions);
            }
        }
        catch
        {
            _store = null;
        }

        _store ??= new CalibrationStore();
        return _store;
    }

    private static void SaveStoreLocked(CalibrationStore store)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            string json = JsonSerializer.Serialize(store, JsonOptions);
            File.WriteAllText(CalibrationFilePath, json, Encoding.UTF8);
        }
        catch
        {
            // Ignore transient write errors.
        }
    }

    private static string NormalizeKey(string name) => name.Trim().ToLowerInvariant();

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        }

        return value;
    }

    private sealed record LoggedDeviceSnapshot(
        bool IsConnected,
        ChargingStatus ChargingStatus,
        int? DisplayedPercent,
        int? Millivolts,
        DateTime LoggedUtc);

    internal sealed class CalibrationStore
    {
        public Dictionary<string, DeviceCalibrationState> Devices { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    internal sealed class DeviceCalibrationState
    {
        public int FullRestingMillivolts { get; set; } = 4186;
        public int? LastDisplayedPercent { get; set; }
        public ChargingStatus LastChargingStatus { get; set; } = ChargingStatus.Unknown;
        public int? LastMillivolts { get; set; }
        public DateTime? LastReadingUtc { get; set; }

        public int? LastDischargingPercent { get; set; }
        public int? LastDischargingMv { get; set; }
        public DateTime? LastDischargingUtc { get; set; }

        public int? LastChargingMv { get; set; }
        public DateTime? LastChargingUtc { get; set; }

        public int? ChargeAnchorPercent { get; set; }
        public int? ChargeAnchorMv { get; set; }
        public DateTime? ChargeAnchorUtc { get; set; }

        public List<LearnedVoltagePoint> LearnedChargingPoints { get; set; } = [];
    }

    internal sealed record LearnedVoltagePoint(int ChargingMv, int TruePercent, DateTime UpdatedUtc);
}
