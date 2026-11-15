using FinanceFlow.Application.Dtos;

namespace FinanceFlow.Web.State;

/// <summary>
/// Holds the session of the signed in user for the lifetime of the circuit.
/// It is published to the component tree with a cascading value.
/// </summary>
public sealed class SessionState : StateContainer<SessionInfo>
{
    /// <summary>Creates an empty session.</summary>
    public SessionState()
        : base(SessionInfo.Anonymous)
    {
    }

    /// <summary>Returns <c>true</c> when a user is signed in.</summary>
    public bool IsAuthenticated => State.IsAuthenticated;

    /// <summary>Stores the session of a signed in user.</summary>
    public void SignIn(UserSession session) =>
        State = new SessionInfo
        {
            IsAuthenticated = true,
            UserId = session.UserId,
            Email = session.Email,
            DisplayName = session.DisplayName,
            Role = session.Role
        };

    /// <summary>Clears the session.</summary>
    public void SignOut() => State = SessionInfo.Anonymous;
}
