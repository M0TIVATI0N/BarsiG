using System.Security.Claims;

using Microsoft.AspNetCore.Components.Authorization;

namespace FinanceFlow.Web.State;

/// <summary>
/// Gives the pages access to the identifier of the signed in user.
/// The identifier is read from the authentication state, so a page can never
/// work with the data of another user by mistake.
/// </summary>
public sealed class UserContext
{
    private readonly AuthenticationStateProvider _authenticationStateProvider;

    /// <summary>Creates the context.</summary>
    public UserContext(AuthenticationStateProvider authenticationStateProvider)
    {
        _authenticationStateProvider = authenticationStateProvider;
    }

    /// <summary>Returns the identifier of the signed in user.</summary>
    public async Task<Guid> GetUserIdAsync()
    {
        AuthenticationState state = await _authenticationStateProvider.GetAuthenticationStateAsync();
        string? value = state.User.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(value, out Guid userId) ? userId : Guid.Empty;
    }

    /// <summary>
    /// Returns the moment of the last successful sign in.
    /// The interface uses it to know whether the session is still fresh enough
    /// for a sensitive operation.
    /// </summary>
    public async Task<DateTimeOffset?> GetAuthenticatedAtAsync()
    {
        AuthenticationState state = await _authenticationStateProvider.GetAuthenticationStateAsync();
        string? value = state.User.FindFirstValue("auth_time");

        return long.TryParse(value, out long seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
    }
}
