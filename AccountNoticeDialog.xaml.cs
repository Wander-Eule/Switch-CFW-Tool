using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SwitchCfwTool;

/// <summary>
/// Shown when a tool needs an account before it can be used. Returns true when the user
/// wants to be taken to the sign-up page, false when they already have an account.
/// </summary>
public partial class AccountNoticeDialog : Window
{
    public AccountNoticeDialog()
    {
        InitializeComponent();
        TitleBarTheme.Apply(this);
    }

    public AccountNoticeDialog(string headline, string reason, string question,
        string goLabel, string haveLabel) : this()
    {
        HeadlineText.Text = headline;
        ReasonText.Text = reason;
        QuestionText.Text = question;
        GoLabelText.Text = goLabel;
        HaveLabelText.Text = haveLabel;
    }

    private void Go_Click(object? sender, RoutedEventArgs e) => Close(true);

    private void Have_Click(object? sender, RoutedEventArgs e) => Close(false);
}
