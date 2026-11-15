using Microsoft.AspNetCore.Antiforgery;

namespace FinanceFlow.Web;

/// <summary>
/// The metadata that tells the antiforgery middleware to validate the token of a request.
/// It is the same behaviour as the <c>RequireAntiforgery</c> convention of the framework:
/// every request that changes the state of the bank has to carry a valid token.
/// </summary>
internal sealed class RequireAntiforgeryMetadata : IAntiforgeryMetadata
{
    /// <inheritdoc />
    public bool RequiresValidation
    {
        get => true;
        set { }
    }
}
