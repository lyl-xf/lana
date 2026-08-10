using Avalonia.Controls;
using Avalonia.Input;
using Lana.ViewModels;

namespace Lana.Views;

/// <summary>
/// 定义页视图。Bool 点动不能用 Button（会吞 PointerPressed），
/// 故在 AXAML 中用 Border 绑定本文件的 Pressed/Released/CaptureLost。
/// </summary>
public partial class DefinedPageView : UserControl
{
    public DefinedPageView()
    {
        InitializeComponent();
    }

    /// <summary>按下：捕获指针并写 true。</summary>
    private void OnMomentaryPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(sender as Control).Properties.IsLeftButtonPressed)
            return;

        if (!TryGetMomentary(sender, out var host, out var action, out var vm))
            return;

        e.Pointer.Capture(host);
        e.Handled = true;
        _ = vm.PressBoolAsync(action);
    }

    private void OnMomentaryReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!TryGetMomentary(sender, out var host, out var action, out var vm))
            return;

        if (ReferenceEquals(e.Pointer.Captured, host))
            e.Pointer.Capture(null);

        e.Handled = true;
        _ = vm.ReleaseBoolAsync(action);
    }

    private void OnMomentaryCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (sender is not Control { DataContext: DefinedVariableAction { IsMomentaryBool: true, IsPressed: true } action })
            return;
        if (DataContext is not DefinedPageViewModel vm)
            return;

        _ = vm.ReleaseBoolAsync(action);
    }

    private bool TryGetMomentary(
        object? sender,
        out Control host,
        out DefinedVariableAction action,
        out DefinedPageViewModel vm)
    {
        host = null!;
        action = null!;
        vm = null!;

        if (sender is not Control control)
            return false;

        // 点到内部 TextBlock 时，向上找到带 DataContext 的容器
        var current = control;
        DefinedVariableAction? found = null;
        Control? foundHost = null;
        while (current is not null)
        {
            if (current.DataContext is DefinedVariableAction a)
            {
                found = a;
                foundHost = current;
                break;
            }

            current = current.Parent as Control;
        }

        if (found is not { IsMomentaryBool: true } || foundHost is null)
            return false;
        if (DataContext is not DefinedPageViewModel pageVm)
            return false;

        host = foundHost;
        action = found;
        vm = pageVm;
        return true;
    }
}
