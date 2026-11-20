using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LogiBatteryTray.Services;

/// <summary>
/// Win32 P/Invoke declarations for Configuration Manager, HID/SetupAPI, DPI, DWM, and messaging.
/// </summary>
internal static class NativeMethods
{
    internal static readonly Guid GUID_DEVINTERFACE_HID = new("4D1E55B2-F16F-11CF-88CB-001111000030");
    internal const uint DIGCF_PRESENT = 0x00000002;
    internal const uint DIGCF_DEVICEINTERFACE = 0x00000010;

    internal const uint GENERIC_READ = 0x80000000;
    internal const uint GENERIC_WRITE = 0x40000000;
    internal const uint FILE_SHARE_READ = 0x00000001;
    internal const uint FILE_SHARE_WRITE = 0x00000002;
    internal const uint OPEN_EXISTING = 3;
    internal const uint FILE_FLAG_OVERLAPPED = 0x40000000;

    internal const uint CR_SUCCESS = 0x00000000;
    internal const uint CR_BUFFER_SMALL = 0x0000001A;
    internal const uint CM_GETIDLIST_FILTER_ENUMERATOR = 0x00000001;
    internal const uint CM_LOCATE_DEVNODE_PHANTOM = 0x00000001;

    internal const int SM_CXSMICON = 49;
    internal const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    internal const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    internal const int DWMWCP_ROUND = 2;

    internal const int ATTACH_PARENT_PROCESS = -1;
    internal static readonly IntPtr HWND_BROADCAST = new(0xFFFF);

    internal const int WM_DEVICECHANGE = 0x0219;
    internal const int WM_SETTINGCHANGE = 0x001A;

    [StructLayout(LayoutKind.Sequential)]
    internal struct DEVPROPKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    internal static extern uint CM_Get_Device_ID_List_SizeW(
        out uint pulLen,
        string? pszFilter,
        uint ulFlags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    internal static extern uint CM_Get_Device_ID_ListW(
        string? pszFilter,
        char[] Buffer,
        uint BufferLen,
        uint ulFlags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    internal static extern uint CM_Locate_DevNodeW(
        out uint pdnDevInst,
        string pDeviceID,
        uint ulFlags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    internal static extern uint CM_Get_DevNode_PropertyW(
        uint dnDevInst,
        ref DEVPROPKEY PropertyKey,
        out uint PropertyType,
        byte[]? PropertyBuffer,
        ref uint PropertyBufferSize,
        uint ulFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    internal static extern int GetSystemMetricsForDpi(int nIndex, uint dpi);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern uint RegisterWindowMessageW(string lpString);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessageW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attr,
        ref int attrValue,
        int attrSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AttachConsole(int dwProcessId);

    [StructLayout(LayoutKind.Sequential)]
    internal struct SP_DEVICE_INTERFACE_DATA
    {
        public uint cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr SetupDiGetClassDevsW(
        ref Guid ClassGuid,
        IntPtr Enumerator,
        IntPtr hwndParent,
        uint Flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr DeviceInfoSet,
        IntPtr DeviceInfoData,
        ref Guid InterfaceClassGuid,
        uint MemberIndex,
        ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiGetDeviceInterfaceDetailW(
        IntPtr DeviceInfoSet,
        ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData,
        IntPtr DeviceInterfaceDetailData,
        uint DeviceInterfaceDetailDataSize,
        out uint RequiredSize,
        IntPtr DeviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool HidD_GetPreparsedData(
        SafeFileHandle HidDeviceObject,
        out IntPtr PreparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool HidD_FreePreparsedData(IntPtr PreparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    internal static extern int HidP_GetCaps(
        IntPtr PreparsedData,
        out HIDP_CAPS Capabilities);

    [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool HidD_GetProductString(
        SafeFileHandle HidDeviceObject,
        byte[] Buffer,
        uint BufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);
}
