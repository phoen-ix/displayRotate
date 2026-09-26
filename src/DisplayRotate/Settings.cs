using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;
using static DisplayRotate.NativeMethods;

namespace DisplayRotate;

public class HotkeyBinding
{
    public string ActionId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int TargetDisplayIndex { get; set; }
    public int TargetOrientation { get; set; }
    public uint Modifiers { get; set; }
    public uint VirtualKey { get; set; }
    public bool IsEnabled { get; set; } = true;

    public string FormatHotkey()
    {
        if (VirtualKey == 0) return "(None)";
        var parts = new List<string>();
        if ((Modifiers & MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((Modifiers & MOD_ALT) != 0) parts.Add("Alt");
        if ((Modifiers & MOD_SHIFT) != 0) parts.Add("Shift");
        parts.Add(KeyName(VirtualKey));
        return string.Join("+", parts);
    }

    private static string KeyName(uint vk) => vk switch
    {
        VK_UP => "Up",
        VK_DOWN => "Down",
        VK_LEFT => "Left",
        VK_RIGHT => "Right",
        >= 0x30 and <= 0x39 => ((char)vk).ToString(),
        >= 0x41 and <= 0x5A => ((char)vk).ToString(),
        >= 0x70 and <= 0x87 => $"F{vk - 0x6F}",
        0x20 => "Space",
        0x2E => "Delete",
        0x2D => "Insert",
        0x24 => "Home",
        0x23 => "End",
        0x21 => "PageUp",
        0x22 => "PageDown",
        _ => $"0x{vk:X2}"
    };
}

public class AppSettings
{
    public bool StartWithWindows { get; set; }
    public int OverlayDurationMs { get; set; } = 2000;
    public bool ShowIdentifyButtons { get; set; } = true;
    public bool AutoRestoreOrientations { get; set; }
    public Dictionary<string, int> PreferredOrientations { get; set; } = new();
    public List<HotkeyBinding> Hotkeys { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string GetSettingsPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DisplayRotate", "settings.json");

    public static AppSettings Load()
    {
        var path = GetSettingsPath();
        if (!File.Exists(path))
            return CreateDefault();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? CreateDefault();
        }
        catch
        {
            return CreateDefault();
        }
    }

    public void Save()
    {
        var path = GetSettingsPath();
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(path, json);
    }

    public static AppSettings CreateDefault()
    {
        return new AppSettings
        {
            Hotkeys = new List<HotkeyBinding>
            {
                new() { ActionId = "rotate_primary_0", DisplayName = "Rotate Primary to 0° (Landscape)", TargetDisplayIndex = 0, TargetOrientation = DMDO_DEFAULT, Modifiers = MOD_CONTROL | MOD_ALT, VirtualKey = VK_UP, IsEnabled = true },
                new() { ActionId = "rotate_primary_90", DisplayName = "Rotate Primary to 90° (Portrait)", TargetDisplayIndex = 0, TargetOrientation = DMDO_90, Modifiers = MOD_CONTROL | MOD_ALT, VirtualKey = VK_RIGHT, IsEnabled = true },
                new() { ActionId = "rotate_primary_180", DisplayName = "Rotate Primary to 180° (Landscape flipped)", TargetDisplayIndex = 0, TargetOrientation = DMDO_180, Modifiers = MOD_CONTROL | MOD_ALT, VirtualKey = VK_DOWN, IsEnabled = true },
                new() { ActionId = "rotate_primary_270", DisplayName = "Rotate Primary to 270° (Portrait flipped)", TargetDisplayIndex = 0, TargetOrientation = DMDO_270, Modifiers = MOD_CONTROL | MOD_ALT, VirtualKey = VK_LEFT, IsEnabled = true },
            }
        };
    }

    public void ApplyStartWithWindows()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);
        if (key == null) return;

        if (StartWithWindows)
            key.SetValue("DisplayRotate", $"\"{Application.ExecutablePath}\"");
        else
            key.DeleteValue("DisplayRotate", throwOnMissingValue: false);
    }

    public void RestoreOrientations()
    {
        if (!AutoRestoreOrientations || PreferredOrientations.Count == 0)
            return;

        var displays = DisplayManager.GetDisplays();
        foreach (var (deviceName, orientation) in PreferredOrientations)
        {
            var display = displays.FirstOrDefault(d => d.DeviceName == deviceName);
            if (display != null && display.CurrentOrientation != orientation)
                DisplayManager.Rotate(deviceName, orientation);
        }
    }
}
