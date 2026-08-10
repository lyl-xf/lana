namespace Lana.Data;

/// <summary>
/// SQLite 文件路径：相对应用程序目录（<see cref="AppContext.BaseDirectory"/> 下的 lana.db）。
/// 开发时通常在 bin/Debug/net9.0/；发布后与可执行文件同级。
/// </summary>
public static class DbPath
{
    public const string FileName = "lana.db";

    /// <summary>解析后的绝对路径（基于应用基目录）。</summary>
    public static string GetDatabaseFilePath()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, FileName));

    public static string GetConnectionString()
        => $"Data Source={GetDatabaseFilePath()}";
}
