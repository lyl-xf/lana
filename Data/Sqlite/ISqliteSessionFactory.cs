namespace AvaloniaUse.Data.Sqlite;

public interface ISqliteSessionFactory
{
    ISqliteSession OpenSession();
    string DatabasePath { get; }
}
