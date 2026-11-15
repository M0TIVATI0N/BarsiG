namespace FinanceFlow.Web.State;

/// <summary>The answer of the sign in and of the registration endpoint.</summary>
/// <param name="Ok">Whether the operation was successful.</param>
/// <param name="Body">The raw answer of the server.</param>
public sealed record JsResponse(bool Ok, string Body);
