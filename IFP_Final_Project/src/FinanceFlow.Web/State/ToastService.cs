namespace FinanceFlow.Web.State;

/// <summary>The kind of a toast message.</summary>
public enum ToastKind
{
    /// <summary>A successful operation.</summary>
    Success = 0,

    /// <summary>A problem the user should know about.</summary>
    Error = 1,

    /// <summary>Neutral information.</summary>
    Info = 2
}

/// <summary>One toast message.</summary>
/// <param name="Id">The unique identifier of the message.</param>
/// <param name="Kind">The kind of the message.</param>
/// <param name="Title">The short headline of the message.</param>
/// <param name="Message">The detailed text of the message.</param>
public sealed record ToastMessage(Guid Id, ToastKind Kind, string Title, string Message);

/// <summary>
/// Collects the short messages that are shown in the corner of the interface.
/// The service is a state container, so the host component re-renders automatically.
/// </summary>
public sealed class ToastService : StateContainer<IReadOnlyList<ToastMessage>>
{
    /// <summary>Creates the service with an empty list of messages.</summary>
    public ToastService()
        : base(Array.Empty<ToastMessage>())
    {
    }

    /// <summary>Adds a success message.</summary>
    public void Success(string title, string message) => Add(ToastKind.Success, title, message);

    /// <summary>Adds an error message.</summary>
    public void Error(string title, string message) => Add(ToastKind.Error, title, message);

    /// <summary>Adds an informational message.</summary>
    public void Info(string title, string message) => Add(ToastKind.Info, title, message);

    /// <summary>Removes a message from the list.</summary>
    public void Remove(Guid id) => State = State.Where(message => message.Id != id).ToList();

    private void Add(ToastKind kind, string title, string message)
    {
        ToastMessage toast = new(Guid.NewGuid(), kind, title, message);
        State = [.. State, toast];
    }
}
