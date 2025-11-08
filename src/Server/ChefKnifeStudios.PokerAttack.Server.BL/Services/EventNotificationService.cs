using System.Linq;
using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.PokerAttack.Server.BL.Services;

public delegate Task EventReceivedEventHandler(object sender, IEventArgs e);

public interface IEventNotificationService
{
    event EventReceivedEventHandler? EventReceived;
    void PostEvent(object sender, IEventArgs args);
}

public interface IEventArgs { }

public class EventNotificationService : IEventNotificationService
{
    readonly ILogger<EventNotificationService> _logger;

    public EventNotificationService(ILogger<EventNotificationService> logger)
    {
        _logger = logger;
    }

    public event EventReceivedEventHandler? EventReceived;

    public void PostEvent(object sender, IEventArgs args)
    {
        var handler = EventReceived;
        if (handler == null) return;

        // Capture invocation list to avoid race with subscribe/unsubscribe
        var invocationList = handler.GetInvocationList()
            .OfType<EventReceivedEventHandler>()
            .ToArray();

        // Invoke handlers and capture tasks (synchronous exceptions are caught)
        var tasks = invocationList.Select(h =>
        {
            try
            {
                return h.Invoke(sender, args) ?? Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Synchronous exception invoking event handler.");
                return Task.CompletedTask;
            }
        }).ToArray();

        // Fire-and-forget, but observe exceptions and log them
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // This will capture exceptions thrown by any handler Task
                _logger.LogError(ex, "One or more event handlers threw an exception.");
            }
        });
    }
}