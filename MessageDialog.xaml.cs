using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SwitchCfwTool;

/// <summary>
/// Single-button notice styled like the rest of the app.
/// </summary>
public partial class MessageDialog : Window
{
    public MessageDialog()
    {
        InitializeComponent();
        TitleBarTheme.Apply(this);
    }

    public MessageDialog(string title, string headline, string message) : this()
    {
        Title = title;
        HeadlineText.Text = headline;
        MessageText.Text = message;
    }

    private void Ok_Click(object? sender, RoutedEventArgs e) => Close();

    /// <summary>Convenience wrapper so call sites read like the MessageBox they replace.</summary>
    public static Task Show(Window owner, string title, string headline, string message)
        => new MessageDialog(title, headline, message).ShowDialog(owner);
}
