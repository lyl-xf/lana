using Lana.Data.Entities;
using Lana.Data.Mappers;
using Lana.Data.Sqlite;
using Lana.Gateway.Data;

namespace Lana.Data;

public static class DbInitializer
{
    public static async Task InitializeAsync(ISqliteSessionFactory sessionFactory)
    {
        await using var session = sessionFactory.OpenSession();

        await session.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS Users (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Username TEXT NOT NULL COLLATE NOCASE,
                PasswordHash TEXT NOT NULL,
                DisplayName TEXT NOT NULL,
                Role TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                LastLoginAtUtc TEXT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS IX_Users_Username ON Users(Username);

            CREATE TABLE IF NOT EXISTS Settings (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Key TEXT NOT NULL,
                Value TEXT NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS IX_Settings_Key ON Settings(Key);
            """);

        await GatewaySchema.EnsureAsync(session);
        await Lana.Cameras.Data.CameraSchema.EnsureAsync(session);

        var userMapper = new UserMapper(sessionFactory);
        var settingMapper = new SettingMapper(sessionFactory);

        if (await userMapper.CountAsync() == 0)
        {
            await userMapper.InsertAsync(new UserEntity
            {
                Username = "admin",
                PasswordHash = PasswordHasher.Hash("123456"),
                DisplayName = "Administrator",
                Role = "Admin",
                CreatedAtUtc = DateTime.UtcNow,
            });

            await userMapper.InsertAsync(new UserEntity
            {
                Username = "demo",
                PasswordHash = PasswordHasher.Hash("demo"),
                DisplayName = "Demo User",
                Role = "Member",
                CreatedAtUtc = DateTime.UtcNow,
            });
        }

        await EnsureSettingAsync(settingMapper, SettingKeys.ThemeStyle, "Aurora");
        await EnsureSettingAsync(settingMapper, SettingKeys.EnableAnimations, "true");
    }

    private static async Task EnsureSettingAsync(SettingMapper mapper, string key, string value)
    {
        var existing = await mapper.FindByKeyAsync(key);
        if (existing is null)
        {
            await mapper.InsertAsync(key, value);
        }
    }
}

public static class SettingKeys
{
    public const string ThemeStyle = "ThemeStyle";
    public const string DarkTheme = "DarkTheme";
    public const string EnableAnimations = "EnableAnimations";
    public const string RememberMe = "RememberMe";
    public const string RememberedUsername = "RememberedUsername";
    public const string RememberedPassword = "RememberedPassword";
}
