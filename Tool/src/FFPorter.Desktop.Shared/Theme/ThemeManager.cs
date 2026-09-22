using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace FFPorter.Desktop.Theme;

public static class ThemeManager
{
    private static readonly string Component = $"/{typeof(ThemeManager).Assembly.GetName().Name};component/Theme/";

    public static void Initialize(Application application)
    {
        application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(Component + "Palette.Dark.xaml", UriKind.Relative) });
        application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(Component + "Controls.xaml", UriKind.Relative) });
    }

    public static void StyleTitleBar(Window window)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            window.SourceInitialized -= OnSourceInitialized;
            window.SourceInitialized += OnSourceInitialized;
            return;
        }
        int dark = 1;
        if (DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref dark, sizeof(int)) != 0)
            DwmSetWindowAttribute(handle, UseImmersiveDarkModeBefore20H1, ref dark, sizeof(int));
        if (Application.Current.TryFindResource("Color.Caption") is Color caption)
        {
            int colour = caption.R | caption.G << 8 | caption.B << 16;
            DwmSetWindowAttribute(handle, CaptionColor, ref colour, sizeof(int));
            DwmSetWindowAttribute(handle, BorderColor, ref colour, sizeof(int));
        }
        if (Application.Current.TryFindResource("Color.CaptionText") is Color text)
        {
            int colour = text.R | text.G << 8 | text.B << 16;
            DwmSetWindowAttribute(handle, TextColor, ref colour, sizeof(int));
        }
    }

    private static void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            window.SourceInitialized -= OnSourceInitialized;
            StyleTitleBar(window);
        }
    }

    private const int UseImmersiveDarkModeBefore20H1 = 19, UseImmersiveDarkMode = 20, BorderColor = 34, CaptionColor = 35, TextColor = 36;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
