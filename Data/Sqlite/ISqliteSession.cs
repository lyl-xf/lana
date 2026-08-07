using System.Data;

namespace Lana.Data.Sqlite;

/// <summary>
/// 轻量 SQL 会话，风格接近 MyBatis SqlSession：手写 SQL + 参数对象映射。
/// </summary>
public interface ISqliteSession : IAsyncDisposable, IDisposable
{
    IDbConnection Connection { get; }

    Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? param = null);

    Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object? param = null);

    Task<T> ExecuteScalarAsync<T>(string sql, object? param = null);

    Task<int> ExecuteAsync(string sql, object? param = null);

    Task<IDbTransaction> BeginTransactionAsync();
}
