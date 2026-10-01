using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using FFPorter.Desktop.Theme;

namespace FFPorter.Desktop;

public enum DialogKind
{
    Info,
    Question,
    Warning,
    Error,
}

public partial class MessageDialog : Window
{
    private MessageDialog(Window? owner, DialogKind kind, string heading, string body, string confirm, string? cancel, bool danger)
    {
        InitializeComponent();
        ThemeManager.StyleTitleBar(this);
        Title = Edition.Current.Title;
        if (owner != null && new WindowInteropHelper(owner).Handle != IntPtr.Zero)
        {
            Owner = owner;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = true;
        }
        (string glyph, string colour) = kind switch
        {
            DialogKind.Warning => ("", "Status.Warning"),
            DialogKind.Error => ("", "Status.Error"),
            DialogKind.Question => ("", "Text.Primary"),
            _ => ("", "Text.Primary"),
        };
        IconText.Text = glyph;
        IconText.SetResourceReference(TextBlock.ForegroundProperty, colour);
        HeadingText.Text = heading;
        BodyText.Text = body;
        BodyScroll.Visibility = string.IsNullOrWhiteSpace(body) ? Visibility.Collapsed : Visibility.Visible;
        ConfirmButton.Content = confirm;
        if (danger)
            ConfirmButton.SetResourceReference(StyleProperty, "Button.Danger");
        if (cancel == null)
        {
            CancelButton.Visibility = Visibility.Collapsed;
            ConfirmButton.IsCancel = true;
        }
        else
        {
            CancelButton.Content = cancel;
        }
    }

    public static bool Confirm(Window? owner, DialogKind kind, string heading, string body, string confirm, string cancel = "Cancel", bool danger = false) =>
        new MessageDialog(owner, kind, heading, body, confirm, cancel, danger).ShowDialog() == true;

    public static void Inform(Window? owner, DialogKind kind, string heading, string body, string close = "OK") =>
        new MessageDialog(owner, kind, heading, body, close, null, false).ShowDialog();

    private void ConfirmClick(object sender, RoutedEventArgs e)
    {
        if (!ConfirmButton.IsCancel)
            DialogResult = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            try
            {
                Clipboard.SetText(BodyText.Text.Length > 0 ? $"{HeadingText.Text}\n\n{BodyText.Text}" : HeadingText.Text);
            }
            catch (COMException)
            {
            }
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }
}
