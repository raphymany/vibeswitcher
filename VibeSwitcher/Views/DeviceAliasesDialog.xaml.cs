using System.Collections.Generic;
using System.Windows;
using VibeSwitcher.ViewModels;

namespace VibeSwitcher.Views;

public partial class DeviceAliasesDialog : Window
{
    public IEnumerable<DeviceAliasItem> DeviceAliases { get; }
    public bool HasKnownDevices { get; }

    public DeviceAliasesDialog(IEnumerable<DeviceAliasItem> aliases)
    {
        var list = new List<DeviceAliasItem>(aliases);
        DeviceAliases = list;
        HasKnownDevices = list.Count > 0;
        InitializeComponent();
        DataContext = this;
    }

    private void Done_Click(object sender, RoutedEventArgs e) => Close();
}
