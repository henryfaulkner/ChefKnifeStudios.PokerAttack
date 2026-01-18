using ChefKnifeStudios.PokerAttack.Client.Core.Services.EndpointServices;
using ChefKnifeStudios.PokerAttack.Client.Shared.Constants;
using ChefKnifeStudios.PokerAttack.Client.Shared.Models;
using ChefKnifeStudios.PokerAttack.Client.Shared.Services;
using ChefKnifeStudios.PokerAttack.Shared;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Gameplay;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.SoloGameplay;
using ChefKnifeStudios.PokerAttack.Shared.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.AspNetCore.Components;
using System.Collections.ObjectModel;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;

public interface ISoloGameplayViewModel : IViewModel
{
    string? GameId { get; }
    string? PlayerId { get; }
    SoloGamePhase Phase { get; }
    int RoundNumber { get; }
    int Score { get; }
    int Wallet { get; }
    int Threshold { get; }
    int RunTimeInSeconds { get; }
    ObservableCollection<CardItem> CardsInHand { get; }
    int AvailablePlayHands { get; }
    int AvailableDiscards { get; }
    ObservableCollection<ShopItem> ShopItems { get; }
    bool IsLoading { get; }

    Task StartGameAsync(string playerName, CancellationToken cancellationToken = default);
    Task PlaySelectedCardsAsync(CancellationToken cancellationToken = default);
    Task DiscardSelectedCardsAsync(CancellationToken cancellationToken = default);
    Task AdvancePhaseAsync(CancellationToken cancellationToken = default);
    Task PurchaseItemAsync(string itemId, CancellationToken cancellationToken = default);
    void ToggleCardSelection(int index);
    void SortByRank();
    void SortBySuit();
    void ClearSelections();
}

public partial class SoloGameplayViewModel : BaseViewModel, ISoloGameplayViewModel, IDisposable
{
    readonly ISoloGameplayEndpointsService _endpointsService;
    readonly IToastService _toastService;
    readonly NavigationManager _navigationManager;
    readonly IAudioService _audioService;

    const int _BASE_THRESHOLD = 500;
    const int _THRESHOLD_INCREMENT = 250;

    [ObservableProperty]
    string? _gameId;

    [ObservableProperty]
    string? _playerId;

    [ObservableProperty]
    SoloGamePhase _phase = SoloGamePhase.InGame;

    [ObservableProperty]
    int _roundNumber = 1;

    [ObservableProperty]
    int _score = 0;

    [ObservableProperty]
    int _wallet = 0;

    [ObservableProperty]
    int _threshold = _BASE_THRESHOLD;

    [ObservableProperty]
    ObservableCollection<CardItem> _cardsInHand = [];

    [ObservableProperty]
    int _availablePlayHands = 5;

    [ObservableProperty]
    int _availableDiscards = 5;

    [ObservableProperty]
    ObservableCollection<ShopItem> _shopItems = [];

    [ObservableProperty]
    bool _isLoading = false;

    [ObservableProperty]
    int _runTimeInSeconds = 0;

    CancellationTokenSource? _timerToken;

    enum SortMode
    {
        None,
        ByRank,
        BySuit
    }

    SortMode _currentSortMode = SortMode.None;

    public SoloGameplayViewModel(
        ISoloGameplayEndpointsService endpointsService,
        IToastService toastService,
        NavigationManager navigationManager,
        IAudioService audioService)
    {
        _endpointsService = endpointsService;
        _toastService = toastService;
        _navigationManager = navigationManager;
        _audioService = audioService;
    }

    public void Dispose()
    {
        _timerToken?.Cancel();
        _timerToken?.Dispose();
    }

    public async Task StartGameAsync(string playerName, CancellationToken cancellationToken = default)
    {
        IsLoading = true;

        var result = await _endpointsService.StartGameAsync(
            new StartSoloGameReqDTO(playerName),
            cancellationToken
        );

        if (result.IsSuccess && result.Value is not null)
        {
            var response = result.Value;
            GameId = response.GameId;
            PlayerId = response.PlayerId;
            RoundNumber = response.RoundNumber;
            Score = response.Score;
            Wallet = response.Wallet;
            AvailablePlayHands = response.HandsAvailable;
            AvailableDiscards = response.DiscardsAvailable;
            Threshold = CalculateThreshold(RoundNumber);
            Phase = SoloGamePhase.InGame;

            RefreshCardsInHand(response.Cards);

            // Initialize timer
            RunTimeInSeconds = ChefKnifeStudios.PokerAttack.Shared.Constants.RoundTimeMs / 1000;
            StartTimer();
        }
        else
        {
            _toastService.ShowError("Failed to start game");
        }

        IsLoading = false;
    }

    public async Task PlaySelectedCardsAsync(CancellationToken cancellationToken = default)
    {
        if (Phase != SoloGamePhase.InGame) return;

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

        if (GameId is null) return;

        var request = new PlayHandReqDTO(
            PlayerId ?? "",
            selectedCards.Select(x => new CardDTO { Rank = x.Rank, Suit = x.Suit }).ToList()
        );

        var result = await _endpointsService.PlayHandAsync(GameId, request, cancellationToken);

        if (result.IsSuccess && result.Value is not null)
        {
            var response = result.Value;

            // Show hand result toast
            var handResult = response.HandResult;
            _toastService.ShowSuccess(
                $"({string.Join(" + ", handResult.CardValues)} + {handResult.BaseChips}) x {handResult.BaseMultiplier}",
                $"{handResult.HandType.GetDescription()} - {handResult.HandScore}"
            );

            Score = response.Score;
            Wallet = response.Wallet;
            AvailablePlayHands = response.HandsRemaining;

            RefreshCardsInHand(response.NewCards);
            ApplySort();

            // Play card dealing sounds
            await PlayCardDealSoundsAsync(selectedCards.Count);
        }
        else
        {
            _toastService.ShowError("Failed to play hand");
        }
    }

