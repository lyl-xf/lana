using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;

namespace AvaloniaUse.Data.Sqlite;

public sealed class SqliteSession : ISqliteSession
{
    private readonly SqliteConnection _connection;
    private bool _disposed;

    public SqliteSession(string connectionString)
    {
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
    }

    public IDbConnection Connection => _connection;

    public async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? param = null)
    {
        var rows = await _connection.QueryAsync<T>(sql, param);
        return rows.AsList();
    }

    public async Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object? param = null)
        => await _connection.QueryFirstOrDefaultAsync<T>(sql, param);

    public async Task<T> ExecuteScalarAsync<T>(string sql, object? param = null)
    {
        var result = await _connection.ExecuteScalarAsync<T>(sql, param);
        return result!;
    }

    public Task<int> ExecuteAsync(string sql, object? param = null)
        => _connection.ExecuteAsync(sql, param);

    public async Task<IDbTransaction> BeginTransactionAsync()
        => await _connection.BeginTransactionAsync();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _connection.Dispose();
        _disposed = true;
    }

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
