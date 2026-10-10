# Regenerates the table in src/VibeSwitcher.Core/Keys/VirtualKeyMap.cs from WPF itself, so hotkey labels
# on macOS/Linux read exactly like the Windows app. Run on Windows (Windows PowerShell or pwsh):
#   powershell -File xplat/tools/gen-virtualkeymap.ps1
# and paste the printed entries into the dictionary initializer.
Add-Type -AssemblyName PresentationCore, WindowsBase
$entries = foreach ($vk in 1..254) {
    $key = [System.Windows.Input.KeyInterop]::KeyFromVirtualKey($vk)
    if ($key -ne [System.Windows.Input.Key]::None) { "[$vk] = `"$key`"," }
}
"// $($entries.Count) entries"
$entries -join ' '
