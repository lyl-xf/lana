using Lana.Data.Mappers;
using Lana.Data.Sqlite;

namespace Lana.Services;

/// <summary>Settings 表键值读写实现。</summary>
public sealed class SettingsService : ISettingsService
{
    private readonly ISqliteSessionFactory _sessionFactory;
    private readonly SettingMapper _settingMapper;

    public SettingsService(ISqliteSessionFactory sessionFactory)
        : this(sessionFactory, new SettingMapper(sessionFactory))
    {
    }

    public SettingsService(ISqliteSessionFactory sessionFactory, SettingMapper settingMapper)
    {
        _sessionFactory = sessionFactory;
        _settingMapper = settingMapper;
    }

    public string DatabasePath => _sessionFactory.DatabasePath;

    public async Task<bool> GetBoolAsync(string key, bool defaultValue = false)
    {
        var raw = await GetStringAsync(key, defaultValue ? "true" : "false");
        return bool.TryParse(raw, out var value) ? value : defaultValue;
    }

    public Task SetBoolAsync(string key, bool value)
        => SetStringAsync(key, value ? "true" : "false");

    public async Task<string> GetStringAsync(string key, string defaultValue = "")
    {
        var setting = await _settingMapper.FindByKeyAsync(key);
        return setting?.Value ?? defaultValue;
    }

    public Task SetStringAsync(string key, string value)
        => _settingMapper.UpsertAsync(key, value);
}
