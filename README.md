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
