namespace Lana.Data;

/// <summary>
/// SQLite 文件路径：相对应用程序目录（<see cref="AppContext.BaseDirectory"/> 下的 lana.db）。
/// 开发时通常在 bin/Debug/net9.0/；发布后与可执行文件同级。
/// </summary>
public static class DbPath
{
    /// <summary>数据库文件名（与可执行文件同目录）。</summary>
    public const string FileName = "lana.db";

    /// <summary>
    /// 获取数据库文件的绝对路径。
    /// </summary>
    /// <returns>基于 <see cref="AppContext.BaseDirectory"/> 解析后的完整路径。</returns>
    public static string GetDatabaseFilePath()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, FileName));

    /// <summary>
    /// 构建 SQLite 连接字符串。
    /// </summary>
    /// <returns>格式为 <c>Data Source=...</c> 的连接字符串。</returns>
    public static string GetConnectionString()
        => $"Data Source={GetDatabaseFilePath()}";
}
