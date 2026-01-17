using ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;
using ChefKnifeStudios.PokerAttack.Shared.Enums;
using Microsoft.AspNetCore.Components;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Components.Lobbies;

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
        foreach (var item in LobbyViewModel.Lobbies)
        {
            if (item is INotifyPropertyChanged npc)
            {
                npc.PropertyChanged += HandleItemPropertyChanged;
            }
        }
    }

    public void Dispose()
    {
        LobbyViewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
        LobbyViewModel.Lobbies.CollectionChanged -= HandleCollectionChanged;
        foreach (var item in LobbyViewModel.Lobbies)
        {
            if (item is INotifyPropertyChanged npc)
            {
                npc.PropertyChanged -= HandleItemPropertyChanged;
            }
        }
    }

    void HandleCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // New items added?
        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is INotifyPropertyChanged npc)
                {
                    npc.PropertyChanged += HandleItemPropertyChanged;
                }
            }
        }

        // Items removed?
        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is INotifyPropertyChanged npc)
                {
                    npc.PropertyChanged -= HandleItemPropertyChanged;
                }
            }
        }

        InvokeAsync(StateHasChanged);
    }

    void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_subscriptions.Contains(e.PropertyName) is false) return;
        Task.Run(async () => await InvokeAsync(StateHasChanged));
    }

    void HandleJoinLobbyPressed(string lobbyId) =>
        _ = LobbyViewModel.JoinLobbyAsync(lobbyId, ApplicationViewModel.Player);

    void HandleLeaveLobbyPressed(string lobbyId) =>
        _ = LobbyViewModel.LeaveLobbyAsync(lobbyId, ApplicationViewModel.Player);

    void HandleStartQuickGamePressed(string lobbyId) =>
        _ = LobbyViewModel.StartGameAsync(lobbyId, GameModes.Quick);

    void HandleStartFullGamePressed(string lobbyId) =>
        _ = LobbyViewModel.StartGameAsync(lobbyId, GameModes.Full);

    void HandleItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        InvokeAsync(StateHasChanged);
    }
}
