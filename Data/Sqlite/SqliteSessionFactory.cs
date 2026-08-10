namespace Lana.Data.Sqlite;

/// <summary>
/// <see cref="ISqliteSessionFactory"/> 的默认实现，使用应用默认数据库路径。
/// </summary>
public sealed class SqliteSessionFactory : ISqliteSessionFactory
{
    /// <summary>SQLite 连接字符串。</summary>
    private readonly string _connectionString;

    /// <summary>
    /// 使用 <see cref="DbPath"/> 默认路径创建工厂。
    /// </summary>
    public SqliteSessionFactory()
        : this(DbPath.GetConnectionString(), DbPath.GetDatabaseFilePath())
    {
    }

    /// <summary>
    /// 使用指定连接字符串与数据库路径创建工厂。
    /// </summary>
    /// <param name="connectionString">SQLite 连接字符串。</param>
    /// <param name="databasePath">数据库文件绝对路径（供 UI 展示等用途）。</param>
    public SqliteSessionFactory(string connectionString, string databasePath)
    {
        _connectionString = connectionString;
        DatabasePath = databasePath;
    }

    /// <inheritdoc />
    public string DatabasePath { get; }

    /// <inheritdoc />
    public ISqliteSession OpenSession() => new SqliteSession(_connectionString);
}
