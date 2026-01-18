using ChefKnifeStudios.PokerAttack.Client.Core.Services;
using ChefKnifeStudios.PokerAttack.Client.Shared.Constants;
using ChefKnifeStudios.PokerAttack.Client.Shared.Models;
using ChefKnifeStudios.PokerAttack.Client.Shared.Services;
using ChefKnifeStudios.PokerAttack.Client.Shared.Services.JsInterop;
using ChefKnifeStudios.PokerAttack.Shared;
using ChefKnifeStudios.PokerAttack.Shared.DTOs;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Gameplay;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.AspNetCore.Components;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;

public interface ISoloGameplayViewModel
{
    int RunTimeInSeconds { get; }
    int Score { get; }
    ObservableCollection<CardItem> CardsInHand { get; }
    int AvailablePlayHands { get; }
    int AvailableDiscards { get; }
    PlayerPowerDTO? ActivePlayerPower { get; }
    int PowerCharges { get; }

    void PlaySelectedCards();
    void DiscardSelectedCards();
    void ToggleCardSelection(int index);
    void SortByRank();
    void SortBySuit();
    void ClearSelections();
}

public partial class SoloGameplayViewModel : BaseViewModel, ISoloGameplayViewModel, IDisposable
{
    readonly IToastService _toastService;
    readonly IEventNotificationService _eventNotificationService;
    readonly NavigationManager _navigationManager;
    readonly IAudioJsInterop _audioJsInterop;

    const int _DEFAULT_NUM_PLAY_HANDS = 5;
    const int _DEFAULT_NUM_DISCARDS = 5;
    const int _DEFAULT_NUM_POWER_CHARGES = 2;

    [ObservableProperty]
    int _runTimeInSeconds = 0;

    [ObservableProperty]
    int _score = 0;

    [ObservableProperty]
    ObservableCollection<CardItem> _cardsInHand = [];

    [ObservableProperty]
    int _availablePlayHands = _DEFAULT_NUM_PLAY_HANDS;

    [ObservableProperty]
    int _availableDiscards = _DEFAULT_NUM_DISCARDS;

    [ObservableProperty]
    PlayerPowerDTO? _activePlayerPower;

    [ObservableProperty]
    int _powerCharges = _DEFAULT_NUM_POWER_CHARGES;

    CancellationTokenSource _timerToken;

    enum SortMode
    {
        None,
        ByRank,
        BySuit
    }

    SortMode _currentSortMode = SortMode.None;

    public SoloGameplayViewModel(
        IToastService toastService,
        IEventNotificationService eventNotificationService,
        NavigationManager navigationManager,
        IAudioJsInterop audioJsInterop)
    {
        _toastService = toastService;
        _eventNotificationService = eventNotificationService;
        _navigationManager = navigationManager;
        _audioJsInterop = audioJsInterop;

        _timerToken = TimerHelper.SetInterval(() =>
        {
            if (RunTimeInSeconds > 0) RunTimeInSeconds--;
        }, 1000);
    }

    public void Dispose()
    {
        _timerToken.Cancel();
        _timerToken.Dispose();
    }

    public void PlaySelectedCards()
    {
        if (AvailablePlayHands <= 0)
        {
            _toastService.ShowWarning("No hands left");
            return;
        }

        var selectedCards = CardsInHand.Where(x => x.IsSelected).ToList();

        if (selectedCards.Count < 1) return;
        if (selectedCards.Count > 5)
        {
            _toastService.ShowWarning("A Hand has a 5 card limit");
            return;
        }

        // TODO PlayHand
        // - Add scoring
        // - Remove & add new cards (Refresh hand)
    }

    public void DiscardSelectedCards()
    {
        if (AvailableDiscards <= 0)
        {
            _toastService.ShowWarning("No hands left");
            return;
        }

        var selectedCards = CardsInHand.Where(x => x.IsSelected).ToList();

        if (selectedCards.Count < 1) return;

        // TODO DiscardCards
        // - Remove & add new cards (Refresh hand)

        AvailableDiscards--;
    }

    public void ToggleCardSelection(int index)
    {
        if (index < 0 || index >= CardsInHand.Count) return;
        CardsInHand[index].IsSelected = !CardsInHand[index].IsSelected;
        _ = _audioJsInterop.PlayOneShotAsync(FilePaths.PlaceCardThree);
    }

    public void SortByRank()
    {
        _currentSortMode = SortMode.ByRank;
        ApplySort();
    }

    public void SortBySuit()
    {
        _currentSortMode = SortMode.BySuit;
        ApplySort();
    }

    public void ClearSelections()
    {
        foreach (var card in CardsInHand)
        {
            card.IsSelected = false;
        }
    }

    void ApplySort()
    {
        if (_currentSortMode == SortMode.None) return;

        var sorted = _currentSortMode switch
        {
            SortMode.ByRank => CardsInHand.OrderBy(c => c.Rank).ThenBy(c => c.Suit).ToList(),
            SortMode.BySuit => CardsInHand.OrderBy(c => c.Suit).ThenBy(c => c.Rank).ToList(),
            _ => CardsInHand.ToList()
        };

        CardsInHand.Clear();
        foreach (var card in sorted)
        {
            CardsInHand.Add(card);
        }
    }
}
