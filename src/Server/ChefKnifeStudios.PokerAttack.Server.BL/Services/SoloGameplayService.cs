using Ardalis.Result;
using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Shared;
using ChefKnifeStudios.PokerAttack.Shared.DTOs;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Gameplay;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.SoloGameplay;
using ChefKnifeStudios.PokerAttack.Shared.Enums;
using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.PokerAttack.Server.BL.Services;

public interface ISoloGameplayService
{
    Task<Result<StartSoloGameResDTO>> StartGameAsync(StartSoloGameReqDTO request, CancellationToken ct = default);
    Task<Result<PlayHandResDTO>> PlayHandAsync(string gameId, PlayHandReqDTO request, CancellationToken ct = default);
    Task<Result<DiscardResDTO>> DiscardAsync(string gameId, DiscardReqDTO request, CancellationToken ct = default);
    Task<Result<SoloGameStateDTO>> GetGameStateAsync(string gameId, CancellationToken ct = default);
    Task<Result<PurchaseItemResDTO>> PurchaseShopItemAsync(string gameId, string shopItemId, CancellationToken ct = default);
    Task<Result<AdvancePhaseResDTO>> AdvancePhaseAsync(string gameId, CancellationToken ct = default);
}

public class SoloGameplayService(
    ILogger<SoloGameplayService> logger,
    IKeyValueRepository<SoloGame> soloGameRepository,
    IScoringRulesService scoringRulesService,
    IItemEffectsService itemEffectsService,
    IWagerService wagerService,
    IItemRepository itemRepository) : ISoloGameplayService
{
    const int NumCardsInHand = 8;
    const int BaseHandsAvailable = 5;
    const int BaseDiscardsAvailable = 5;

    public async Task<Result<StartSoloGameResDTO>> StartGameAsync(StartSoloGameReqDTO request, CancellationToken ct = default)
    {
        var gameId = Guid.NewGuid().ToString();
        var playerId = Guid.NewGuid().ToString();

        var deck = new Deck();
        deck.RandomizeDeck();

        var soloGame = new SoloGame
        {
            Id = gameId,
            PlayerId = playerId,
            PlayerName = request.PlayerName,
            Deck = deck,
            Phase = SoloGamePhase.InGame,
            RoundNumber = 1,
            HandsRemaining = BaseHandsAvailable,
            DiscardsRemaining = BaseDiscardsAvailable,
        };

        // Deal initial hand
        for (int i = 0; i < NumCardsInHand; i++)
        {
            soloGame.CardsInHand.Add(deck.PullCard());
        }

        var addResult = await soloGameRepository.AddAsync(gameId, soloGame, ct);
        if (!addResult.IsSuccess)
            return Result.Error("Failed to create solo game.");

        logger.LogInformation(
            "Solo game started: GameId={GameId}, PlayerId={PlayerId}, PlayerName={PlayerName}",
            gameId, playerId, request.PlayerName);

        return Result.Success(new StartSoloGameResDTO(
            GameId: gameId,
            PlayerId: playerId,
            Cards: soloGame.CardsInHand.Select(c => c.MapToDTO()),
            HandsAvailable: soloGame.HandsRemaining,
            DiscardsAvailable: soloGame.DiscardsRemaining,
            Wallet: soloGame.Wallet,
            Score: soloGame.Score,
            RoundNumber: soloGame.RoundNumber
        ));
    }

    public async Task<Result<PlayHandResDTO>> PlayHandAsync(string gameId, PlayHandReqDTO request, CancellationToken ct = default)
    {
        var gameResult = await soloGameRepository.GetAsync(gameId, ct);
        if (!gameResult.IsSuccess || gameResult.Value is null)
            return Result.NotFound($"Solo game not found: {gameId}");

        var game = gameResult.Value;

        if (game.Phase != SoloGamePhase.InGame)
            return Result.Invalid(new ValidationError("Cannot play hand outside of InGame phase"));

        if (game.HandsRemaining <= 0)
            return Result.Invalid(new ValidationError("No hands remaining"));

        var hand = request.Hand.Select(c => c.MapToModel()).ToList();

        // Remove cards from hand
        foreach (var card in hand)
        {
            var match = game.CardsInHand.FirstOrDefault(c => c.Rank == card.Rank && c.Suit == card.Suit);
            if (match == null)
                return Result.Invalid(new ValidationError($"Card not in hand: {card.Rank} of {card.Suit}"));
            game.CardsInHand.Remove(match);
        }

        // Evaluate hand
        var handEvaluator = new HandEvaluator(scoringRulesService);
        var evalResult = handEvaluator.EvaluateHand(hand);
        if (!evalResult.IsSuccess)
            return Result.Error("Failed to evaluate hand.");

        var baseResult = evalResult.Value;

        // Apply item effects
        var handResult = itemEffectsService.ApplyItemEffects(baseResult, hand, game.ActiveItems);

        // Check wagers
        var completedWagers = wagerService.CheckWagers(game.ActiveWagers, handResult, hand);
        int wagerChips = 0;
        foreach (var wagerResult in completedWagers)
        {
            wagerChips += wagerResult.ChipsAwarded;
            wagerResult.Wager.IsCompleted = true;
        }

        // Update score and wallet
        game.Score += handResult.HandScore;
        game.Wallet += handResult.HandScore + wagerChips;
        game.HandsRemaining--;

        // Replenish hand
        ReplenishHand(game);

        var updateResult = await soloGameRepository.UpdateAsync(gameId, game, ct);
        if (!updateResult.IsSuccess)
            return Result.Error("Failed to update solo game.");

        logger.LogInformation(
            "Hand played: GameId={GameId}, HandType={HandType}, Score={Score}, TotalScore={TotalScore}",
            gameId, handResult.HandType, handResult.HandScore, game.Score);

        return Result.Success(new PlayHandResDTO(
            HandResult: handResult.MapToDTO(game.Score),
            NewCards: game.CardsInHand.Select(c => c.MapToDTO()),
            HandsRemaining: game.HandsRemaining,
            Wallet: game.Wallet,
            Score: game.Score
        ));
    }

    public async Task<Result<DiscardResDTO>> DiscardAsync(string gameId, DiscardReqDTO request, CancellationToken ct = default)
    {
        var gameResult = await soloGameRepository.GetAsync(gameId, ct);
        if (!gameResult.IsSuccess || gameResult.Value is null)
            return Result.NotFound($"Solo game not found: {gameId}");

        var game = gameResult.Value;

        if (game.Phase != SoloGamePhase.InGame)
            return Result.Invalid(new ValidationError("Cannot discard outside of InGame phase"));

        if (game.DiscardsRemaining <= 0)
            return Result.Invalid(new ValidationError("No discards remaining"));

        var discardCards = request.DiscardCards.Select(c => c.MapToModel()).ToList();

        // Remove discarded cards
        foreach (var card in discardCards)
        {
            var match = game.CardsInHand.FirstOrDefault(c => c.Rank == card.Rank && c.Suit == card.Suit);
            if (match == null)
                return Result.Invalid(new ValidationError($"Card not in hand: {card.Rank} of {card.Suit}"));
            game.CardsInHand.Remove(match);
        }

        game.DiscardsRemaining--;

        // Replenish hand
        ReplenishHand(game);

        var updateResult = await soloGameRepository.UpdateAsync(gameId, game, ct);
        if (!updateResult.IsSuccess)
            return Result.Error("Failed to update solo game.");

        logger.LogInformation(
            "Cards discarded: GameId={GameId}, Count={Count}, DiscardsRemaining={DiscardsRemaining}",
            gameId, discardCards.Count, game.DiscardsRemaining);

        return Result.Success(new DiscardResDTO(
            NewCards: game.CardsInHand.Select(c => c.MapToDTO()),
            DiscardsRemaining: game.DiscardsRemaining
        ));
    }

    public async Task<Result<SoloGameStateDTO>> GetGameStateAsync(string gameId, CancellationToken ct = default)
    {
        var gameResult = await soloGameRepository.GetAsync(gameId, ct);
        if (!gameResult.IsSuccess || gameResult.Value is null)
            return Result.NotFound($"Solo game not found: {gameId}");

        var game = gameResult.Value;

        return Result.Success(new SoloGameStateDTO(
            GameId: game.Id,
            PlayerId: game.PlayerId,
            Cards: game.CardsInHand.Select(c => c.MapToDTO()),
            HandsAvailable: game.HandsRemaining,
            DiscardsAvailable: game.DiscardsRemaining,
            Wallet: game.Wallet,
            Score: game.Score,
            RoundNumber: game.RoundNumber,
            Phase: game.Phase.ToString()
        ));
    }

    public async Task<Result<PurchaseItemResDTO>> PurchaseShopItemAsync(string gameId, string shopItemId, CancellationToken ct = default)
    {
        var gameResult = await soloGameRepository.GetAsync(gameId, ct);
        if (!gameResult.IsSuccess || gameResult.Value is null)
            return Result.NotFound($"Solo game not found: {gameId}");

        var game = gameResult.Value;

        if (game.Phase != SoloGamePhase.Shop)
            return Result.Invalid(new ValidationError("Cannot purchase outside of Shop phase"));

        var itemResult = itemRepository.Get(shopItemId);
        if (!itemResult.IsSuccess || itemResult.Value is null)
            return Result.NotFound($"Item not found: {shopItemId}");

        var item = itemResult.Value;

        // Calculate adjusted price: 15% increase per round
        var adjustedPrice = (int)(item.Price * (1 + 0.15 * game.RoundNumber));

        if (game.Wallet < adjustedPrice)
            return Result.Invalid(new ValidationError($"Insufficient funds. Wallet: {game.Wallet}, Price: {adjustedPrice}"));

        game.Wallet -= adjustedPrice;
        game.PurchasedItems.Add(item);

        var updateResult = await soloGameRepository.UpdateAsync(gameId, game, ct);
        if (!updateResult.IsSuccess)
            return Result.Error("Failed to update solo game.");

        logger.LogInformation(
            "Item purchased: GameId={GameId}, ItemId={ItemId}, Price={Price}, RemainingWallet={Wallet}",
            gameId, shopItemId, adjustedPrice, game.Wallet);

        return Result.Success(new PurchaseItemResDTO(
            PurchasedItem: new ShopItemDTO { Item = item, AdjustedPrice = adjustedPrice },
            RemainingWallet: game.Wallet
        ));
    }

    public async Task<Result<AdvancePhaseResDTO>> AdvancePhaseAsync(string gameId, CancellationToken ct = default)
    {
        var gameResult = await soloGameRepository.GetAsync(gameId, ct);
        if (!gameResult.IsSuccess || gameResult.Value is null)
            return Result.NotFound($"Solo game not found: {gameId}");

        var game = gameResult.Value;

        var previousPhase = game.Phase;
        SoloGamePhase newPhase;
        IEnumerable<CardDTO>? newCards = null;
        int? handsAvailable = null;
        int? discardsAvailable = null;
        IEnumerable<ShopItemDTO>? shopItems = null;

        switch (game.Phase)
        {
            case SoloGamePhase.InGame:
                // Transition to Scoreboard
                newPhase = SoloGamePhase.Scoreboard;
                game.Phase = newPhase;
                break;

            case SoloGamePhase.Scoreboard:
                // Check threshold
                var threshold = game.GetCurrentThreshold();
                if (game.Score >= threshold)
                {
                    // Player survived - go to Shop
                    newPhase = SoloGamePhase.Shop;
                    game.Phase = newPhase;

                    // Get shop items
                    var itemsResult = itemRepository.GetRandomNumber(3);
                    if (itemsResult.IsSuccess && itemsResult.Value is not null)
                    {
                        shopItems = itemsResult.Value.Select(item => new ShopItemDTO
                        {
                            Item = item,
                            AdjustedPrice = (int)(item.Price * (1 + 0.15 * game.RoundNumber))
                        });
                    }
                }
                else
                {
                    // Player failed - go directly to GameOver (no elimination phase needed in solo)
                    newPhase = SoloGamePhase.GameOver;
                    game.Phase = newPhase;
                }
                break;

            case SoloGamePhase.Shop:
                // Start new round
                game.RoundNumber++;
                game.Score = 0; // Reset score for new round
                game.Phase = SoloGamePhase.InGame;
                newPhase = SoloGamePhase.InGame;

                // Reset deck and deal new hand
                game.Deck = new Deck();
                game.Deck.RandomizeDeck();
                game.CardsInHand.Clear();

                // Calculate hands/discards with item buffs
                int handsBuff = itemEffectsService.GetHandsAvailableBuff(game.ActiveItems);
                int discardsBuff = itemEffectsService.GetDiscardsAvailableBuff(game.ActiveItems);
                game.HandsRemaining = BaseHandsAvailable + handsBuff;
                game.DiscardsRemaining = BaseDiscardsAvailable + discardsBuff;

                // Deal new hand
                for (int i = 0; i < NumCardsInHand; i++)
                {
                    game.CardsInHand.Add(game.Deck.PullCard());
                }

                newCards = game.CardsInHand.Select(c => c.MapToDTO());
                handsAvailable = game.HandsRemaining;
                discardsAvailable = game.DiscardsRemaining;
                break;

            case SoloGamePhase.GameOver:
                return Result.Invalid(new ValidationError("Game is already over"));

            default:
                return Result.Error("Unknown game phase");
        }

        var updateResult = await soloGameRepository.UpdateAsync(gameId, game, ct);
        if (!updateResult.IsSuccess)
            return Result.Error("Failed to update solo game.");

        logger.LogInformation(
            "Phase advanced: GameId={GameId}, From={FromPhase}, To={ToPhase}, Round={Round}",
            gameId, previousPhase, newPhase, game.RoundNumber);

        return Result.Success(new AdvancePhaseResDTO(
            NewPhase: newPhase.ToString(),
            NewCards: newCards,
            HandsAvailable: handsAvailable,
            DiscardsAvailable: discardsAvailable,
            ShopItems: shopItems
        ));
    }

    void ReplenishHand(SoloGame game)
    {
        int numCardsToAdd = NumCardsInHand - game.CardsInHand.Count;
        for (int i = 0; i < numCardsToAdd; i++)
        {
            game.CardsInHand.Add(game.Deck.PullCard());
        }
    }
}
