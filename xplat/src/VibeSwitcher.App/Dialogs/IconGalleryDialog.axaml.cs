using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using VibeSwitcher.App.Controls;
using VibeSwitcher.Helpers;

namespace VibeSwitcher.App.Dialogs;

// Port of Views/IconGalleryDialog.xaml.cs: a built-in emoji (drawn live in the chosen colour, so the
// thumbnail is exactly what gets saved), one of the user's earlier uploads, or "browse from disk".
public partial class IconGalleryDialog : VsDialog
{
    private readonly string _libraryDir;

    public ObservableCollection<GalleryPreviewItem> PreviewItems { get; } = new();

    // The user's previously-uploaded custom .ico files.
    public ObservableCollection<LibraryIconItem> LibraryItems { get; } = new();
    public bool HasLibraryItems => LibraryItems.Count > 0;

    public GalleryItem? SelectedItem { get; private set; }
    public bool BrowseFromDisk { get; private set; }
    public string? CustomIconPath { get; private set; } // set when a library icon is picked
    public IconColor SelectedColor =>
        this.FindControl<RadioButton>("ColorWhite")?.IsChecked == true ? IconColor.White : IconColor.Black;

    private readonly bool _loaded;

    public IconGalleryDialog() : this("") { } // (for the XAML loader)

    public IconGalleryDialog(string libraryDir)
    {
        AvaloniaXamlLoader.Load(this);
        _libraryDir = libraryDir;
        DataContext = this;

        foreach (var item in GalleryIconHelper.Items)
            PreviewItems.Add(new GalleryPreviewItem(item));
        _loaded = true;
        RenderPreviews();
        LoadLibrary();
    }

    private void LoadLibrary()
    {
        LibraryItems.Clear();
        if (_libraryDir.Length > 0)
        {
            foreach (var path in UploadLibrary.List(_libraryDir, "*.ico"))
            {
                var img = TryLoadImage(path);
                if (img != null) LibraryItems.Add(new LibraryIconItem(path, img));
            }
        }
        OnLibraryChanged();
    }

    private void OnLibraryChanged() => this.FindControl<StackPanel>("LibrarySection")!.IsVisible = HasLibraryItems;

    // Loaded fully into memory, so the file isn't held open and can be deleted.
    private static Bitmap? TryLoadImage(string path)
    {
        try
        {
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    // The glyph is drawn in the selected colour; the checkerboard behind it stays, so both black and
    // white glyphs are always visible.
    private void RenderPreviews()
    {
        var color = SelectedColor;
        foreach (var p in PreviewItems)
        {
            using var png = new MemoryStream(GalleryIconHelper.RenderGlyphPng(p.Item.Emoji, color));
            var old = p.Preview;
            p.Preview = new Bitmap(png);
            old?.Dispose();
        }
    }

    private void Color_Changed(object? sender, RoutedEventArgs e)
    {
        // One redraw per change, once the dialog is built: on the White pill's change, which comes
        // both ways and with its state final (Avalonia unchecks the old pill after the new one
        // reports, so on Black's own change White would still read as checked).
        if (!_loaded || (sender as Control)?.Name != "ColorWhite") return;
        RenderPreviews();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        foreach (var p in PreviewItems) p.Preview?.Dispose();
        foreach (var item in LibraryItems) item.Preview.Dispose();
    }

    private void GalleryItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: GalleryPreviewItem item })
        {
            SelectedItem = item.Item;
            CloseWith(true);
        }
    }

    private void LibraryItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: LibraryIconItem item })
        {
            CustomIconPath = item.Path;
            CloseWith(true);
        }
    }

    private void DeleteLibraryItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: LibraryIconItem item })
        {
            UploadLibrary.Delete(item.Path);
            LibraryItems.Remove(item);
            item.Preview.Dispose();
            OnLibraryChanged();
        }
        e.Handled = true;
    }

    private void BrowseFromDisk_Click(object? sender, RoutedEventArgs e)
    {
        BrowseFromDisk = true;
        CloseWith(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => CloseWith(false);
}

// A built-in gallery item plus its live-drawn, colour-aware preview.
public sealed class GalleryPreviewItem : INotifyPropertyChanged
{
    public GalleryItem Item { get; }
    public string Label => Item.Label;

    public GalleryPreviewItem(GalleryItem item) => Item = item;

    private Bitmap? _preview;
    public Bitmap? Preview { get => _preview; set { _preview = value; Notify(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

// A previously-uploaded custom icon from the user's library.
public sealed class LibraryIconItem(string path, Bitmap preview)
{
    public string Path { get; } = path;
    public Bitmap Preview { get; } = preview;
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
}
