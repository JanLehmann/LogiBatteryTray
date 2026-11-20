using LogiBatteryTray.Models;
using LogiBatteryTray.Rendering;
using LogiBatteryTray.Services;
using LogiBatteryTray.UI;

namespace LogiBatteryTray;

internal static class Program
{
    private const string SingleInstanceMutexName = @"Local\LogiBatteryTray_SingleInstance_v1";
    private const string ShowFlyoutMessageName = "LogiBatteryTray_ShowFlyout_v1";

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0)
        {
            if (string.Equals(args[0], "--dump", StringComparison.OrdinalIgnoreCase))
            {
                AttachParentConsoleIfNeeded();
                RunDump();
                return 0;
            }

            if (string.Equals(args[0], "--export-icons", StringComparison.OrdinalIgnoreCase))
            {
                AttachParentConsoleIfNeeded();
                string dir = args.Length > 1 ? args[1] : Path.Combine(AppContext.BaseDirectory, "preview");
                string path = TrayIconRenderer.ExportPreviewSheet(dir);
                Console.WriteLine(path);
                return 0;
            }
        }

        uint showFlyoutMsg = NativeMethods.RegisterWindowMessageW(ShowFlyoutMessageName);
        using var mutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            if (showFlyoutMsg != 0)
            {
                NativeMethods.PostMessageW(NativeMethods.HWND_BROADCAST, showFlyoutMsg, IntPtr.Zero, IntPtr.Zero);
            }

            return 0;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext(showFlyoutMsg));
        return 0;
    }

    private static void RunDump()
    {
        IReadOnlyList<DeviceBatteryInfo> devices = BluetoothBatteryReader.ReadLogitechDevices();
        if (devices.Count == 0)
        {
            Console.WriteLine("No Logitech Bluetooth LE devices found.");
            return;
        }

        foreach (DeviceBatteryInfo dev in devices)
        {
            string pct = dev.BatteryPercent is int p ? $"{p}%" : "null";
            string ts = dev.LastUpdated is DateTime dt ? dt.ToString("yyyy-MM-dd HH:mm:ss") : "null";
            string mv = dev.BatteryMillivolts is int v && v > 0 ? $" | Voltage={v}mV" : string.Empty;
            string est = !string.IsNullOrEmpty(dev.EstimatedTimeText) ? $" | Remaining={dev.EstimatedTimeText}" : string.Empty;
            Console.WriteLine($"{dev.Name} | Kind={dev.Kind} | Battery={pct}{mv}{est} | Connected={dev.IsConnected} | Charging={dev.ChargingStatus} | Updated={ts} | Id={dev.InstanceId}");
        }
    }

    private static void AttachParentConsoleIfNeeded()
    {
        if (!Console.IsOutputRedirected)
        {
            NativeMethods.AttachConsole(NativeMethods.ATTACH_PARENT_PROCESS);
        }
    }
}
