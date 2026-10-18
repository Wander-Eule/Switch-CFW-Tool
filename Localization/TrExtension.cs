using System;
using System.Windows.Data;
using System.Windows.Markup;

namespace SwitchCfwTool.Localization;

/// <summary>
/// XAML shorthand for a translated text: <c>Text="{l:Tr Main.Title}"</c>.
///
/// Produces a one-way binding to <see cref="Loc"/>'s indexer rather than a fixed string, so
/// the text follows a language switch without the window having to be reopened. Works in
/// templates and setters as well, because the binding itself decides how to apply there.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public class TrExtension : MarkupExtension
{
    public TrExtension() { }

    public TrExtension(string key)
    {
        Key = key;
    }

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = Loc.Instance,
            Mode = BindingMode.OneWay
        };
        return binding.ProvideValue(serviceProvider);
    }
}
