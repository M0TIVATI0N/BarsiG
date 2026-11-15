using FinanceFlow.Application.Abstractions;

using Microsoft.AspNetCore.SignalR;

namespace FinanceFlow.Infrastructure.RealTime;

/// <summary>
/// The hub that pushes information about changed data to the connected clients.
/// Every client joins a group named after the identifier of the signed in user,
/// so a notification reaches exactly the owner of the data.
/// </summary>
public sealed class NotificationsHub : Hub
{
    /// <summary>The name of the client method that receives a change notification.</summary>
    public const string DataChangedMethod = "dataChanged";

    /// <inheritdoc />
    public override async Task OnConnectedAsync()
    {
        string? userId = Context.UserIdentifier;

        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, userId);
        }

        await base.OnConnectedAsync();
    }

    /// <inheritdoc />
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        string? userId = Context.UserIdentifier;

        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, userId);
        }

        await base.OnDisconnectedAsync(exception);
    }
}

/// <summary>Publishes data changes through SignalR.</summary>
public sealed class SignalRDataChangeNotifier : IDataChangeNotifier
{
    private readonly IHubContext<NotificationsHub> _hubContext;

    /// <summary>Creates the notifier.</summary>
    public SignalRDataChangeNotifier(IHubContext<NotificationsHub> hubContext)
    {
        _hubContext = hubContext;
    }

    /// <inheritdoc />
    public async Task NotifyAsync(
        Guid userId,
        string entity,
        string action,
        CancellationToken cancellationToken = default)
    {
        DataChangeNotification notification = new(entity, action, DateTimeOffset.UtcNow);

        await _hubContext.Clients
            .Group(userId.ToString())
            .SendAsync(NotificationsHub.DataChangedMethod, notification, cancellationToken);
    }
}

/// <summary>The payload of a real time change notification.</summary>
/// <param name="Entity">The name of the changed entity, for example <c>transactions</c>.</param>
/// <param name="Action">The performed action, for example <c>created</c>.</param>
/// <param name="OccurredAtUtc">The moment of the change.</param>
public sealed record DataChangeNotification(string Entity, string Action, DateTimeOffset OccurredAtUtc);
