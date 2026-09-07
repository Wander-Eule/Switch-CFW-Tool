using System.Windows;

namespace SwitchCfwTool;

public partial class FaqWindow : Window
{
    public FaqWindow()
    {
        InitializeComponent();
        TitleBarTheme.Apply(this);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
