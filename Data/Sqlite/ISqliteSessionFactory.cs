namespace Lana.Data.Sqlite;

/// <summary>
/// 打开短生命周期 SQL 会话。Mapper/Service 每次操作 OpenSession，用完 Dispose。
/// </summary>
public interface ISqliteSessionFactory
{
    /// <summary>
    /// 创建并打开一个新的 SQLite 会话。
    /// </summary>
    /// <returns>已连接的数据库会话，调用方负责释放。</returns>
    ISqliteSession OpenSession();

    /// <summary>当前数据库文件的绝对路径。</summary>
    string DatabasePath { get; }
}
