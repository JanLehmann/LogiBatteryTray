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
                state.PeakChargingMv = null;
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
                state.PeakChargingMv = smoothedMv;
                if (state.LastDischargingPercent is int preChargePct &&
                    state.LastDischargingUtc is DateTime preChargeUtc &&
                    (now - preChargeUtc).TotalMinutes <= 10)
                {
                    state.ChargeAnchorPercent = preChargePct;
                    state.ChargeAnchorMv = smoothedMv;
                    state.ChargeAnchorUtc = now;
                    if (preChargePct <= 80 && smoothedMv < 4305)
                    {
                        UpsertLearnedPointLocked(state, smoothedMv, preChargePct, now);
                    }
                }
            }

            // Detect Charging -> Discharging transition: learn resting percentage for the last charging voltage
            if (status == ChargingStatus.Discharging && (wasCharging || state.LastChargingStatus == ChargingStatus.ChargingComplete))
            {
                int? sessionPeakMv = state.PeakChargingMv;
                state.ChargeAnchorPercent = null;
                state.ChargeAnchorMv = null;
                state.ChargeAnchorUtc = null;
                state.PeakChargingMv = null;

                if (state.LastChargingStatus == ChargingStatus.ChargingComplete &&
                    rawMillivolts is >= 4080 and <= 4230)
                {
                    state.FullRestingMillivolts = (int)Math.Round(state.FullRestingMillivolts * 0.7 + rawMillivolts * 0.3);
                }
                else if (wasCharging &&
                         state.LastChargingMv is int lastChgMv &&
                         state.LastChargingUtc is DateTime lastChgUtc &&
                         (now - lastChgUtc).TotalMinutes <= 5 &&
                         rawCurvePercent <= 80 &&
                         (sessionPeakMv is null || lastChgMv >= sessionPeakMv.Value - 8))
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
                state.PeakChargingMv = Math.Max(state.PeakChargingMv ?? smoothedMv, smoothedMv);
                int peakMv = state.PeakChargingMv.Value;
                bool inCvTaper = peakMv >= 4273 && smoothedMv <= peakMv - 6;

                if (inCvTaper)
                {
                    // Official Logitech <charging><descending> CV-topping curve (87% -> 99%):
                    // As charging current tapers off, terminal voltage drops from peak down toward 4208 mV.
                    LastRawPercentByDevice[key] = HidppBatteryReader.DescendingVoltageToPercent(rawMillivolts);
                    candidate = HidppBatteryReader.DescendingVoltageToPercent(smoothedMv);
                }
                else
                {
                    candidate = ApplyLearnedChargingPoints(state, smoothedMv, candidate);
                }

                // If we have an active plug-in anchor from this charging session, interpolate smoothly from it
                if (state.ChargeAnchorPercent is int anchorPct &&
                    state.ChargeAnchorUtc is DateTime anchorUtc &&
                    (now - anchorUtc).TotalHours <= 5)
                {
                    double elapsedMin = Math.Max(0.0, (now - anchorUtc).TotalMinutes);

                    // Measured PRO X Wireless charge profile:
                    // ~0.51%/min during CC phase (up to 82%), tapering to ~0.38%/min during CV phase (82% -> 99%).
                    double timeEstimate;
                    if (anchorPct < 82)
                    {
                        double minToCv = (82 - anchorPct) / 0.51;
                        if (elapsedMin <= minToCv)
                        {
                            timeEstimate = anchorPct + elapsedMin * 0.51;
                        }
                        else
                        {
                            timeEstimate = 82.0 + (elapsedMin - minToCv) * 0.38;
                        }
                    }
                    else
                    {
                        timeEstimate = anchorPct + elapsedMin * 0.38;
                    }

                    int timeBasedPct = (int)Math.Round(Math.Clamp(timeEstimate, anchorPct, 99.0));
                    candidate = (int)Math.Round(timeBasedPct * 0.75 + candidate * 0.25);

                    // Physical charge-rate cap (~0.58% per minute max) prevents racing ahead to 99%
                    int maxAllowedByTime = Math.Min(99, anchorPct + (int)Math.Floor(elapsedMin * 0.58) + 1);
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
    /// Updates discharge-rate learning, attaches remaining runtime / charge time estimates,
    /// and appends a CSV row to battery_log.csv whenever a device's state meaningfully changes.
    /// </summary>
    public static IReadOnlyList<DeviceBatteryInfo> UpdateEstimatesAndLog(IReadOnlyList<DeviceBatteryInfo> devices)
    {
        if (devices.Count == 0)
        {
            return devices;
        }

        lock (SyncLock)
        {
            var enriched = new List<DeviceBatteryInfo>(devices.Count);
            CalibrationStore store = LoadStoreLocked();
            bool storeDirty = false;
            DateTime nowLocal = DateTime.Now;
            DateTime nowUtc = DateTime.UtcNow;

            StringBuilder? csvBuilder = null;
            bool writeHeader = !File.Exists(LogFilePath);

            foreach (DeviceBatteryInfo dev in devices)
            {
                string key = NormalizeKey(dev.Name);
                if (!store.Devices.TryGetValue(key, out DeviceCalibrationState? state))
                {
                    state = new DeviceCalibrationState();
                    store.Devices[key] = state;
                    storeDirty = true;
                }

                if (UpdateDischargeRateTrackingLocked(state, dev, nowUtc))
                {
                    storeDirty = true;
                }

                string? estimateText = ComputeEstimatedTimeText(state, dev);
                DeviceBatteryInfo updatedDev = dev with { EstimatedTimeText = estimateText };
                enriched.Add(updatedDev);

                if (!ShouldLogDeviceLocked(key, updatedDev, state, nowUtc))
                {
                    continue;
                }

                LastLoggedSnapshots[key] = new LoggedDeviceSnapshot(
                    updatedDev.IsConnected,
                    updatedDev.ChargingStatus,
                    updatedDev.BatteryPercent,
                    updatedDev.BatteryMillivolts,
                    nowUtc);

                if (updatedDev.BatteryPercent is int loggedPct && state.LastLoggedCsvPercent != loggedPct)
                {
                    state.LastLoggedCsvPercent = loggedPct;
                    storeDirty = true;
                }

                csvBuilder ??= new StringBuilder();
                if (writeHeader)
                {
                    csvBuilder.AppendLine("Timestamp,DeviceName,Kind,Connected,ChargingStatus,Millivolts,RawPercent,DisplayedPercent");
                    writeHeader = false;
                }

                int? rawPct = LastRawPercentByDevice.TryGetValue(key, out int r) ? r : updatedDev.BatteryPercent;
                string ts = nowLocal.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                string safeName = EscapeCsv(updatedDev.Name);
                string mvStr = updatedDev.BatteryMillivolts?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
                string rawStr = rawPct?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
                string dispStr = updatedDev.BatteryPercent?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

                csvBuilder.AppendLine($"{ts},{safeName},{updatedDev.Kind},{updatedDev.IsConnected},{updatedDev.ChargingStatus},{mvStr},{rawStr},{dispStr}");
            }

            if (storeDirty)
            {
                SaveStoreLocked(store);
            }

            if (csvBuilder is { Length: > 0 })
            {
                try
                {
                    Directory.CreateDirectory(DataDirectory);
                    File.AppendAllText(LogFilePath, csvBuilder.ToString(), Encoding.UTF8);
                }
                catch
                {
                    // Ignore transient I/O errors so logging never interrupts tray updates.
                }
            }

            return enriched;
        }
    }

    private static bool UpdateDischargeRateTrackingLocked(
        DeviceCalibrationState state,
        DeviceBatteryInfo dev,
        DateTime nowUtc)
    {
        if (!dev.IsConnected || dev.BatteryPercent is not int pct || pct <= 0)
        {
            state.LastActiveDischargePollUtc = null;
            return false;
        }

        if (dev.IsCharging || dev.IsChargingComplete)
        {
            bool changed = state.DischargeMilestonePercent != pct || state.LastActiveDischargePollUtc is not null;
            state.DischargeMilestonePercent = pct;
            state.DischargeMilestoneUtc = nowUtc;
            state.ActiveDischargeHoursSinceMilestone = 0;
            state.LastActiveDischargePollUtc = null;
            return changed;
        }

        bool isAdcDevice = dev.BatteryMillivolts.HasValue || dev.Kind == DeviceKind.Headset;
        if (isAdcDevice)
        {
            bool dirty = false;
            if (state.LastActiveDischargePollUtc is DateTime lastPoll)
            {
                double deltaHours = (nowUtc - lastPoll).TotalHours;
                if (deltaHours is > 0.0 and <= 0.25)
                {
                    state.ActiveDischargeHoursSinceMilestone += deltaHours;
                    dirty = true;
                }
            }

            state.LastActiveDischargePollUtc = nowUtc;

            if (state.DischargeMilestonePercent is null || pct > state.DischargeMilestonePercent.Value)
            {
                state.DischargeMilestonePercent = pct;
                state.DischargeMilestoneUtc = nowUtc;
                state.ActiveDischargeHoursSinceMilestone = 0;
                return true;
            }

            int drop = state.DischargeMilestonePercent.Value - pct;
            if (drop >= 3 && state.ActiveDischargeHoursSinceMilestone >= 0.4)
            {
                double measuredRate = Math.Clamp(drop / state.ActiveDischargeHoursSinceMilestone, 1.8, 6.5);
                double oldRate = state.LearnedDischargePercentPerHour ?? GetDefaultDischargeRatePerHour(dev.Kind);
                state.LearnedDischargePercentPerHour = Math.Round(oldRate * 0.65 + measuredRate * 0.35, 3);
                state.DischargeMilestonePercent = pct;
                state.DischargeMilestoneUtc = nowUtc;
                state.ActiveDischargeHoursSinceMilestone = 0;
                return true;
            }

            return dirty;
        }
        else
        {
            if (state.DischargeMilestonePercent is null || pct > state.DischargeMilestonePercent.Value)
            {
                state.DischargeMilestonePercent = pct;
                state.DischargeMilestoneUtc = nowUtc;
                return true;
            }

            int drop = state.DischargeMilestonePercent.Value - pct;
            if (drop >= 5 && state.DischargeMilestoneUtc is DateTime mileUtc)
            {
                double calendarHours = (nowUtc - mileUtc).TotalHours;
                if (calendarHours >= 6.0)
                {
                    (double minRate, double maxRate) = dev.Kind == DeviceKind.Mouse
                        ? (0.015, 0.25)
                        : (0.025, 0.65);
                    double measuredRate = Math.Clamp(drop / calendarHours, minRate, maxRate);
                    double oldRate = state.LearnedDischargePercentPerHour ?? GetDefaultDischargeRatePerHour(dev.Kind);
                    state.LearnedDischargePercentPerHour = Math.Round(oldRate * 0.5 + measuredRate * 0.5, 4);
                }

                state.DischargeMilestonePercent = pct;
                state.DischargeMilestoneUtc = nowUtc;
                return true;
            }

            return false;
        }
    }

    private static string? ComputeEstimatedTimeText(DeviceCalibrationState state, DeviceBatteryInfo dev)
    {
        if (!dev.IsConnected || dev.BatteryPercent is not int pct || dev.IsChargingComplete)
        {
            return null;
        }

        if (dev.IsCharging)
        {
            if (pct >= 99)
            {
                return "fast voll";
            }

            double minutesLeft;
            if (dev.Kind == DeviceKind.Headset || dev.BatteryMillivolts.HasValue)
            {
                minutesLeft = pct < 82
                    ? ((82 - pct) / 0.51) + (18.0 / 0.38)
                    : (100 - pct) / 0.38;
            }
            else
            {
                minutesLeft = (100 - pct) / 0.80;
            }

            if (minutesLeft < 60.0)
            {
                int mins = Math.Max(2, (int)Math.Round(minutesLeft));
                return $"voll in ~{mins} Min.";
            }

            int hours = (int)(minutesLeft / 60.0);
            int remMins = (int)Math.Round((minutesLeft % 60.0) / 5.0) * 5;
            if (remMins == 60)
            {
                hours++;
                remMins = 0;
            }

            return remMins > 0
                ? $"voll in ~{hours} Std. {remMins} Min."
                : $"voll in ~{hours} Std.";
        }

        if (pct <= 0)
        {
            return null;
        }

        double ratePerHour = state.LearnedDischargePercentPerHour ?? GetDefaultDischargeRatePerHour(dev.Kind);
        if (ratePerHour <= 0)
        {
            return null;
        }

        double hoursLeft = pct / ratePerHour;
        if (dev.Kind == DeviceKind.Headset || dev.BatteryMillivolts.HasValue || hoursLeft < 48.0)
        {
            if (hoursLeft < 1.0)
            {
                int mins = Math.Max(5, (int)Math.Round(hoursLeft * 60.0 / 5.0) * 5);
                return $"~{mins} Min. Rest";
            }

            if (hoursLeft < 10.0)
            {
                double roundedHalf = Math.Round(hoursLeft * 2.0) / 2.0;
                return $"~{roundedHalf.ToString("0.#", CultureInfo.CurrentCulture)} Std. Rest";
            }

            return $"~{(int)Math.Round(hoursLeft)} Std. Rest";
        }

        int days = Math.Max(1, (int)Math.Round(hoursLeft / 24.0));
        return days == 1 ? "~1 Tag Rest" : $"~{days} Tage Rest";
    }

    private static double GetDefaultDischargeRatePerHour(DeviceKind kind)
    {
        return kind switch
        {
            // PRO X Wireless: ~30 hours total active battery life (~3.35% per active hour)
            DeviceKind.Headset => 3.35,
            // MX Master 3S: ~70 calendar days (~1.44% per day = 0.060% per hour)
            DeviceKind.Mouse => 0.060,
            // MX KEYS S: ~15 calendar days typical with smart backlight (~6.7% per day = 0.28% per hour)
            DeviceKind.Keyboard => 0.280,
            _ => 1.0
        };
    }

    private static bool ShouldLogDeviceLocked(
        string key,
        DeviceBatteryInfo dev,
        DeviceCalibrationState state,
        DateTime nowUtc)
    {
        // Non-ADC devices (Mouse / Keyboard only report 5% steps): only log when their % actually changes.
        if (!dev.BatteryMillivolts.HasValue)
        {
            if (!dev.IsConnected || dev.BatteryPercent is not int pct)
            {
                return false;
            }

            return state.LastLoggedCsvPercent != pct;
        }

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
        if (chargingMv is < 3550 or > 4340 || truePercent is < 0 or > 100)
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
        public int FullRestingMillivolts { get; set; } = 4102;
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
        public int? PeakChargingMv { get; set; }

        public double? LearnedDischargePercentPerHour { get; set; }
        public int? DischargeMilestonePercent { get; set; }
        public DateTime? DischargeMilestoneUtc { get; set; }
        public double ActiveDischargeHoursSinceMilestone { get; set; }
        public DateTime? LastActiveDischargePollUtc { get; set; }
        public int? LastLoggedCsvPercent { get; set; }

        public List<LearnedVoltagePoint> LearnedChargingPoints { get; set; } = [];
    }

    internal sealed record LearnedVoltagePoint(int ChargingMv, int TruePercent, DateTime UpdatedUtc);
}
