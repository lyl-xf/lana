using System.Data;

namespace Lana.Data.Sqlite;

/// <summary>
/// 轻量 SQL 会话，风格接近 MyBatis SqlSession：手写 SQL + 参数对象映射。
/// </summary>
public interface ISqliteSession : IAsyncDisposable, IDisposable
{
    /// <summary>底层 ADO.NET 连接，可用于事务等高级操作。</summary>
    IDbConnection Connection { get; }

    /// <summary>
    /// 执行查询并返回多行结果。
    /// </summary>
    /// <typeparam name="T">映射目标类型。</typeparam>
    /// <param name="sql">SQL 语句。</param>
    /// <param name="param">命名参数对象，可为 null。</param>
    /// <returns>只读结果列表。</returns>
    Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? param = null);

    /// <summary>
    /// 执行查询并返回首行，无结果时返回 default。
    /// </summary>
    /// <typeparam name="T">映射目标类型。</typeparam>
    /// <param name="sql">SQL 语句。</param>
    /// <param name="param">命名参数对象，可为 null。</param>
    /// <returns>首行映射结果，或 default。</returns>
    Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object? param = null);

    /// <summary>
    /// 执行标量查询（如 COUNT、MAX 等）。
    /// </summary>
    /// <typeparam name="T">标量值类型。</typeparam>
    /// <param name="sql">SQL 语句。</param>
    /// <param name="param">命名参数对象，可为 null。</param>
    /// <returns>标量结果。</returns>
    Task<T> ExecuteScalarAsync<T>(string sql, object? param = null);

    /// <summary>
    /// 执行非查询语句（INSERT / UPDATE / DELETE / DDL）。
    /// </summary>
    /// <param name="sql">SQL 语句。</param>
    /// <param name="param">命名参数对象，可为 null。</param>
    /// <returns>受影响的行数。</returns>
    Task<int> ExecuteAsync(string sql, object? param = null);

    /// <summary>
    /// 开启数据库事务。
    /// </summary>
    /// <returns>可用于 Commit / Rollback 的事务对象。</returns>
    Task<IDbTransaction> BeginTransactionAsync();
}
