using System.Runtime.InteropServices;
using static DisplayRotate.NativeMethods;

namespace DisplayRotate;

public record DisplayInfo(
    int Index,
    string DeviceName,
    string FriendlyName,
    bool IsPrimary,
    int CurrentOrientation,
    int Width,
    int Height,
    int Frequency);

internal static class DisplayManager
{
    public static List<DisplayInfo> GetDisplays()
    {
        var displays = new List<DisplayInfo>();
        var dd = new DISPLAY_DEVICE();
        dd.cb = Marshal.SizeOf<DISPLAY_DEVICE>();
        uint index = 0;
        int displayNum = 1;

        while (EnumDisplayDevices(null, index, ref dd, 0))
        {
            index++;

            if ((dd.StateFlags & DISPLAY_DEVICE_ACTIVE) == 0)
                continue;

            bool isPrimary = (dd.StateFlags & DISPLAY_DEVICE_PRIMARY_DEVICE) != 0;
            string deviceName = dd.DeviceName;

            // Get the monitor friendly name
            var monitor = new DISPLAY_DEVICE();
            monitor.cb = Marshal.SizeOf<DISPLAY_DEVICE>();
            string friendlyName = dd.DeviceString;
            if (EnumDisplayDevices(deviceName, 0, ref monitor, 0))
                friendlyName = monitor.DeviceString;

            // Get current display settings
            var dm = new DEVMODE();
            dm.dmSize = (short)Marshal.SizeOf<DEVMODE>();
            if (!EnumDisplaySettingsEx(deviceName, ENUM_CURRENT_SETTINGS, ref dm, 0))
                continue;

            displays.Add(new DisplayInfo(
                displayNum++,
                deviceName,
                friendlyName,
                isPrimary,
                dm.dmDisplayOrientation,
                dm.dmPelsWidth,
                dm.dmPelsHeight,
                dm.dmDisplayFrequency));
        }

        return displays;
    }

    public static (bool Success, string Message) Rotate(string deviceName, int targetOrientation)
    {
        var dm = new DEVMODE();
        dm.dmSize = (short)Marshal.SizeOf<DEVMODE>();

        if (!EnumDisplaySettingsEx(deviceName, ENUM_CURRENT_SETTINGS, ref dm, 0))
            return (false, "Failed to get current display settings.");

        int currentOrientation = dm.dmDisplayOrientation;

        if (currentOrientation == targetOrientation)
            return (true, $"Display is already at {GetOrientationName(targetOrientation)}.");

        // Swap width/height when crossing between landscape and portrait
        bool currentIsPortrait = currentOrientation is DMDO_90 or DMDO_270;
        bool targetIsPortrait = targetOrientation is DMDO_90 or DMDO_270;

        if (currentIsPortrait != targetIsPortrait)
            (dm.dmPelsWidth, dm.dmPelsHeight) = (dm.dmPelsHeight, dm.dmPelsWidth);

        dm.dmDisplayOrientation = targetOrientation;
        dm.dmFields = DM_DISPLAYORIENTATION | DM_PELSWIDTH | DM_PELSHEIGHT;

        // Test if mode is valid
        int testResult = ChangeDisplaySettingsEx(deviceName, ref dm, IntPtr.Zero, CDS_TEST, IntPtr.Zero);
        if (testResult != DISP_CHANGE_SUCCESSFUL)
            return (false, $"Display does not support {GetOrientationName(targetOrientation)} (error: {testResult}).");

        // Apply the change
        int result = ChangeDisplaySettingsEx(deviceName, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);

        return result switch
        {
            DISP_CHANGE_SUCCESSFUL => (true, $"Rotated to {GetOrientationName(targetOrientation)}."),
            DISP_CHANGE_RESTART => (true, "Rotation applied. A restart may be required."),
            DISP_CHANGE_BADMODE => (false, "The requested orientation is not supported by this display."),
            _ => (false, $"Rotation failed (error code: {result}).")
        };
    }

    public static string GetOrientationName(int orientation) => orientation switch
    {
        DMDO_DEFAULT => "0° (Landscape)",
        DMDO_90 => "90° (Portrait)",
        DMDO_180 => "180° (Landscape flipped)",
        DMDO_270 => "270° (Portrait flipped)",
        _ => $"Unknown ({orientation})"
    };

    public static int? ParseAngle(string angle) => angle switch
    {
        "0" => DMDO_DEFAULT,
        "90" => DMDO_90,
        "180" => DMDO_180,
        "270" => DMDO_270,
        _ => null
    };
}
