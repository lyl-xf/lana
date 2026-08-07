namespace AvaloniaUse.Data;

public static class DbPath
{
    public static string GetDatabaseFilePath()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AvaloniaUse");

        Directory.CreateDirectory(root);
        return Path.Combine(root, "avalonia-use.db");
    }

    public static string GetConnectionString()
        => $"Data Source={GetDatabaseFilePath()}";
}
