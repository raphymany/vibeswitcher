using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VibeSwitcher.Helpers;

namespace VibeSwitcher.Views;

public partial class IconGalleryDialog : Window
{
    private readonly string _libraryDir;

    // Built-in gallery items, each rendered live in the selected colour so the thumbnail
    // matches exactly what gets saved.
    public ObservableCollection<GalleryPreviewItem> PreviewItems { get; } = new();

    // The user's previously-uploaded custom .ico files.
    public ObservableCollection<LibraryIconItem> LibraryItems { get; } = new();
    public bool HasLibraryItems => LibraryItems.Count > 0;

    public GalleryItem? SelectedItem { get; private set; }
    public bool BrowseFromDisk { get; private set; }
    public string? CustomIconPath { get; private set; }   // set when a library icon is picked
    public IconColor SelectedColor =>
        ColorWhite.IsChecked == true ? IconColor.White : IconColor.Black;

    public IconGalleryDialog(string libraryDir)
    {
        InitializeComponent();
        _libraryDir = libraryDir;
        DataContext = this;

        foreach (var item in GalleryIconHelper.Items)
            PreviewItems.Add(new GalleryPreviewItem(item));
        RenderPreviews();
        LoadLibrary();
    }

    private void LoadLibrary()
    {
        LibraryItems.Clear();
        foreach (var path in UploadLibrary.List(_libraryDir, "*.ico"))
        {
            var img = TryLoadImage(path);
            if (img != null) LibraryItems.Add(new LibraryIconItem(path, img));
        }
        OnLibraryChanged();
    }

    private void OnLibraryChanged()
    {
        LibrarySection.Visibility = HasLibraryItems ? Visibility.Visible : Visibility.Collapsed;
    }

    private static ImageSource? TryLoadImage(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad; // load fully so the file isn't locked / can be deleted
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private void RenderPreviews()
    {
        // The glyph is rendered in the selected colour; the checkerboard tile behind it (defined in
        // XAML) stays fixed so both black and white glyphs are always visible.
        var color = SelectedColor;
        foreach (var p in PreviewItems)
            p.Preview = GalleryIconHelper.RenderGlyph(p.Item.Emoji, color);
    }

    private void Color_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized) return;
        RenderPreviews();
    }

    private void GalleryItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: GalleryPreviewItem item })
        {
            SelectedItem = item.Item;
            DialogResult = true;
        }
    }

    private void LibraryItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: LibraryIconItem item })
        {
            CustomIconPath = item.Path;
            DialogResult = true;
        }
    }

    private void DeleteLibraryItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: LibraryIconItem item })
        {
            UploadLibrary.Delete(item.Path);
            LibraryItems.Remove(item);
            OnLibraryChanged();
        }
        e.Handled = true;
    }

    private void BrowseFromDisk_Click(object sender, RoutedEventArgs e)
    {
        BrowseFromDisk = true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

// A built-in gallery item plus its live-rendered, colour-aware preview.
public sealed class GalleryPreviewItem : INotifyPropertyChanged
{
    public GalleryItem Item { get; }
    public string Label => Item.Label;

    public GalleryPreviewItem(GalleryItem item) => Item = item;

    private ImageSource? _preview;
    public ImageSource? Preview { get => _preview; set { _preview = value; Notify(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

// A previously-uploaded custom icon from the user's library.
public sealed class LibraryIconItem
{
    public string Path { get; }
    public ImageSource Preview { get; }
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);

    public LibraryIconItem(string path, ImageSource preview)
    {
        Path = path;
        Preview = preview;
    }
}
