using Microsoft.JSInterop;

namespace FinanceFlow.Web.State;

/// <summary>
/// Connects the browser to the SignalR hub of the application.
/// The server pushes a notification when the data of the user changes,
/// and the pages that subscribed refresh themselves.
/// </summary>
public sealed class RealtimeService : IAsyncDisposable
{
    private readonly IJSRuntime _jsRuntime;
    private DotNetObjectReference<RealtimeService>? _selfReference;
    private bool _started;

    /// <summary>Creates the service.</summary>
    public RealtimeService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    /// <summary>Raised when the server reports a changed piece of data.</summary>
    public event Func<string, string, Task>? DataChanged;

    /// <summary>Starts the connection. Calling it twice has no effect.</summary>
    public async Task StartAsync()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _selfReference = DotNetObjectReference.Create(this);

        try
        {
            await _jsRuntime.InvokeVoidAsync("financeFlow.connectRealtime", _selfReference);
        }
        catch (JSException)
        {
            // The real time connection is optional, the application works without it.
            _started = false;
        }
        catch (JSDisconnectedException)
        {
            _started = false;
        }
    }

    /// <summary>Called from the browser when the server reports a change.</summary>
    [JSInvokable]
    public Task OnDataChanged(string entity, string action) =>
        DataChanged?.Invoke(entity, action) ?? Task.CompletedTask;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("financeFlow.disconnectRealtime");
        }
        catch (JSException)
        {
            // The circuit is already closed, nothing has to be cleaned up.
        }
        catch (JSDisconnectedException)
        {
            // The circuit is already closed, nothing has to be cleaned up.
        }
        catch (InvalidOperationException)
        {
            // The component was rendered statically, so there is no browser to talk to.
        }

        _selfReference?.Dispose();
        _selfReference = null;
    }
}
