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
    public string _id = "";
    [ObservableProperty]
    public string _name = "";
    [ObservableProperty]
    public string _description = "";
    [ObservableProperty]
    public int _pointCost;
    [ObservableProperty]
    public bool _isSelected = false;
}
