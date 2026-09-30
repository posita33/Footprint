using System.Windows;
using System.Windows.Media;
using Footprint.Core;

namespace Footprint;

internal static class Appearance
{
    public static void Apply(AppearanceSettings settings)
    {
        var resources = Application.Current.Resources;
        void Brush(string key, string color) => resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        Brush("WindowBackground", settings.DarkTheme ? "#202936" : "#DCE2EA");
        Brush("WorkspaceBackground", settings.DarkTheme ? "#2C3848" : "#F9FBFF");
        Brush("LabelForeground", settings.DarkTheme ? "#E8EEF5" : "#182635");
        Brush("TabHoverBackground", settings.DarkTheme ? "#425369" : "#E8EDF4");
        Brush("SplitterBackground", settings.DarkTheme ? "#526579" : "#B6C1CE");
        resources["CommandFontSize"] = settings.CommandFontSize;
    }
}
