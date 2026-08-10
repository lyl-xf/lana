using Lana.Data.Mappers;
using Lana.Data.Sqlite;

namespace Lana.Services;

/// <summary>Settings 表键值读写实现。</summary>
public sealed class SettingsService : ISettingsService
{
    /// <summary>会话工厂，用于暴露数据库路径。</summary>
    private readonly ISqliteSessionFactory _sessionFactory;

    /// <summary>设置表数据访问。</summary>
    private readonly SettingMapper _settingMapper;

    /// <summary>
    /// 通过会话工厂创建默认 <see cref="SettingMapper"/>。
    /// </summary>
    /// <param name="sessionFactory">数据库会话工厂。</param>
    public SettingsService(ISqliteSessionFactory sessionFactory)
        : this(sessionFactory, new SettingMapper(sessionFactory))
    {
    }

    /// <summary>
    /// 使用已有 Mapper 实例（便于测试或自定义注入）。
    /// </summary>
    /// <param name="sessionFactory">数据库会话工厂。</param>
    /// <param name="settingMapper">设置表 Mapper。</param>
    public SettingsService(ISqliteSessionFactory sessionFactory, SettingMapper settingMapper)
    {
        _sessionFactory = sessionFactory;
        _settingMapper = settingMapper;
    }

    /// <inheritdoc />
    public string DatabasePath => _sessionFactory.DatabasePath;

    /// <inheritdoc />
    public async Task<bool> GetBoolAsync(string key, bool defaultValue = false)
    {
        var raw = await GetStringAsync(key, defaultValue ? "true" : "false");
        // 解析失败时回退到默认值
        return bool.TryParse(raw, out var value) ? value : defaultValue;
    }

    /// <inheritdoc />
    public Task SetBoolAsync(string key, bool value)
        => SetStringAsync(key, value ? "true" : "false");

    /// <inheritdoc />
    public async Task<string> GetStringAsync(string key, string defaultValue = "")
    {
        var setting = await _settingMapper.FindByKeyAsync(key);
        return setting?.Value ?? defaultValue;
    }

    /// <inheritdoc />
    public Task SetStringAsync(string key, string value)
        => _settingMapper.UpsertAsync(key, value);
}
