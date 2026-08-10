using Avalonia.Controls;
using Avalonia.Input;
using Lana.ViewModels;

namespace Lana.Views;

/// <summary>
/// 定义页视图。
/// <para>左侧状态区：AXAML 绑定 <see cref="DefinedPageViewModel.StatusGroups"/>（共享 LiveState，无 code-behind 刷新）。</para>
/// <para>Bool 点动不能用 Button（会吞 PointerPressed），故用 Border + 本文件的 Pressed/Released/CaptureLost。</para>
/// </summary>
public partial class DefinedPageView : UserControl
{
    /// <summary>
    /// 初始化定义页组件并加载 XAML 布局。
    /// </summary>
    public DefinedPageView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 点动 Bool 变量按下：捕获指针并向 PLC 写入 true。
    /// </summary>
    /// <param name="sender">触发事件的 Border 或其子控件。</param>
    /// <param name="e">指针按下事件参数。</param>
    private void OnMomentaryPressed(object? sender, PointerPressedEventArgs e)
    {
        // 仅响应左键，忽略右键/中键
        if (!e.GetCurrentPoint(sender as Control).Properties.IsLeftButtonPressed)
            return;

        if (!TryGetMomentary(sender, out var host, out var action, out var vm))
            return;

        // 捕获指针，确保后续 Release/CaptureLost 能正确配对
        e.Pointer.Capture(host);
        e.Handled = true;
        _ = vm.PressBoolAsync(action);
    }

    /// <summary>
    /// 点动 Bool 变量松开：释放指针捕获并向 PLC 写入 false。
    /// </summary>
    /// <param name="sender">触发事件的 Border 或其子控件。</param>
    /// <param name="e">指针释放事件参数。</param>
    private void OnMomentaryReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!TryGetMomentary(sender, out var host, out var action, out var vm))
            return;

        // 仅当本控件仍持有捕获时才释放，避免误清其它控件的捕获
        if (ReferenceEquals(e.Pointer.Captured, host))
            e.Pointer.Capture(null);

        e.Handled = true;
        _ = vm.ReleaseBoolAsync(action);
    }

    /// <summary>
    /// 指针捕获丢失时（如窗口失焦、被其它控件抢占）：等价于松开，写入 false。
    /// </summary>
    /// <param name="sender">丢失捕获的 Border。</param>
    /// <param name="e">捕获丢失事件参数。</param>
    private void OnMomentaryCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        // 仅处理仍处于按下状态的点动 Bool 动作
        if (sender is not Control { DataContext: DefinedVariableAction { IsMomentaryBool: true, IsPressed: true } action })
            return;
        if (DataContext is not DefinedPageViewModel vm)
            return;

        _ = vm.ReleaseBoolAsync(action);
    }

    /// <summary>
    /// 从事件源向上遍历可视树，查找 Bool 点动 Border 及其 ViewModel。
    /// </summary>
    /// <param name="sender">事件源控件（可能是 Border 内部的 TextBlock 等）。</param>
    /// <param name="host">输出：承载点动动作的 Border 容器。</param>
    /// <param name="action">输出：对应的 <see cref="DefinedVariableAction"/>。</param>
    /// <param name="vm">输出：页面 <see cref="DefinedPageViewModel"/>。</param>
    /// <returns>找到有效的点动 Bool 动作时返回 <c>true</c>。</returns>
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

        // 必须是点动 Bool 类型才继续
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
