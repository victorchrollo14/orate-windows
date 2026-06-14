namespace Orate.Services;

/// <summary>Friendly display names for the virtual-key codes we care about for push-to-talk.</summary>
public static class VirtualKeys
{
    public const int RightAlt = 0xA5;   // VK_RMENU — default, the closest analog to macOS Right Option
    public const int LeftAlt = 0xA4;
    public const int RightControl = 0xA3;
    public const int LeftControl = 0xA2;
    public const int RightShift = 0xA1;
    public const int LeftShift = 0xA0;
    public const int CapsLock = 0x14;

    public static string DisplayName(int vk) => vk switch
    {
        RightAlt => "Right Alt",
        LeftAlt => "Left Alt",
        RightControl => "Right Ctrl",
        LeftControl => "Left Ctrl",
        RightShift => "Right Shift",
        LeftShift => "Left Shift",
        CapsLock => "Caps Lock",
        0x5B => "Left Win",
        0x5C => "Right Win",
        0x70 => "F1", 0x71 => "F2", 0x72 => "F3", 0x73 => "F4",
        0x74 => "F5", 0x75 => "F6", 0x76 => "F7", 0x77 => "F8",
        0x78 => "F9", 0x79 => "F10", 0x7A => "F11", 0x7B => "F12",
        >= 0x41 and <= 0x5A => ((char)vk).ToString(), // A–Z
        _ => $"Key 0x{vk:X2}",
    };
}
