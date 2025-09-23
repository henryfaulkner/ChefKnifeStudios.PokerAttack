using ChefKnifeStudios.PokerAttack.Client.Core.Services;
using ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;
using Microsoft.AspNetCore.Components;

namespace ChefKnifeStudios.PokerAttack.Client.WebApp;

public partial class App : ComponentBase
{
    [Inject] IApplicationViewModel ApplicationViewModel { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        await ApplicationViewModel.InitAsync();
    }
}
