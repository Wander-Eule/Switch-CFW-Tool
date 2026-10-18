using System.Windows;

namespace SwitchCfwTool;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Before the main window exists, so it is built in the saved language from the start
        // instead of flashing up in English and switching a moment later.
        Loc.LoadSavedLanguage();
        base.OnStartup(e);
    }
}
