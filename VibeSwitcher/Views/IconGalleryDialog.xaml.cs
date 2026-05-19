using System.Windows;
using VibeSwitcher.Helpers;

namespace VibeSwitcher.Views;

public partial class IconGalleryDialog : Window
{
    public IReadOnlyList<GalleryItem> Items => GalleryIconHelper.Items;

    public GalleryItem? SelectedItem { get; private set; }
    public bool BrowseFromDisk { get; private set; }
    public IconColor SelectedColor =>
        ColorBlack.IsChecked == true ? IconColor.Black :
        ColorWhite.IsChecked == true ? IconColor.White :
        IconColor.Auto;

    public IconGalleryDialog()
    {
        InitializeComponent();
        DataContext = this;
    }

    private void GalleryItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: GalleryItem item })
        {
            SelectedItem = item;
            DialogResult = true;
        }
    }

    private void BrowseFromDisk_Click(object sender, RoutedEventArgs e)
    {
        BrowseFromDisk = true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
