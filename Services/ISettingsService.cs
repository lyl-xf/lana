namespace Lana.Services;

/// <summary>
/// 键值设置读写（Settings 表）。键名见 <c>Lana.Data.SettingKeys</c>。
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// 读取布尔设置项。
    /// </summary>
    /// <param name="key">设置键名。</param>
    /// <param name="defaultValue">键不存在或解析失败时的默认值。</param>
    /// <returns>解析后的布尔值。</returns>
    Task<bool> GetBoolAsync(string key, bool defaultValue = false);

    /// <summary>
    /// 写入布尔设置项（存为 "true" / "false" 字符串）。
    /// </summary>
    /// <param name="key">设置键名。</param>
    /// <param name="value">布尔值。</param>
    Task SetBoolAsync(string key, bool value);

    /// <summary>
    /// 读取字符串设置项。
    /// </summary>
    /// <param name="key">设置键名。</param>
    /// <param name="defaultValue">键不存在时的默认值。</param>
    /// <returns>设置值或默认值。</returns>
    Task<string> GetStringAsync(string key, string defaultValue = "");

    /// <summary>
    /// 写入或更新字符串设置项。
    /// </summary>
    /// <param name="key">设置键名。</param>
    /// <param name="value">设置值。</param>
    Task SetStringAsync(string key, string value);

    /// <summary>当前 SQLite 文件路径（应用目录下 lana.db）。</summary>
    string DatabasePath { get; }
}
