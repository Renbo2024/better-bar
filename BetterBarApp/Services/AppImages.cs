using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BetterBarApp.Services;

/// <summary>The BetterBar application icon (app.ico) as a shared, frozen image source, plus a factory
/// for the per-use <see cref="Image"/> elements menus need (a single Image can't live in two menus).</summary>
public static class AppImages
{
    /// <summary>The app icon, loaded once and frozen. Usable as a Window.Icon or anywhere an ImageSource fits.</summary>
    public static ImageSource Icon { get; } = Load();

    private static ImageSource Load()
    {
        var img = new BitmapImage();
        img.BeginInit();
        img.UriSource   = new Uri("pack://application:,,,/app.ico");
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.EndInit();
        img.Freeze();
        return img;
    }

    /// <summary>A fresh <see cref="Image"/> of the app icon at the given size (for MenuItem.Icon etc.).</summary>
    public static Image NewIcon(double size = 16) => new() { Source = Icon, Width = size, Height = size };
}
