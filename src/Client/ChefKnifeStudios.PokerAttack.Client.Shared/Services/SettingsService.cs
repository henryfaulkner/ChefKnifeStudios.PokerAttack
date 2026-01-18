using Blazored.LocalStorage;
using ChefKnifeStudios.PokerAttack.Client.Shared.Constants;
using ChefKnifeStudios.PokerAttack.Client.Shared.Models;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Services;

public interface ISettingsService
{
    Settings GetSettings();
    void SaveSettings(Settings settings);
    T? GetSettingValue<T>(string propertyName);
    void SetSettingValue<T>(string propertyName, T value);
}

public class SettingsService : ISettingsService
{
    readonly ISyncLocalStorageService _localStorageService;

    public SettingsService(ISyncLocalStorageService localStorageService)
    {
        _localStorageService = localStorageService;
    }

    public Settings GetSettings()
    {
        var storedSettings = _localStorageService.GetItem<Settings>(LocalStorageConstants.SettingsKey);
        if (storedSettings is not null)
        {
            return storedSettings;
        }

        var defaultSettings = new Settings();
        SaveSettings(defaultSettings);
        return defaultSettings;
    }

    public void SaveSettings(Settings settings)
    {
        _localStorageService.SetItem(LocalStorageConstants.SettingsKey, settings);
    }

    public T? GetSettingValue<T>(string propertyName)
    {
        var settings = GetSettings();
        var property = typeof(Settings).GetProperty(propertyName);
        if (property is null)
        {
            return default;
        }

        var value = property.GetValue(settings);
        if (value is T typedValue)
        {
            return typedValue;
        }

        return default;
    }

    public void SetSettingValue<T>(string propertyName, T value)
    {
        var settings = GetSettings();
        var property = typeof(Settings).GetProperty(propertyName);
        if (property is not null)
        {
            property.SetValue(settings, value);
            SaveSettings(settings);
        }
    }
}
