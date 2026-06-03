namespace VibeSwitcher.ViewModels;

public class DayChip : ViewModelBase
{
    public DayOfWeek Day  { get; }
    public string    Label { get; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public DayChip(DayOfWeek day, string label)
    {
        Day   = day;
        Label = label;
    }
}
