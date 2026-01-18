using ChefKnifeStudios.PokerAttack.Client.Shared.Services.JsInterop;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Services;

public interface IAudioService
{
    Task PlayOneShotAsync(string audioFilePath);
}

public class AudioService : IAudioService
{
    readonly IAudioJsInterop _audioJsInterop;
    readonly ISettingsService _settingsService;

    public AudioService(IAudioJsInterop audioJsInterop, ISettingsService settingsService)
    {
        _audioJsInterop = audioJsInterop;
        _settingsService = settingsService;
    }

    public async Task PlayOneShotAsync(string audioFilePath)
    {
        var settings = _settingsService.GetSettings();
        if (!settings.IsAudioEnabled)
        {
            return;
        }

        await _audioJsInterop.PlayOneShotAsync(audioFilePath);
    }
}
