namespace Lana.Services;

/// <summary>
/// 键值设置读写（Settings 表）。键名见 <c>Lana.Data.SettingKeys</c>。
/// </summary>
public interface ISettingsService
{
    Task<bool> GetBoolAsync(string key, bool defaultValue = false);
    Task SetBoolAsync(string key, bool value);
    Task<string> GetStringAsync(string key, string defaultValue = "");
    Task SetStringAsync(string key, string value);

    /// <summary>当前 SQLite 文件路径（应用目录下 lana.db）。</summary>
    string DatabasePath { get; }
}
