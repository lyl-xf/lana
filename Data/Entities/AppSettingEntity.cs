namespace Lana.Data.Entities;

/// <summary>
/// 应用设置表（Settings）的持久化实体，键值对形式存储用户偏好与配置。
/// </summary>
public sealed class AppSettingEntity
{
    /// <summary>主键，自增。</summary>
    public int Id { get; set; }

    /// <summary>设置项键名，全局唯一（见 <see cref="Lana.Data.SettingKeys"/>）。</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>设置项值，以字符串形式存储。</summary>
    public string Value { get; set; } = string.Empty;
}
