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
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        var name = param.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType(name);

        if (type != null)
        {
            return (Control)Activator.CreateInstance(type)!;
        }

        return new TextBlock { Text = "Not Found: " + name };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
