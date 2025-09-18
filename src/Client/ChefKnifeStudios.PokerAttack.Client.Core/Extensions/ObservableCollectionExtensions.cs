using System.Collections.ObjectModel;

namespace ChefKnifeStudios.PokerAttack.Client.Core.Extensions;

public static class CollectionExtensions
{
    public static ObservableCollection<T> ToObservableCollection<T>(this IEnumerable<T> source)
    {
        return new ObservableCollection<T>(source);
    }
}
