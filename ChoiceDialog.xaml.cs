using System.Windows;

namespace SwitchCfwTool;

/// <summary>
/// Two-way prompt used for anything that needs a deliberate yes/no from the user (tool
/// conflicts, optional companion downloads). Kept generic rather than one window per case,
/// since they only differ in wording.
/// </summary>
public partial class ChoiceDialog : Window
{
    public ChoiceDialog(string headline, string reason, string question, string yesLabel, string noLabel)
    {
        InitializeComponent();
        TitleBarTheme.Apply(this);

        HeadlineText.Text = headline;
        ReasonText.Text = reason;
        QuestionText.Text = question;
        YesButton.Tag = yesLabel;
        NoButton.Tag = noLabel;
    }

    private void Yes_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void No_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
