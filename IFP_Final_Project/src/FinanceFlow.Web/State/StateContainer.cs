using System.ComponentModel;

namespace FinanceFlow.Web.State;

/// <summary>
/// A small state container that follows the container pattern.
/// The state itself is immutable: every change replaces the whole value and raises an event,
/// so the user interface always renders a consistent snapshot.
/// </summary>
/// <typeparam name="TState">The type of the state.</typeparam>
public abstract class StateContainer<TState> : INotifyPropertyChanged
{
    private TState _state;

    /// <summary>Creates the container with its initial state.</summary>
    protected StateContainer(TState initialState)
    {
        _state = initialState;
    }

    /// <summary>The current state of the container.</summary>
    public TState State
    {
        get => _state;
        protected set
        {
            _state = value;
            NotifyStateChanged();
        }
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raises the change event so that the components can render the new state.</summary>
    protected void NotifyStateChanged() =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(State)));
}
