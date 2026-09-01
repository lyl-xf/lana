# lana
基于 **.NET 9 + Avalonia 12** 的跨平台桌面应用骨架，带炫酷深色/白色风格界面。


## 跨平台发布

```bash
# Windows
dotnet publish -c Release -r win-x64 --self-contained true

# Linux
dotnet publish -c Release -r linux-x64 --self-contained true

# macOS Intel
dotnet publish -c Release -r osx-x64 --self-contained true

# macOS Apple Silicon
dotnet publish -c Release -r osx-arm64 --self-contained true
```

发布产物在 `bin/Release/net9.0/<rid>/publish/`。

## 图片一览
![1.png](Assets%2F1.png)

![2.png](Assets%2F2.png)

![3.png](Assets%2F3.png)

![4.png](Assets%2F4.png)


## 关于摄像头预览许可说明

摄像头预览、播放通过 NuGet 使用 [LibVLCSharp](https://code.videolan.org/videolan/LibVLCSharp) 与 [LibVLC](https://code.videolan.org/videolan/vlc)（含 `VideoLAN.LibVLC.Windows` / `VideoLAN.LibVLC.Mac`），上述组件采用 **GNU LGPL-2.1 或后续版本（LGPL-2.1+）**。本仓库未修改其源码，运行时以动态库方式加载，这些库可被兼容版本替换。

- LGPL-2.1 全文：[Licenses/LGPL-2.1.txt](Licenses/LGPL-2.1.txt)
- 第三方说明：[Licenses/NOTICE-LibVLC.txt](Licenses/NOTICE-LibVLC.txt)
- 在线文本：<https://www.gnu.org/licenses/old-licenses/lgpl-2.1.html>

对应版本的 LibVLC / LibVLCSharp 源码请从上方上游仓库获取（与本项目 `csproj` 中的 NuGet 版本一致）。分发本应用时，请一并保留 `Licenses/` 目录。
