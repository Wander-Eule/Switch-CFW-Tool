using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SwitchCfwTool;

/// <summary>
/// Two-way prompt used for anything that needs a deliberate yes/no from the user (tool
/// conflicts, optional companion downloads). Kept generic rather than one window per case,
/// since they only differ in wording.
/// </summary>
public partial class ChoiceDialog : Window
{
    public ChoiceDialog()
    {
        InitializeComponent();
        TitleBarTheme.Apply(this);
    }

    public ChoiceDialog(string headline, string reason, string question, string yesLabel, string noLabel) : this()
    {
        HeadlineText.Text = headline;
        ReasonText.Text = reason;
        QuestionText.Text = question;
        YesLabelText.Text = yesLabel;
        NoLabelText.Text = noLabel;
    }

    private void Yes_Click(object? sender, RoutedEventArgs e) => Close(true);

    private void No_Click(object? sender, RoutedEventArgs e) => Close(false);
}
