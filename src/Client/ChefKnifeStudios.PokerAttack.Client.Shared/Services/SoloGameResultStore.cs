using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Services;

public interface ISoloGameResultStore : INotifyPropertyChanged
{
    bool HasResult { get; }
    int RoundReached { get; }
    int FinalWallet { get; }
    int TotalScore { get; }
    int HandsPlayed { get; }
    int DiscardsUsed { get; }
    int HighestSingleHandScore { get; }
    List<string> PurchasedItemNames { get; }
    void SetResult(SoloGameResultData data);
    void Clear();
}

public record SoloGameResultData(
    int RoundReached,
    int FinalWallet,
    int TotalScore,
    int HandsPlayed,
    int DiscardsUsed,
    int HighestSingleHandScore,
    List<string> PurchasedItemNames
);

public partial class SoloGameResultStore : ObservableObject, ISoloGameResultStore
{
    [ObservableProperty]
    bool _hasResult;

    [ObservableProperty]
    int _roundReached;

    [ObservableProperty]
    int _finalWallet;

    [ObservableProperty]
    int _totalScore;

    [ObservableProperty]
    int _handsPlayed;

    [ObservableProperty]
    int _discardsUsed;

    [ObservableProperty]
    int _highestSingleHandScore;

    [ObservableProperty]
    List<string> _purchasedItemNames = [];

    public void SetResult(SoloGameResultData data)
    {
        RoundReached = data.RoundReached;
        FinalWallet = data.FinalWallet;
        TotalScore = data.TotalScore;
        HandsPlayed = data.HandsPlayed;
        DiscardsUsed = data.DiscardsUsed;
        HighestSingleHandScore = data.HighestSingleHandScore;
        PurchasedItemNames = data.PurchasedItemNames;
        HasResult = true;
    }

    public void Clear()
    {
        HasResult = false;
        RoundReached = 0;
        FinalWallet = 0;
        TotalScore = 0;
        HandsPlayed = 0;
        DiscardsUsed = 0;
        HighestSingleHandScore = 0;
        PurchasedItemNames = [];
    }
}
