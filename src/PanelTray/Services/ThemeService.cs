using System.Windows;
using PanelTray.Models;

namespace PanelTray.Services;

public sealed class ThemeService
{
    public void Apply(ThemeMode theme)
    {
        var normalized = theme == ThemeMode.System ? ThemeMode.Dark : theme;
        var themePath = normalized == ThemeMode.Light
            ? "Resources/Themes/Light.xaml"
            : "Resources/Themes/Dark.xaml";

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var currentTheme = dictionaries.FirstOrDefault(dictionary =>
            dictionary.Source?.OriginalString.Contains("Resources/Themes/", StringComparison.OrdinalIgnoreCase) == true);

        var newTheme = new ResourceDictionary { Source = new Uri(themePath, UriKind.Relative) };
        if (currentTheme is not null)
        {
            var index = dictionaries.IndexOf(currentTheme);
            dictionaries[index] = newTheme;
        }
        else
        {
            dictionaries.Insert(0, newTheme);
        }
    }
}
