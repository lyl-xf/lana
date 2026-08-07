namespace Lana.Data.Entities;

public sealed class AppSettingEntity
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
