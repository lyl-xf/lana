namespace AvaloniaUse.Data.Sqlite;

public sealed class SqliteSessionFactory : ISqliteSessionFactory
{
    private readonly string _connectionString;

    public SqliteSessionFactory()
        : this(DbPath.GetConnectionString(), DbPath.GetDatabaseFilePath())
    {
    }

    public SqliteSessionFactory(string connectionString, string databasePath)
    {
        _connectionString = connectionString;
        DatabasePath = databasePath;
    }

    public string DatabasePath { get; }

    public ISqliteSession OpenSession() => new SqliteSession(_connectionString);
}
