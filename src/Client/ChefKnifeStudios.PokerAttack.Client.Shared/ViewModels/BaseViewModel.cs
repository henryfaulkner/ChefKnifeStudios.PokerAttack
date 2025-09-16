using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;

public interface IViewModel : INotifyPropertyChanged
{
}

// Base class using source generators
public partial class BaseViewModel : ObservableObject, IViewModel
{
    // Source generators automatically implement INotifyPropertyChanged
}
