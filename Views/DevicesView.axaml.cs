using Avalonia.Controls;

namespace Lana.Views;

/// <summary>
/// 设备管理页面视图。
/// <para>展示 PLC/Modbus 等设备连接配置、变量映射与在线状态，绑定 <c>DevicesViewModel</c>。</para>
/// <para>底部状态栏显示操作反馈与忙碌指示，布局见 <c>DevicesView.axaml</c>。</para>
/// </summary>
public partial class DevicesView : UserControl
{
    /// <summary>
    /// 初始化设备管理页面组件。
    /// </summary>
    public DevicesView()
    {
        InitializeComponent();
    }
}
