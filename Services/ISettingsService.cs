namespace Lana.Services;

public interface ISettingsService
{
    Task<bool> GetBoolAsync(string key, bool defaultValue = false);
    Task SetBoolAsync(string key, bool value);
    Task<string> GetStringAsync(string key, string defaultValue = "");
    Task SetStringAsync(string key, string value);
    string DatabasePath { get; }
}
