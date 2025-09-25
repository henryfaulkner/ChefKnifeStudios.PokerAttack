using ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;
using Microsoft.AspNetCore.Components;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Components;

public partial class LobbyList : ComponentBase, IDisposable
{
    [Inject] IApplicationViewModel ApplicationViewModel { get; set; } = null!;
    [Inject] ILobbyViewModel LobbyViewModel { get; set; } = null!;

    readonly string[] _subscriptions =
    [
        nameof(LobbyViewModel.Lobbies),
        nameof(LobbyViewModel.IsLoadingLobbies),
    ];

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        await LobbyViewModel.LoadLobbiesAsync();

        LobbyViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        LobbyViewModel.Lobbies.CollectionChanged += Lobbies_CollectionChanged;
    }

    private void Lobbies_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        InvokeAsync(StateHasChanged);
    }

    public void Dispose()
    {
        LobbyViewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
        LobbyViewModel.Lobbies.CollectionChanged -= Lobbies_CollectionChanged;
    }

    void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_subscriptions.Contains(e.PropertyName) is false) return;
        Task.Run(async () => await InvokeAsync(StateHasChanged));
    }

    void HandleJoinLobbyPressed(string gameId)
    {
        _ = LobbyViewModel.JoinLobbyAsync(gameId, ApplicationViewModel.Player);
    }

    void HandleLeaveLobbyPressed(string gameId)
    {
        _ = LobbyViewModel.LeaveLobbyAsync(gameId, ApplicationViewModel.Player);
    }
}
