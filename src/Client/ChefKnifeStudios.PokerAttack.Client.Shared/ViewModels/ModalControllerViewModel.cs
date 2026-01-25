using ChefKnifeStudios.PokerAttack.Client.Core.Services;
using ChefKnifeStudios.PokerAttack.Client.Shared.EventArgs.ModalEvents;
using ChefKnifeStudios.PokerAttack.Client.Shared.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;

public interface IModalControllerViewModel : IViewModel
{
    bool IsOpen { get; }
    ModalType CurrentModal { get; }
    object? ModalData { get; }
}

public class ModalControllerViewModel : BaseViewModel, IModalControllerViewModel, IDisposable
{
    readonly IEventNotificationService _eventService;
    readonly NavigationManager _navigation;

    bool _isOpen;
    public bool IsOpen
    {
        get => _isOpen;
        private set => SetProperty(ref _isOpen, value);
    }

    ModalType _currentModal;
    public ModalType CurrentModal
    {
        get => _currentModal;
        private set => SetProperty(ref _currentModal, value);
    }

    object? _modalData;
    public object? ModalData
    {
        get => _modalData;
        private set => SetProperty(ref _modalData, value);
    }

    string _currentPath;

    public ModalControllerViewModel(
        IEventNotificationService eventService,
        NavigationManager navigation)
    {
        _eventService = eventService;
        _navigation = navigation;
        _currentPath = new Uri(navigation.Uri).AbsolutePath;

        _eventService.EventReceived += OnEventReceived;
        _navigation.LocationChanged += OnLocationChanged;
    }

    Task OnEventReceived(object sender, IEventArgs e)
    {
        if (e is not ModalEventArgs modal)
            return Task.CompletedTask;

        switch (modal.ModalAction)
        {
            case ModalEventArgs.ModalActions.Open:
                OpenModal(modal);
                break;

            case ModalEventArgs.ModalActions.Close:
                CloseModal();
                break;
        }

        return Task.CompletedTask;
    }

    void OpenModal(ModalEventArgs args)
    {
        CurrentModal = GetModalTypeFromEvent(args);
        if (CurrentModal == ModalType.None) return;

        ModalData = args switch
        {
            MultiGameResultModalEventArgs multiGame => multiGame.GameResult,
            _ => null
        };

        IsOpen = true;
    }

    void CloseModal()
    {
        IsOpen = false;
        CurrentModal = ModalType.None;
        ModalData = null;
    }

    void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        var uri = new Uri(e.Location);
        var newPath = uri.AbsolutePath;

        if (_currentPath != newPath)
        {
            _currentPath = newPath;

            // Don't close if navigating TO a page that wants to show a modal
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            if (query["multi-gameresult"] != null || query["solo-gameresult"] != null)
                return;

            CloseModal();
        }
    }

    static ModalType GetModalTypeFromEvent(IEventArgs args) => args switch
    {
        ScoringRulesModalEventArgs => ModalType.ScoringRules,
        MultiGameResultModalEventArgs => ModalType.MultiGameResult,
        SoloGameResultModalEventArgs => ModalType.SoloGameResult,
        HowToPlayModalEventArgs => ModalType.HowToPlay,
        _ => ModalType.None
    };

    public void Dispose()
    {
        _eventService.EventReceived -= OnEventReceived;
        _navigation.LocationChanged -= OnLocationChanged;
    }
}
