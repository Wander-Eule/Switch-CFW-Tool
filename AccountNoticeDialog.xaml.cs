using System.Windows;

namespace SwitchCfwTool;

/// <summary>
/// Shown when a tool needs an account before it can be used. Returns true when the user
/// wants to be taken to the sign-up page, false when they already have an account.
/// </summary>
public partial class AccountNoticeDialog : Window
{
    public AccountNoticeDialog(string headline, string reason, string question,
        string goLabel, string haveLabel)
    {
        InitializeComponent();
        TitleBarTheme.Apply(this);

        HeadlineText.Text = headline;
        ReasonText.Text = reason;
        QuestionText.Text = question;
        GoButton.Tag = goLabel;
        HaveButton.Tag = haveLabel;
    }

    private void Go_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Have_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
