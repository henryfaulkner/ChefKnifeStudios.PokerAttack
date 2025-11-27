using ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;
using ChefKnifeStudios.PokerAttack.Shared.DTOs;
using ChefKnifeStudios.PokerAttack.Shared.Enums;
using Microsoft.AspNetCore.Components;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Components;

public partial class ScoringRulesModal : ComponentBase, IDisposable
{
    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    [Inject] IScoringRulesViewModel ScoringRulesViewModel { get; set; } = null!;

    readonly string[] _subscriptions =
    [
        nameof(IScoringRulesViewModel.IsLoading),
        nameof(IScoringRulesViewModel.ScoringRules),
    ];

    protected override void OnInitialized()
    {
        base.OnInitialized();
        ScoringRulesViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
    }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        await ScoringRulesViewModel.LoadAsync();
    }

    public void Dispose()
    {
        ScoringRulesViewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
    }

    string GetHandTypeName(PokerHandType handType) => handType.GetDescription() ?? string.Empty;

    void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null || _subscriptions.Contains(e.PropertyName) is false) return;
        Task.Run(async () => await InvokeAsync(StateHasChanged));
    }
}
