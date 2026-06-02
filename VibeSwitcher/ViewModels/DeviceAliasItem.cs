namespace VibeSwitcher.ViewModels;

public class DeviceAliasItem : ViewModelBase
{
    private string _alias;

    public string DeviceId   { get; }
    public string RawName    { get; }
    public bool   IsPlayback { get; }
    public bool   IsConnected  { get; }
    public bool   IsDisabled   { get; }
    public string ProfileUsage { get; }

    public string Alias
    {
        get => _alias;
        set
        {
            var trimmed = value?.Trim() ?? "";
            if (SetField(ref _alias, trimmed))
                AliasChanged?.Invoke(DeviceId, trimmed);
        }
    }

    public bool HasProfileUsage => !string.IsNullOrEmpty(ProfileUsage);
    public bool IsUnavailable   => !IsConnected || IsDisabled;

    internal event Action<string, string>? AliasChanged;

    public DeviceAliasItem(
        string deviceId,
        string rawName,
        string alias,
        bool   isPlayback,
        bool   isConnected,
        bool   isDisabled,
        string profileUsage)
    {
        DeviceId     = deviceId;
        RawName      = rawName;
        _alias       = alias;
        IsPlayback   = isPlayback;
        IsConnected  = isConnected;
        IsDisabled   = isDisabled;
        ProfileUsage = profileUsage;
    }
}
