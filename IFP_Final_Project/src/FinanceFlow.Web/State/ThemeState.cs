namespace FinanceFlow.Web.State;

/// <summary>The colour scheme of the interface.</summary>
public enum ThemeMode
{
    /// <summary>The light colour scheme.</summary>
    Light = 0,

    /// <summary>The dark colour scheme.</summary>
    Dark = 1
}

/// <summary>
/// Holds the colour scheme of the interface. The chosen scheme is stored in the browser,
/// so it survives a reload of the page.
/// </summary>
public sealed class ThemeState : StateContainer<ThemeMode>
{
    /// <summary>Creates the container with the light colour scheme.</summary>
    public ThemeState()
        : base(ThemeMode.Light)
    {
    }

    /// <summary>Switches between the light and the dark colour scheme.</summary>
    public void Toggle() => State = State == ThemeMode.Light ? ThemeMode.Dark : ThemeMode.Light;

    /// <summary>Applies a known colour scheme.</summary>
    public void Apply(ThemeMode mode) => State = mode;
}