    public async Task DiscardSelectedCardsAsync(CancellationToken cancellationToken = default)
    {
        if (Phase != SoloGamePhase.InGame) return;

        if (AvailableDiscards <= 0)
        {
            _toastService.ShowWarning("No discards left");
            return;
        }

        var selectedCards = CardsInHand.Where(x => x.IsSelected).ToList();

        if (selectedCards.Count < 1) return;

        if (GameId is null) return;

        var request = new DiscardReqDTO(
            PlayerId ?? "",
            selectedCards.Select(x => new CardDTO { Rank = x.Rank, Suit = x.Suit }).ToList()
        );

        var result = await _endpointsService.DiscardAsync(GameId, request, cancellationToken);

        if (result.IsSuccess && result.Value is not null)
        {
            var response = result.Value;
            AvailableDiscards = response.DiscardsRemaining;

            RefreshCardsInHand(response.NewCards);
            ApplySort();

            // Play card dealing sounds
            await PlayCardDealSoundsAsync(selectedCards.Count);
        }
        else
        {
            _toastService.ShowError("Failed to discard");
        }
    }

    public async Task AdvancePhaseAsync(CancellationToken cancellationToken = default)
    {
        if (GameId is null) return;

        var result = await _endpointsService.AdvancePhaseAsync(GameId, cancellationToken);

        if (result.IsSuccess && result.Value is not null)
        {
            var response = result.Value;

            if (Enum.TryParse<SoloGamePhase>(response.NewPhase, out var newPhase))
            {
                Phase = newPhase;
            }

            // Handle phase-specific data
            if (response.NewCards is not null)
            {
                RefreshCardsInHand(response.NewCards);
                ApplySort();
            }

            if (response.HandsAvailable.HasValue)
            {
                AvailablePlayHands = response.HandsAvailable.Value;
            }

            if (response.DiscardsAvailable.HasValue)
            {
                AvailableDiscards = response.DiscardsAvailable.Value;
            }

            if (response.ShopItems is not null)
            {
                ShopItems.Clear();
                foreach (var item in response.ShopItems)
                {
                    ShopItems.Add(new ShopItem(item));
                }
            }

            // Update threshold and round for new rounds
            if (Phase == SoloGamePhase.InGame)
            {
                RoundNumber++;
                Score = 0;
                Threshold = CalculateThreshold(RoundNumber);
                RunTimeInSeconds = ChefKnifeStudios.PokerAttack.Shared.Constants.RoundTimeMs / 1000;
            }
        }
        else
        {
            _toastService.ShowError("Failed to advance phase");
        }
    }

    public async Task PurchaseItemAsync(string itemId, CancellationToken cancellationToken = default)
    {
        if (Phase != SoloGamePhase.Shop) return;
        if (GameId is null) return;

        var result = await _endpointsService.PurchaseShopItemAsync(GameId, itemId, cancellationToken);

        if (result.IsSuccess && result.Value is not null)
        {
            var response = result.Value;
            Wallet = response.RemainingWallet;

            // Mark item as purchased in the shop list
            var purchasedItem = ShopItems.FirstOrDefault(x => x.ItemId == itemId);
            if (purchasedItem is not null)
            {
                purchasedItem.WasPurchased = true;
            }

            _toastService.ShowSuccess($"Purchased {response.PurchasedItem.Item.Name}");
        }
        else
        {
            _toastService.ShowError("Failed to purchase item");
        }
    }

    public void ToggleCardSelection(int index)
    {
        if (Phase != SoloGamePhase.InGame) return;
        if (index < 0 || index >= CardsInHand.Count) return;
        CardsInHand[index].IsSelected = !CardsInHand[index].IsSelected;
        _ = _audioService.PlayOneShotAsync(FilePaths.PlaceCardThree);
    }

    public void SortByRank()
    {
        if (Phase != SoloGamePhase.InGame) return;
        _currentSortMode = SortMode.ByRank;
        ApplySort();
    }

    public void SortBySuit()
    {
        if (Phase != SoloGamePhase.InGame) return;
        _currentSortMode = SortMode.BySuit;
        ApplySort();
    }

    public void ClearSelections()
    {
        if (Phase != SoloGamePhase.InGame) return;
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

    void RefreshCardsInHand(IEnumerable<CardDTO> cards)
    {
        var cardItems = cards.Select(x => new CardItem(x));

        CardsInHand.Clear();
        foreach (var cardItem in cardItems)
        {
            CardsInHand.Add(cardItem);
        }
    }

    async Task PlayCardDealSoundsAsync(int cardCount)
    {
        for (int i = 0; i < cardCount; i++)
        {
            await _audioService.PlayOneShotAsync(FilePaths.SlideCardOne);
            await Task.Delay(150);
        }
    }

    static int CalculateThreshold(int roundNumber) => _BASE_THRESHOLD + (_THRESHOLD_INCREMENT * (roundNumber - 1));

    void StartTimer()
    {
        _timerToken = TimerHelper.SetInterval(() =>
        {
            if (Phase != SoloGamePhase.InGame) return;

            if (RunTimeInSeconds > 0)
            {
                RunTimeInSeconds--;
            }
            else
            {
                // Auto-end round when timer expires
                _ = AdvancePhaseAsync();
            }
        }, 1000);
    }
}
