using VibeSwitcher.App.Controls;
using VibeSwitcher.App.Dialogs;
using VibeSwitcher.Helpers;
using VibeSwitcher.Tests;

namespace VibeSwitcher.App.Tests;

// Every dialog, with the content the Windows app shows in it (for tests and screenshots).
internal static class DialogCatalog
{
    public static readonly string[] Names =
        ["alert", "alert-info", "confirm", "confirm-delete", "conflict-retry", "error", "uninstall", "controls"];

    public static VsDialog Create(string name) => name switch
    {
        "alert" => new AlertDialog("Device Not Found", "The headset in this profile isn't connected. Plug it in, or pick another device."),
        "alert-info" => new AlertDialog("Mini Mode", "Mini Mode keeps your profiles one click away in a small window.", AlertKind.Info),
        "confirm" => new ConfirmDialog("Replace Configuration?", "This replaces all your profiles and settings with the imported ones.", "Replace", subtitle: ""),
        "confirm-delete" => new ConfirmDeleteDialog("Gaming"),
        "conflict-retry" => new ConflictRetryDialog("Hotkey Already in Use", "Ctrl + Alt + G is used by another app. Try a different shortcut?"),
        "error" => new ErrorDialog(ErrorCode.ProfileSwitchFailed, "Profile Switch Failed",
            "Couldn't switch to \"Gaming\": the playback device isn't available.", "/tmp/vs.log", new FakeAppLogger(), new FakeSessionErrorTracker()),
        "uninstall" => new UninstallDialog(UninstallText.Mac),
        "controls" => new DialogPreviewWindow(),
        _ => throw new ArgumentException(name),
    };

    public static IEnumerable<VsDialog> All() => Names.Select(Create);
}
