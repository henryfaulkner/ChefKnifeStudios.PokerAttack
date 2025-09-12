namespace ChefKnifeStudios.PokerAttack.Client.Core.Services;

public delegate Task EventReceivedEventHandler(
    object sender, IEventArgs e);

public interface IEventNotificationService
{
    event EventReceivedEventHandler? EventReceived;
    void PostEvent(object sender, IEventArgs args);
}

public interface IEventArgs
{
}

public class EventNotificationService : IEventNotificationService
{
    public event EventReceivedEventHandler? EventReceived;

    public void PostEvent(object sender, IEventArgs args)
    {
        EventReceived?.Invoke(sender, args);
    }
}
