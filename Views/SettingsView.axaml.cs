using Avalonia.Controls;
using Avalonia.Input;
using Lana.ViewModels;

namespace Lana.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void OnAuroraThemePressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            vm.SelectAuroraThemeCommand.Execute(null);
        }
    }

    private void OnSnowThemePressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            vm.SelectSnowThemeCommand.Execute(null);
        }
    }
}
