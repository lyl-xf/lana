using System;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Lana.ViewModels;

namespace Lana;

/// <summary>
/// ViewModel → View 约定定位器（注册于 App.axaml）。
/// <para>
/// 规则：将类型全名中的 <c>ViewModel</c> 替换为 <c>View</c>，再反射创建实例。
/// 例如 <c>Lana.ViewModels.HomeViewModel</c> → <c>Lana.Views.HomeView</c>。
/// </para>
/// <para>
/// 新增页面时请保证命名空间与类名严格遵循此约定，否则界面显示 “Not Found”。
/// </para>
/// </summary>
[RequiresUnreferencedCode(
    "Default implementation of ViewLocator involves reflection which may be trimmed away.",
    Url = "https://docs.avaloniaui.net/docs/concepts/view-locator")]
public class ViewLocator : IDataTemplate
{
    /// <summary>
    /// 根据 ViewModel 实例反射创建对应 View 控件。
    /// </summary>
    /// <param name="param">ViewModel 实例，为 null 时返回 null。</param>
    /// <returns>匹配的 View 控件，找不到类型时返回错误提示 TextBlock。</returns>
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        // ViewModel 全名 → View 全名
        var name = param.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType(name);

        if (type != null)
        {
            return (Control)Activator.CreateInstance(type)!;
        }

        // 约定不匹配或未找到程序集内类型
        return new TextBlock { Text = "Not Found: " + name };
    }

    /// <summary>
    /// 判断数据对象是否应由本模板处理。
    /// </summary>
    /// <param name="data">绑定数据上下文。</param>
    /// <returns>为 <see cref="ViewModelBase"/> 派生类型时返回 true。</returns>
    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
