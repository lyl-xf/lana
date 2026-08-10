using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Lana.Data.Sqlite;

/// <summary>
/// <see cref="ISqliteSession"/> 的默认实现，基于 Dapper 映射查询结果。
/// </summary>
public sealed class SqliteSession : ISqliteSession
{
    /// <summary>底层 SQLite 连接实例。</summary>
    private readonly SqliteConnection _connection;

    /// <summary>是否已释放，防止重复 Dispose。</summary>
    private bool _disposed;

    /// <summary>
    /// 使用连接字符串创建会话并立即打开连接。
    /// </summary>
    /// <param name="connectionString">SQLite 连接字符串。</param>
    public SqliteSession(string connectionString)
    {
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
    }

    /// <inheritdoc />
    public IDbConnection Connection => _connection;

    /// <inheritdoc />
    public async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? param = null)
    {
        var rows = await _connection.QueryAsync<T>(sql, param);
        return rows.AsList();
    }

    /// <inheritdoc />
    public async Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object? param = null)
        => await _connection.QueryFirstOrDefaultAsync<T>(sql, param);

    /// <inheritdoc />
    public async Task<T> ExecuteScalarAsync<T>(string sql, object? param = null)
    {
        var result = await _connection.ExecuteScalarAsync<T>(sql, param);
        return result!;
    }

    /// <inheritdoc />
    public Task<int> ExecuteAsync(string sql, object? param = null)
        => _connection.ExecuteAsync(sql, param);

    /// <inheritdoc />
    public async Task<IDbTransaction> BeginTransactionAsync()
        => await _connection.BeginTransactionAsync();

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _connection.Dispose();
        _disposed = true;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _connection.DisposeAsync();
        _disposed = true;
    }
}
