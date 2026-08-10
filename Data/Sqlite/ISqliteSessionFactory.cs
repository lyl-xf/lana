namespace Lana.Data.Sqlite;

/// <summary>
/// 打开短生命周期 SQL 会话。Mapper/Service 每次操作 OpenSession，用完 Dispose。
/// </summary>
public interface ISqliteSessionFactory
{
    ISqliteSession OpenSession();
    string DatabasePath { get; }
}
