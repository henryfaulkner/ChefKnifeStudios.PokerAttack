using ChefKnifeStudios.PokerAttack.Client.Core.Services;
using ChefKnifeStudios.PokerAttack.Client.Shared.EventArgs;
using ChefKnifeStudios.PokerAttack.Client.Shared.Services;
using Microsoft.AspNetCore.Components;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Components;

public partial class BladeContainer : ComponentBase, IDisposable
{
    [Parameter] public required RenderFragment ContentFragment { get; set; }
    [Parameter] public bool KeepOpen { get; set; }

    [Inject] IEventNotificationService EventNotificationService { get; set; } = null!;
    [Inject] ICommonJsInterop CommonJsInterop { get; set; } = null!;

    bool _isOpen = false;
    string _uid = Guid.NewGuid().ToString();
    string _elementId => $"blade-{_uid.ToString()}";
    DateTime _lastOpenedUtc;
    const int MinOpenDurationMs = 300;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);
        if (!firstRender) return;
    }

    public void Dispose()
    {
        CommonJsInterop.RemoveOutsideClickListener(_uid);
    }

    public void Open()
    {
        _lastOpenedUtc = DateTime.UtcNow;
        _isOpen = true;
        CommonJsInterop.AddOutsideClickListener(_uid, HandleClosePressed);
        StateHasChanged();
    }

    public void Close()
    {
        if (KeepOpen) return;
        // Prevent close if not enough time has passed since open
        if ((DateTime.UtcNow - _lastOpenedUtc).TotalMilliseconds < MinOpenDurationMs)
            return;

        _isOpen = false;
        CommonJsInterop.RemoveOutsideClickListener(_uid);
        StateHasChanged();
    }

    void HandleClosePressed()
    {
        EventNotificationService.PostEvent(
            this,
            new BladeEventArgs()
            {
                Type = BladeEventArgs.Types.Close,
            }
        );
    }
}
