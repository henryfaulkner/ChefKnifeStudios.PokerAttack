using ChefKnifeStudios.PokerAttack.Shared.DTOs;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Models;

public partial class PlayerPowerListItem : ObservableObject
{
    public PlayerPowerListItem(PlayerPowerDTO playerPower)
    {
        Id = playerPower.Id;
        Name = playerPower.Name;
        Description = playerPower.Description;
        PointCost = playerPower.PointCost;
        IsSelected = false;
    }

    [ObservableProperty]
    string _id = "";
    [ObservableProperty]
    string _name = "";
    [ObservableProperty]
    string _description = "";
    [ObservableProperty]
    int _pointCost;
    [ObservableProperty]
    bool _isSelected = false;
}
