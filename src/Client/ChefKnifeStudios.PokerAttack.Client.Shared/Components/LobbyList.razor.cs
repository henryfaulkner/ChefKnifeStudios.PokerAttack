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
        nameof(ILobbyViewModel.Lobbies),
        nameof(ILobbyViewModel.IsLoadingLobbies),
    ];

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        await LobbyViewModel.LoadLobbiesAsync();

        LobbyViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        LobbyViewModel.Lobbies.CollectionChanged += HandleCollectionChanged;
    }

    public void Dispose()
    {
        LobbyViewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
        LobbyViewModel.Lobbies.CollectionChanged -= HandleCollectionChanged;
    }

    void HandleCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        InvokeAsync(StateHasChanged);
    }

    void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_subscriptions.Contains(e.PropertyName) is false) return;
        Task.Run(async () => await InvokeAsync(StateHasChanged));
    }

    void HandleJoinLobbyPressed(string gameId) =>
        _ = LobbyViewModel.JoinLobbyAsync(gameId, ApplicationViewModel.Player);

    void HandleLeaveLobbyPressed(string gameId) =>
        _ = LobbyViewModel.LeaveLobbyAsync(gameId, ApplicationViewModel.Player);

    void HandleStartGamePressed(string gameId) =>
        _ = LobbyViewModel.StartGameAsync(gameId);
}
