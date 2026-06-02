namespace VibeSwitcher.ViewModels;

public class DeviceAliasItem : ViewModelBase
{
    private string _alias;

    public string DeviceId { get; }
    public string RawName  { get; }

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

    internal event Action<string, string>? AliasChanged;

    public DeviceAliasItem(string deviceId, string rawName, string alias)
    {
        DeviceId = deviceId;
        RawName  = rawName;
        _alias   = alias;
    }
}
