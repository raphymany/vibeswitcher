using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using VibeSwitcher.App.Controls;
using VibeSwitcher.Helpers;
using VibeSwitcher.Platform.Linux.Hid;

namespace VibeSwitcher.App.Dialogs;

// Linux: wireless-headset detection reads the headset's USB receiver, which Linux lets only
// administrators open until a permission rule (LinuxHidAccess) is installed. Shown when auto-switch is
// turned on and a supported receiver is connected but can't be opened:
//   NeedsRule       explains, and "Allow Access" installs the rule (one password prompt, off the UI
//                   thread), then checks again
//   NeedsReconnect  the rule is there: unplug and replug the receiver
//   TurnedOff       the user switched the rule off; VibeSwitcher leaves that alone
public partial class HeadsetAccessDialog : VsDialog
{
    private readonly Func<RuleInstallResult> _install;
    private readonly Func<HeadsetAccess> _check;
    private bool _installing;

    public HeadsetAccessDialog() : this(HeadsetAccess.NeedsRule, () => RuleInstallResult.Cancelled, () => HeadsetAccess.NeedsRule) { } // (for the XAML loader)

    public HeadsetAccessDialog(HeadsetAccess state, Func<RuleInstallResult> install, Func<HeadsetAccess> check)
    {
        AvaloniaXamlLoader.Load(this);
        _install = install;
        _check = check;
        ShowState(state);
        KeyDown += (_, e) =>
        {
            // (Closing while the password prompt is up is fine: the result is then just dropped.)
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        };
    }

    private T Named<T>(string name) where T : Control => this.FindControl<T>(name)!;

    // What the dialog is showing now (for tests): the state, and the last install's result.
    internal HeadsetAccess State { get; private set; }
    internal RuleInstallResult? LastInstall { get; private set; }

    private const string NeedsRuleReason =
        "Your wireless headset's USB receiver is connected, but Linux only lets administrators read it, so VibeSwitcher can't tell when the headset turns on or off.";

    private void ShowState(HeadsetAccess state)
    {
        State = state;
        switch (state)
        {
            case HeadsetAccess.NeedsRule:
                Named<TextBlock>("Subtitle").Text = "Linux needs your permission first";
                Named<TextBlock>("Message").Text =
                    NeedsRuleReason + " "
                    + "Allowing access adds a system rule that covers only the supported headsets' receivers — never keyboards, mice or anything else. You'll be asked for your password once.";
                AskMode();
                break;
            case HeadsetAccess.NeedsReconnect:
                Named<TextBlock>("Subtitle").Text = "One more step";
                Named<TextBlock>("Message").Text =
                    "Access is set up. Unplug the headset's USB receiver and plug it back in (or log out and back in) so Linux applies it.";
                OkMode();
                break;
            case HeadsetAccess.TurnedOff:
                Named<TextBlock>("Subtitle").Text = "Turned off on this computer";
                Named<TextBlock>("Message").Text =
                    $"Headset access has been turned off on this computer (an empty {LinuxHidAccess.RuleFileName} udev rule), so VibeSwitcher leaves it alone. "
                    + "Remove that file from /etc/udev/rules.d to turn headset detection back on.";
                Named<TextBlock>("Note").IsVisible = false;
                OkMode();
                break;
            default: // Accessible / NoHeadset: nothing more to do here
                Named<TextBlock>("Subtitle").Text = "Ready";
                Named<TextBlock>("Message").Text = state == HeadsetAccess.Accessible
                    ? "VibeSwitcher can see your headset's receiver: it will notice when the headset turns on and off."
                    : "Headset detection will work as soon as the headset's USB receiver is plugged in.";
                OkMode();
                break;
        }
    }

    private void AskMode()
    {
        Named<Grid>("AskButtons").IsVisible = true;
        Named<Button>("AllowBtn").IsDefault = true;
        var ok = Named<Button>("OkBtn");
        ok.IsVisible = false;
        ok.IsDefault = false;
    }

    private void OkMode()
    {
        Named<Grid>("AskButtons").IsVisible = false;
        Named<Button>("AllowBtn").IsDefault = false;
        var ok = Named<Button>("OkBtn");
        ok.IsVisible = true;
        ok.IsDefault = true;
    }

    private void SetStatus(string? text)
    {
        var status = Named<TextBlock>("Status");
        status.Text = text;
        status.IsVisible = text != null;
    }

    private async void Allow_Click(object? sender, RoutedEventArgs e)
    {
        if (_installing) return;
        var allow = Named<Button>("AllowBtn");
        _installing = true;
        allow.IsEnabled = false;
        allow.Content = "Waiting for permission…";
        SetStatus(null);
        try
        {
            var result = await Task.Run(_install);
            LastInstall = result;
            if (!IsVisible) return;
            switch (result)
            {
                case RuleInstallResult.Installed:
                    var now = await Task.Run(_check);
                    if (!IsVisible) return;
                    ShowState(now == HeadsetAccess.NeedsRule ? HeadsetAccess.NeedsReconnect : now);
                    if (now == HeadsetAccess.Accessible) SetStatus("Done — headset detection is on.");
                    break;
                case RuleInstallResult.Cancelled:
                    SetStatus("Permission wasn't given. You can try again any time.");
                    break;
                case RuleInstallResult.NoPrivilegeTool:
                    Named<TextBlock>("Message").Text = NeedsRuleReason;
                    SetStatus("This system has no way to ask for an administrator's password (pkexec isn't installed). Installing VibeSwitcher's .deb package sets this up instead.");
                    OkMode();
                    break;
                case RuleInstallResult.TurnedOffByUser:
                    ShowState(HeadsetAccess.TurnedOff);
                    break;
                default:
                    AppLog.Warning("HeadsetAccessDialog", "The headset permission rule couldn't be installed.");
                    SetStatus("Couldn't add the rule. Details are in the log.");
                    break;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warning("HeadsetAccessDialog", ex.Message);
            if (IsVisible) SetStatus("Couldn't add the rule. Details are in the log.");
        }
        finally
        {
            _installing = false;
            allow.IsEnabled = true;
            allow.Content = "Allow Access";
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => CloseWith(true);
}
