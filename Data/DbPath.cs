namespace Lana.Data;

public static class DbPath
{
    public static string GetDatabaseFilePath()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Lana");

        Directory.CreateDirectory(root);
        return Path.Combine(root, "lana.db");
    }

    public static string GetConnectionString()
        => $"Data Source={GetDatabaseFilePath()}";
}
