using Lana.Data.Entities;
using Lana.Data.Mappers;
using Lana.Data.Sqlite;
using Lana.Gateway.Data;

namespace Lana.Data;

/// <summary>
/// 应用启动时数据库初始化：核心表 + 各模块 Schema + 种子数据。
/// <para>
/// 新增模块表：编写 XxxSchema.EnsureAsync，并在此处调用；
/// 再提供 Mapper / Service，于 MainViewModel 中组装。
/// </para>
/// </summary>
public static class DbInitializer
{
    /// <summary>创建核心表、迁移模块 Schema，并种子 admin/demo 与默认设置。</summary>
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

        // 模块 Schema：网关设备/变量/MQTT、摄像头、操作历史等
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

/// <summary>
/// Settings 表键名常量。新增配置项：在此加 key → SettingsService 读写 → Settings UI。
/// </summary>
public static class SettingKeys
{
    public const string ThemeStyle = "ThemeStyle";
    /// <summary>旧版暗色开关，启动时会迁移到 ThemeStyle。</summary>
    public const string DarkTheme = "DarkTheme";
    public const string EnableAnimations = "EnableAnimations";
    public const string RememberMe = "RememberMe";
    public const string RememberedUsername = "RememberedUsername";
    /// <summary>记住密码（Base64，非强加密，仅本地便利）。</summary>
    public const string RememberedPassword = "RememberedPassword";
}
