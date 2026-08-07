using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Lana.ViewModels;

namespace Lana.Views;

public partial class CamerasView : UserControl
{
    public CamerasView()
    {
        InitializeComponent();
    }

    private void OnPreviewPickPressed(object? sender, PointerPressedEventArgs e)
    {
        // 勾选框自己处理，避免点 CheckBox 时再切换一次
        if (e.Source is CheckBox || (e.Source as Control)?.TemplatedParent is CheckBox)
            return;

        if (sender is not Border { DataContext: CameraListItem item })
            return;
        if (DataContext is not CamerasViewModel vm)
            return;

        if (vm.TogglePreviewPickCommand.CanExecute(item))
            vm.TogglePreviewPickCommand.Execute(item);

        e.Handled = true;
    }
}
