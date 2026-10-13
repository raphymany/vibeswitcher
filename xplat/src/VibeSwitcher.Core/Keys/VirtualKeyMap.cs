using System.Collections.Frozen;

namespace VibeSwitcher.Keys;

// Windows virtual-key code → key name, exactly as the Windows app displays it
// (WPF KeyInterop.KeyFromVirtualKey(vk).ToString()). Generated once from WPF on Windows so hotkey
// labels read identically on every OS — including WPF's own quirks such as "Next" for Page Down,
// "D1" for the 1 key and "Oem1" for ';'. Codes WPF doesn't name return null ("VK(n)" fallback).
// Regenerate with xplat/tools/gen-virtualkeymap.ps1 (Windows).
public static class VirtualKeyMap
{
    private static readonly FrozenDictionary<int, string> Names = new Dictionary<int, string>
    {
        [3] = "Cancel", [8] = "Back", [9] = "Tab", [12] = "Clear", [13] = "Return",
        [16] = "LeftShift", [17] = "LeftCtrl", [18] = "LeftAlt", [19] = "Pause", [20] = "Capital",
        [21] = "KanaMode", [23] = "JunjaMode", [24] = "FinalMode", [25] = "HanjaMode", [27] = "Escape",
        [28] = "ImeConvert", [29] = "ImeNonConvert", [30] = "ImeAccept", [31] = "ImeModeChange",
        [32] = "Space", [33] = "PageUp", [34] = "Next", [35] = "End", [36] = "Home",
        [37] = "Left", [38] = "Up", [39] = "Right", [40] = "Down", [41] = "Select", [42] = "Print",
        [43] = "Execute", [44] = "Snapshot", [45] = "Insert", [46] = "Delete", [47] = "Help",
        [48] = "D0", [49] = "D1", [50] = "D2", [51] = "D3", [52] = "D4",
        [53] = "D5", [54] = "D6", [55] = "D7", [56] = "D8", [57] = "D9",
        [65] = "A", [66] = "B", [67] = "C", [68] = "D", [69] = "E", [70] = "F", [71] = "G",
        [72] = "H", [73] = "I", [74] = "J", [75] = "K", [76] = "L", [77] = "M", [78] = "N",
        [79] = "O", [80] = "P", [81] = "Q", [82] = "R", [83] = "S", [84] = "T", [85] = "U",
        [86] = "V", [87] = "W", [88] = "X", [89] = "Y", [90] = "Z",
        [91] = "LWin", [92] = "RWin", [93] = "Apps", [95] = "Sleep",
        [96] = "NumPad0", [97] = "NumPad1", [98] = "NumPad2", [99] = "NumPad3", [100] = "NumPad4",
        [101] = "NumPad5", [102] = "NumPad6", [103] = "NumPad7", [104] = "NumPad8", [105] = "NumPad9",
        [106] = "Multiply", [107] = "Add", [108] = "Separator", [109] = "Subtract",
        [110] = "Decimal", [111] = "Divide",
        [112] = "F1", [113] = "F2", [114] = "F3", [115] = "F4", [116] = "F5", [117] = "F6",
        [118] = "F7", [119] = "F8", [120] = "F9", [121] = "F10", [122] = "F11", [123] = "F12",
        [124] = "F13", [125] = "F14", [126] = "F15", [127] = "F16", [128] = "F17", [129] = "F18",
        [130] = "F19", [131] = "F20", [132] = "F21", [133] = "F22", [134] = "F23", [135] = "F24",
        [144] = "NumLock", [145] = "Scroll",
        [160] = "LeftShift", [161] = "RightShift", [162] = "LeftCtrl", [163] = "RightCtrl",
        [164] = "LeftAlt", [165] = "RightAlt",
        [166] = "BrowserBack", [167] = "BrowserForward", [168] = "BrowserRefresh", [169] = "BrowserStop",
        [170] = "BrowserSearch", [171] = "BrowserFavorites", [172] = "BrowserHome",
        [173] = "VolumeMute", [174] = "VolumeDown", [175] = "VolumeUp",
        [176] = "MediaNextTrack", [177] = "MediaPreviousTrack", [178] = "MediaStop", [179] = "MediaPlayPause",
        [180] = "LaunchMail", [181] = "SelectMedia", [182] = "LaunchApplication1", [183] = "LaunchApplication2",
        [186] = "Oem1", [187] = "OemPlus", [188] = "OemComma", [189] = "OemMinus", [190] = "OemPeriod",
        [191] = "OemQuestion", [192] = "Oem3", [193] = "AbntC1", [194] = "AbntC2",
        [219] = "OemOpenBrackets", [220] = "Oem5", [221] = "Oem6", [222] = "OemQuotes", [223] = "Oem8",
        [226] = "OemBackslash", [229] = "ImeProcessed",
        [240] = "OemAttn", [241] = "OemFinish", [242] = "OemCopy", [243] = "DbeSbcsChar", [244] = "OemEnlw",
        [245] = "OemBackTab", [246] = "DbeNoRoman", [247] = "DbeEnterWordRegisterMode",
        [248] = "DbeEnterImeConfigureMode", [249] = "EraseEof", [250] = "Play", [251] = "DbeNoCodeInput",
        [252] = "NoName", [253] = "Pa1", [254] = "OemClear",
    }.ToFrozenDictionary();

    public static string? GetName(int virtualKeyCode) =>
        Names.TryGetValue(virtualKeyCode, out var name) ? name : null;

    public static int Count => Names.Count;
}
