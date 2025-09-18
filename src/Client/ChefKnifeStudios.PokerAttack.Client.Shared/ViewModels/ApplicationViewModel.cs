using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;

public interface IApplicationViewModel : IViewModel
{
    string PlayerId { get; }
}

public partial class ApplicationViewModel : BaseViewModel, IApplicationViewModel
{
    [ObservableProperty]
    string _playerId = Guid.NewGuid().ToString();
}
