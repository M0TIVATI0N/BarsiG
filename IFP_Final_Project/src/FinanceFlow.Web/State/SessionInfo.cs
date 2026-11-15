namespace FinanceFlow.Web.State;

/// <summary>
/// The information about the signed in user that the interface needs.
/// The type is never null, so a component can always read it safely.
/// </summary>
public sealed record SessionInfo
{
    /// <summary>The state of an anonymous visitor.</summary>
    public static SessionInfo Anonymous { get; } = new();

    /// <summary>Whether a user is signed in.</summary>
    public bool IsAuthenticated { get; init; }

    /// <summary>The identifier of the signed in user.</summary>
    public Guid UserId { get; init; }

    /// <summary>The e-mail address of the signed in user.</summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>The display name of the signed in user.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>The role of the user inside the Demo Bank, <c>User</c> or <c>Admin</c>.</summary>
    public string Role { get; init; } = "User";

    /// <summary>Returns <c>true</c> when the user is an administrator of the Demo Bank.</summary>
    public bool IsAdmin => string.Equals(Role, "Admin", StringComparison.OrdinalIgnoreCase);
}
