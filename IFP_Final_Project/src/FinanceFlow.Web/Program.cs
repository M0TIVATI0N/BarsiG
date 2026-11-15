using System.Security.Claims;

using FinanceFlow.Application;
using FinanceFlow.Application.Dtos;
using FinanceFlow.Domain.Common;
using FinanceFlow.Infrastructure;
using FinanceFlow.Infrastructure.Persistence;
using FinanceFlow.Infrastructure.RealTime;
using FinanceFlow.Infrastructure.Seeding;
using FinanceFlow.Web;
using FinanceFlow.Web.Components;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

WebApplication app = Program.CreateApp(args);
await Program.PrepareAsync(app);

app.Run();

public partial class Program
{
    /// <summary>Builds the application of BarsiG with every service and every endpoint.</summary>
    public static WebApplication CreateApp(string[] args)
    {
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Configuration["Runtime:Environment"] = builder.Environment.EnvironmentName;

// The static web assets of the build have to be served in every environment and not only
// in the development one. They are the files of the framework of Blazor
// (/_framework/blazor.web.js), the stylesheet of the components and the compressed files.
// Without them the server answers 404 for them, the interface of the simulator stays a
// dead page and the forms of the sign in are sent without the token of the antiforgery.
builder.WebHost.UseStaticWebAssets();

// ---------------------------------------------------------------------------------------------
// Presentation layer: Blazor Web App with interactive server components.
// ---------------------------------------------------------------------------------------------
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.AccessDeniedPath = "/denied";
        options.Cookie.Name = "BarsiG.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Admin", policy => policy.RequireRole("Admin"));
});

// The rate limiter protects the endpoints that move money or check a password.
// The limits are configurable, so a server behind one address can serve many clients.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    int signInLimit = builder.Configuration.GetValue("Banking:RateLimit:SignInPerMinute", 60);
    int moneyLimit = builder.Configuration.GetValue("Banking:RateLimit:MoneyPerMinute", 120);

    options.AddFixedWindowLimiter("sign-in", limiter =>
    {
        // The protection against a brute force is the throttle of the account itself
        // (five wrong passwords block the account for five minutes). The limiter of the
        // server only stops a flood of requests, therefore the limit is generous enough
        // for several clients behind one address.
        limiter.PermitLimit = signInLimit;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });

    options.AddFixedWindowLimiter("money", limiter =>
    {
        limiter.PermitLimit = moneyLimit;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });

});

// ---------------------------------------------------------------------------------------------
// Application layer: use cases that speak in terms of Result<T>.
// ---------------------------------------------------------------------------------------------
builder.Services.AddApplication();

// ---------------------------------------------------------------------------------------------
// Infrastructure layer: SQLite, repositories, SignalR and the background worker.
// ---------------------------------------------------------------------------------------------
builder.Services.AddInfrastructure(builder.Configuration);

// ---------------------------------------------------------------------------------------------
// Client state containers.
// ---------------------------------------------------------------------------------------------
builder.Services.AddScoped<FinanceFlow.Web.State.SessionState>();
builder.Services.AddScoped<FinanceFlow.Web.State.ThemeState>();
builder.Services.AddScoped<FinanceFlow.Web.State.ToastService>();
builder.Services.AddScoped<FinanceFlow.Web.State.UserContext>();
builder.Services.AddScoped<FinanceFlow.Web.State.RealtimeService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();
builder.Services.AddSingleton(serviceProvider => new FinanceFlow.Web.LocalPhoneVerification(
    serviceProvider.GetRequiredService<IConfiguration>(),
    serviceProvider.GetRequiredService<IWebHostEnvironment>()));

        WebApplication app = builder.Build();

        Configure(app);

        return app;
    }

    /// <summary>Registers the pipeline and the endpoints of the server.</summary>
    public static void Configure(WebApplication app)
    {

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseStaticFiles();
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// The endpoints of the static assets are only mapped when the manifest of the build
    // exists. The desktop application of BarsiG serves the folder wwwroot of the published
    // web application directly, there the middleware of the static files is enough.
    string staticAssetsManifest = Path.Combine(
        app.Environment.ContentRootPath,
        $"{app.Environment.ApplicationName}.staticwebassets.endpoints.json");

    if (File.Exists(staticAssetsManifest))
    {
        app.MapStaticAssets();
    }

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// The state of the server itself. The endpoint says nothing about the customers and
// nothing about the money, it only tells that the server of the simulator is alive.
// The window of the desktop application uses it to show the real state of the server.
app.MapGet("/health", () => Results.Ok(new { status = "ok", application = "BarsiG" }))
    .AllowAnonymous();

// Real time notifications about changed data.
app.MapHub<NotificationsHub>("/hubs/notifications");

// Export endpoints. They require an authenticated user and only export the data of that user.
app.MapGet("/export/transactions.csv", async (
    HttpContext context,
    FinanceFlow.Application.Services.ExportService exportService,
    CancellationToken cancellationToken) =>
{
    Guid userId = ReadUserId(context.User);
    string csv = await exportService.TransactionsToCsvAsync(userId, cancellationToken);

    return Results.File(
        System.Text.Encoding.UTF8.GetBytes(csv),
        "text/csv; charset=utf-8",
        exportService.BuildFileName("transactions", "csv"));
}).RequireAuthorization();

app.MapGet("/export/transactions.json", async (
    HttpContext context,
    FinanceFlow.Application.Services.ExportService exportService,
    CancellationToken cancellationToken) =>
{
    Guid userId = ReadUserId(context.User);
    string json = await exportService.TransactionsToJsonAsync(userId, cancellationToken);

    return Results.File(
        System.Text.Encoding.UTF8.GetBytes(json),
        "application/json; charset=utf-8",
        exportService.BuildFileName("transactions", "json"));
}).RequireAuthorization();

app.MapPost("/logout", async (
    HttpContext context,
    FinanceFlow.Application.Banking.SecurityService securityService) =>
{
    Guid userId = ReadUserId(context.User);
    string sessionId = context.User.FindFirstValue("sid") ?? string.Empty;

    if (Guid.TryParse(sessionId, out Guid sid))
    {
        await securityService.EndSessionAsync(userId, sid, ReadIpAddress(context), context.RequestAborted);
    }

    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.LocalRedirect("/login");
}).RequireAuthorization();

// Sign in and registration. The pages of the interface call these endpoints with a small
// JSON body, therefore no antiforgery token of a form is needed.
app.MapPost("/account/sign-in", async (
    HttpContext context,
    FinanceFlow.Application.Dtos.LoginRequest request,
    FinanceFlow.Application.Services.AuthService authService,
    FinanceFlow.Application.Banking.SecurityService securityService,
    FinanceFlow.Application.Abstractions.ILoginThrottle throttle,
    IConfiguration configuration,
    IWebHostEnvironment environment,
    ILogger<FinanceFlow.Application.Services.AuthService> logger) =>
{
    string ip = ReadIpAddress(context);
    string key = BuildThrottleKey(request.Email, ip);
    string identifier = request.Email?.Trim() ?? string.Empty;
    string identifierKind = identifier.Length == 0
        ? "empty"
        : identifier.Contains('@') ? "email" : "phone";
    string normalization = identifierKind switch
    {
        "phone" => FinanceFlow.Domain.Validation.Validation.PhoneNumber(identifier, "Identifier").IsSuccess()
            ? "e164_valid"
            : "invalid_phone_format",
        "email" => FinanceFlow.Domain.Validation.Validation.EmailAddress(identifier, "Identifier").IsSuccess()
            ? "email_valid"
            : "email_invalid",
        _ => "missing"
    };
    logger.LogInformation(
        "Sign-in request diagnostics. Method={Method}; Route={Route}; IdentifierKind={IdentifierKind}; IdentifierNormalization={IdentifierNormalization}; PasswordPresent={PasswordPresent}; ModelBinding=completed; AntiforgeryValidation=not-applied-to-anonymous-json-login",
        context.Request.Method,
        context.Request.Path.Value,
        identifierKind,
        normalization,
        !string.IsNullOrWhiteSpace(request.Password));

    if (throttle.IsBlocked(key, out TimeSpan remaining))
    {
        await securityService.RecordFailedSignInAsync(
            request.Email ?? string.Empty,
            ip,
            context.Request.Headers.UserAgent.ToString(),
            $"The account is locked for {Math.Ceiling(remaining.TotalMinutes)} minutes.",
            context.RequestAborted);

        return Results.Json(
            new { error = "Too many failed attempts. Please try again in a few minutes." },
            statusCode: StatusCodes.Status429TooManyRequests);
    }

    bool allowLocalDemoPhone = IsLocalPhoneDemoEnabled(configuration, environment);
    Result<UserSession> result = await authService.AuthenticateAsync(request, allowLocalDemoPhone);

    if (result is not Ok<UserSession> ok)
    {
        logger.LogWarning(
            "Sign-in rejected. IdentifierKind={IdentifierKind}; Reason={Reason}; HttpStatus={HttpStatus}",
            identifierKind,
            ((Fail<UserSession>)result).Code,
            StatusCodes.Status400BadRequest);

        throttle.RegisterFailure(key);

        await securityService.RecordFailedSignInAsync(
            request.Email ?? string.Empty,
            ip,
            context.Request.Headers.UserAgent.ToString(),
            "The e-mail address or the password is wrong.",
            context.RequestAborted);

        return Results.BadRequest(new { error = result.Error });
    }

    throttle.RegisterSuccess(key);

    FinanceFlow.Domain.Banking.CustomerSession session = await securityService.StartSessionAsync(
        ok.Value.UserId,
        ip,
        context.Request.Headers.UserAgent.ToString(),
        context.RequestAborted);

    await SignInAsync(context, ok.Value, session.Id);
    logger.LogInformation(
        "Sign-in accepted. IdentifierKind={IdentifierKind}; AuthenticationHandler=Cookie; HttpStatus={HttpStatus}",
        identifierKind,
        StatusCodes.Status200OK);

    return Results.Ok(new { displayName = ok.Value.DisplayName, role = ok.Value.Role });
}).AllowAnonymous().RequireRateLimiting("sign-in");

app.MapPost("/account/register", async (
    HttpContext context,
    FinanceFlow.Application.Dtos.RegisterRequest request,
    FinanceFlow.Application.Services.AuthService authService,
    IConfiguration configuration,
    IWebHostEnvironment environment,
    FinanceFlow.Web.LocalPhoneVerification phoneVerification) =>
{
    bool hasPhone = !string.IsNullOrWhiteSpace(request.PhoneNumber);
    bool localDemoEnabled = IsLocalPhoneDemoEnabled(configuration, environment);

    if (!hasPhone)
    {
        return Results.BadRequest(new
        {
            error = "A Kazakhstan mobile number is required for registration."
        });
    }

    if (!localDemoEnabled)
    {
        return Results.BadRequest(new
        {
            error = "Phone verification is unavailable because no approved verification provider is enabled."
        });
    }

    Result<UserSession> result = await authService.RegisterAsync(request);

    if (result is not Ok<UserSession> ok)
    {
        return Results.BadRequest(new { error = "The account could not be created with these details." });
    }

    if (ok.Value.UserId == Guid.Empty)
    {
        return Results.BadRequest(new { error = "The account could not be created." });
    }

    string normalizedPhone = (await authService.GetPhoneNumberAsync(ok.Value.UserId, context.RequestAborted)) ?? string.Empty;
    TimeSpan retryAfter = TimeSpan.Zero;

    if (normalizedPhone.Length == 0 ||
        !phoneVerification.TryIssue(
            ok.Value.UserId.ToString("N"),
            normalizedPhone,
            ReadIpAddress(context),
            DateTimeOffset.UtcNow,
            out FinanceFlow.Web.LocalPhoneVerification.Issued? issued,
            out retryAfter) ||
        issued is null)
    {
        return Results.Json(
            new
            {
                error = "A verification challenge could not be issued. Wait before trying again.",
                retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
            },
            statusCode: StatusCodes.Status429TooManyRequests);
    }

    AppendPhoneChallengeCookie(context, issued.Id);

    return Results.Ok(new
    {
        pendingVerification = true,
        challengeId = issued.Id,
        demoCode = issued.DemoCode,
        expiresAtUtc = issued.ExpiresAtUtc,
        message = "Demo verification — no SMS is sent."
    });
}).AllowAnonymous().RequireRateLimiting("sign-in");

app.MapPost("/account/phone/demo-verify", async (
    HttpContext context,
    FinanceFlow.Web.LocalPhoneVerification.VerifyRequest request,
    FinanceFlow.Web.LocalPhoneVerification phoneVerification,
    FinanceFlow.Infrastructure.Persistence.FinanceFlowDbContext dbContext,
    IConfiguration configuration,
    IWebHostEnvironment environment) =>
{
    bool enabled = IsLocalPhoneDemoEnabled(configuration, environment);

    if (!enabled)
    {
        return Results.NotFound(new { error = "Verification is unavailable." });
    }

    if (!Guid.TryParseExact(
            context.Request.Cookies["BarsiG.PhoneChallenge"],
            "N",
            out Guid boundChallenge) ||
        boundChallenge != request.ChallengeId)
    {
        return Results.BadRequest(new { error = "This verification challenge is unavailable or expired. Start again." });
    }

    if (!phoneVerification.TryGetBinding(request.ChallengeId, out string subject, out string phone))
    {
        return Results.BadRequest(new { error = "This verification challenge is unavailable or expired. Start again." });
    }

    FinanceFlow.Web.LocalPhoneVerification.Verification result =
        phoneVerification.Verify(request.ChallengeId, request.Code, DateTimeOffset.UtcNow);

    if (!result.Succeeded)
    {
        return Results.BadRequest(new { error = result.Error });
    }

    string[] binding = subject.Split(':', 2);
    string purpose = binding.Length == 2 ? binding[0] : "register";
    string subjectId = binding.Length == 2 ? binding[1] : subject;

    if (!Guid.TryParseExact(subjectId, "N", out Guid userId))
    {
        return Results.BadRequest(new { error = "This verification challenge is unavailable or expired. Start again." });
    }

    if (purpose == "change" &&
        (context.User.Identity?.IsAuthenticated != true || ReadUserId(context.User) != userId))
    {
        return Results.Unauthorized();
    }

    FinanceFlow.Domain.Entities.User? user = await dbContext.Users.SingleOrDefaultAsync(
        candidate => candidate.Id == userId &&
            (purpose == "change" || candidate.PhoneNumber == phone),
        context.RequestAborted);

    if (user is null)
    {
        return Results.BadRequest(new { error = "This verification challenge is unavailable or expired. Start again." });
    }

    if (await dbContext.Users.AnyAsync(candidate =>
            candidate.Id != userId && candidate.PhoneNumber == phone,
            context.RequestAborted))
    {
        return Results.BadRequest(new { error = "This number cannot be verified for this account." });
    }

    dbContext.Entry(user).CurrentValues.SetValues(user with
    {
        PhoneNumber = phone,
        IsPhoneVerified = true,
        PhoneVerificationMethod = FinanceFlow.Domain.Enums.PhoneVerificationMethod.LocalDemo
    });
    try
    {
        await dbContext.SaveChangesAsync(context.RequestAborted);
    }
    catch (DbUpdateException)
    {
        return Results.BadRequest(new { error = "This number cannot be verified for this account." });
    }

    context.Response.Cookies.Delete("BarsiG.PhoneChallenge", new CookieOptions { Path = "/account/phone" });
    return Results.Ok(new { verified = true, message = "Demo verification complete. No SMS was sent." });
}).AllowAnonymous().RequireRateLimiting("sign-in");

app.MapPost("/account/phone/demo-resume", async (
    HttpContext context,
    FinanceFlow.Web.LocalPhoneVerification phoneVerification,
    FinanceFlow.Infrastructure.Persistence.FinanceFlowDbContext dbContext,
    IConfiguration configuration,
    IWebHostEnvironment environment) =>
{
    if (!IsLocalPhoneDemoEnabled(configuration, environment))
    {
        return Results.NotFound(new { error = "Verification is unavailable." });
    }

    if (!TryReadPhoneChallengeCookie(context, out Guid challengeId) ||
        !phoneVerification.TryGetBinding(challengeId, DateTimeOffset.UtcNow, out FinanceFlow.Web.LocalPhoneVerification.ChallengeBinding? binding) ||
        binding is null)
    {
        return Results.NotFound(new { pending = false, error = "No resumable phone challenge was found." });
    }

    string[] subjectParts = binding.Subject.Split(':', 2);
    string purpose = subjectParts.Length == 2 ? subjectParts[0] : "register";
    string subjectId = subjectParts.Length == 2 ? subjectParts[1] : binding.Subject;

    if (!Guid.TryParseExact(subjectId, "N", out Guid userId) ||
        (purpose == "change" &&
         (context.User.Identity?.IsAuthenticated != true || ReadUserId(context.User) != userId)))
    {
        return Results.NotFound(new { pending = false, error = "No resumable phone challenge was found." });
    }

    FinanceFlow.Domain.Entities.User? user = await dbContext.Users.SingleOrDefaultAsync(
        candidate => candidate.Id == userId,
        context.RequestAborted);

    if (user is null ||
        (purpose == "register" && (user.IsPhoneVerified || user.PhoneNumber != binding.Phone)) ||
        (purpose == "change" && !context.User.Identity!.IsAuthenticated))
    {
        return Results.NotFound(new { pending = false, error = "No resumable phone challenge was found." });
    }

    return Results.Ok(new
    {
        pending = true,
        challengeId = binding.Id,
        expiresAtUtc = binding.ExpiresAtUtc,
        expired = binding.IsExpired,
        exhausted = binding.IsExhausted,
        replaced = binding.IsReplaced,
        purpose,
        phoneNumber = purpose == "change" ? binding.Phone : null
    });
}).AllowAnonymous().RequireRateLimiting("sign-in");

app.MapPost("/account/phone/demo-resend", async (
    HttpContext context,
    FinanceFlow.Web.LocalPhoneVerification phoneVerification,
    FinanceFlow.Infrastructure.Persistence.FinanceFlowDbContext dbContext,
    IConfiguration configuration,
    IWebHostEnvironment environment) =>
{
    if (!IsLocalPhoneDemoEnabled(configuration, environment))
    {
        return Results.NotFound(new { error = "Verification is unavailable." });
    }

    if (!TryReadPhoneChallengeCookie(context, out Guid oldChallengeId) ||
        !phoneVerification.TryGetBinding(oldChallengeId, DateTimeOffset.UtcNow, out FinanceFlow.Web.LocalPhoneVerification.ChallengeBinding? binding) ||
        binding is null)
    {
        return Results.BadRequest(new { error = "The verification flow expired. Resume it with the phone number and password." });
    }

    string[] subjectParts = binding.Subject.Split(':', 2);
    string purpose = subjectParts.Length == 2 ? subjectParts[0] : "register";
    string subjectId = subjectParts.Length == 2 ? subjectParts[1] : binding.Subject;
    if (!Guid.TryParseExact(subjectId, "N", out Guid userId))
    {
        return Results.BadRequest(new { error = "The verification flow expired. Resume it with the phone number and password." });
    }

    if (purpose == "change" &&
        (context.User.Identity?.IsAuthenticated != true || ReadUserId(context.User) != userId))
    {
        return Results.Unauthorized();
    }

    FinanceFlow.Domain.Entities.User? user = await dbContext.Users.SingleOrDefaultAsync(
        candidate => candidate.Id == userId,
        context.RequestAborted);
    if (user is null ||
        (purpose == "register" && (user.IsPhoneVerified || user.PhoneNumber != binding.Phone)) ||
        (purpose == "change" && await dbContext.Users.AnyAsync(
            candidate => candidate.Id != userId && candidate.PhoneNumber == binding.Phone,
            context.RequestAborted)))
    {
        return Results.BadRequest(new { error = "The verification flow is no longer available. Start again securely." });
    }

    if (!phoneVerification.TryIssue(
            binding.Subject,
            binding.Phone,
            ReadIpAddress(context),
            DateTimeOffset.UtcNow,
            out FinanceFlow.Web.LocalPhoneVerification.Issued? issued,
            out TimeSpan retryAfter) ||
        issued is null)
    {
        return Results.Json(new
            {
                error = "Please wait before requesting another demo code.",
                retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
            },
            statusCode: StatusCodes.Status429TooManyRequests);
    }

    AppendPhoneChallengeCookie(context, issued.Id);
    return Results.Ok(new
    {
        pendingVerification = true,
        challengeId = issued.Id,
        demoCode = issued.DemoCode,
        expiresAtUtc = issued.ExpiresAtUtc,
        message = "Demo verification — no SMS is sent."
    });
}).AllowAnonymous().RequireRateLimiting("sign-in");

app.MapPost("/account/phone/recovery/request", async (
    HttpContext context,
    FinanceFlow.Web.LocalPhoneVerification.PhoneRecoveryRequest request,
    FinanceFlow.Application.Services.AuthService authService,
    FinanceFlow.Web.LocalPhoneVerification phoneVerification,
    IConfiguration configuration,
    IWebHostEnvironment environment,
    ILogger<FinanceFlow.Web.LocalPhoneVerification> logger) =>
{
    if (!IsLocalPhoneDemoEnabled(configuration, environment))
    {
        return Results.NotFound(new { error = "Verification is unavailable." });
    }

    Result<string> normalized = FinanceFlow.Domain.Validation.Validation.KazakhstanPhoneNumber(request.PhoneNumber, "PhoneNumber");
    Guid? userId = normalized.IsSuccess()
        ? await authService.TryGetPendingPhoneRegistrationAsync(
            normalized.ValueOr(string.Empty),
            request.Password,
            context.RequestAborted)
        : null;

    if (userId is null)
    {
        // Same response for an unknown phone, a verified account, and a wrong password.
        return Results.BadRequest(new { error = "The verification flow could not be resumed with these details." });
    }

    string phone = normalized.ValueOr(string.Empty);
    if (!phoneVerification.TryIssue(
            userId.Value.ToString("N"),
            phone,
            ReadIpAddress(context),
            DateTimeOffset.UtcNow,
            out FinanceFlow.Web.LocalPhoneVerification.Issued? issued,
            out TimeSpan retryAfter) ||
        issued is null)
    {
        logger.LogInformation(
            "Phone demo recovery issuance was throttled or unavailable. RetryAfterSeconds={RetryAfterSeconds}",
            Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)));
        return Results.Json(new
            {
                error = "Please wait before requesting another demo code.",
                retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
            },
            statusCode: StatusCodes.Status429TooManyRequests);
    }

    AppendPhoneChallengeCookie(context, issued.Id);
    return Results.Ok(new
    {
        pendingVerification = true,
        challengeId = issued.Id,
        demoCode = issued.DemoCode,
        expiresAtUtc = issued.ExpiresAtUtc,
        message = "Demo verification — no SMS is sent."
    });
}).AllowAnonymous().RequireRateLimiting("sign-in");

app.MapPost("/account/phone/change/request", async (
    HttpContext context,
    FinanceFlow.Web.LocalPhoneVerification.PhoneChangeRequest request,
    FinanceFlow.Web.LocalPhoneVerification phoneVerification,
    FinanceFlow.Infrastructure.Persistence.FinanceFlowDbContext dbContext,
    FinanceFlow.Application.Banking.SecurityService securityService,
    IConfiguration configuration,
    IWebHostEnvironment environment) =>
{
    bool enabled = IsLocalPhoneDemoEnabled(configuration, environment);
    if (!enabled)
    {
        return Results.NotFound(new { error = "Phone verification is unavailable." });
    }

    Guid userId = ReadUserId(context.User);
    Result<string> normalized = FinanceFlow.Domain.Validation.Validation.KazakhstanPhoneNumber(request.PhoneNumber, "PhoneNumber");

    if (normalized.IsFailure())
    {
        return Results.BadRequest(new { error = "Enter a valid phone number and confirm your password." });
    }

    Result passwordResult = await securityService.VerifyPasswordAsync(userId, request.Password, context.RequestAborted);
    if (passwordResult.IsFailure())
    {
        return Results.BadRequest(new { error = "Enter a valid phone number and confirm your password." });
    }

    string phone = normalized.ValueOr(string.Empty);
    bool duplicate = await dbContext.Users.AnyAsync(user => user.Id != userId && user.PhoneNumber == phone, context.RequestAborted);
    TimeSpan retryAfter = TimeSpan.Zero;

    // A duplicate and an unavailable number produce the same response.
    if (duplicate ||
        !phoneVerification.TryIssue(
            $"change:{userId:N}",
            phone,
            ReadIpAddress(context),
            DateTimeOffset.UtcNow,
            out FinanceFlow.Web.LocalPhoneVerification.Issued? issued,
            out retryAfter) ||
        issued is null)
    {
        return Results.Json(
            new
            {
                error = "A verification challenge could not be issued for this number. Wait before trying again.",
                retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
            },
            statusCode: duplicate ? StatusCodes.Status400BadRequest : StatusCodes.Status429TooManyRequests);
    }

    AppendPhoneChallengeCookie(context, issued.Id);

    return Results.Ok(new
    {
        challengeId = issued.Id,
        demoCode = issued.DemoCode,
        expiresAtUtc = issued.ExpiresAtUtc,
        message = "Demo verification — no SMS is sent."
    });
}).RequireAuthorization().RequireRateLimiting("sign-in");

// ---------------------------------------------------------------------------------------------
// Banking endpoints. They work on the server, the client never touches the database.
// The endpoints that move money require an antiforgery token, so a page of another site
// can not send a request in the name of the signed in customer.
// ---------------------------------------------------------------------------------------------
app.MapGet("/api/antiforgery/token", (HttpContext context, IAntiforgery antiforgery) =>
{
    AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);

    return Results.Ok(new { token = tokens.RequestToken });
}).AllowAnonymous();

app.MapGet("/banking/recipients", async (
    HttpContext context,
    string? query,
    FinanceFlow.Application.Banking.TransferService transferService) =>
{
    Guid userId = ReadUserId(context.User);

    IReadOnlyList<FinanceFlow.Application.Abstractions.RecipientSuggestion> recipients =
        await transferService.SearchRecipientsAsync(query ?? string.Empty, userId);

    return Results.Ok(recipients);
}).RequireAuthorization();

app.MapPost("/banking/recipients/resolve", async (
    FinanceFlow.Application.Banking.TransferService transferService,
    ResolveRecipientRequest request) =>
{
    Result<FinanceFlow.Application.Abstractions.RecipientSuggestion> result =
        await transferService.ResolveRecipientAsync(request.Email);

    return ToHttp(result);
}).RequireAuthorization();

app.MapPost("/banking/transfers/preview", async (
    HttpContext context,
    FinanceFlow.Application.Banking.TransferService transferService,
    FinanceFlow.Application.Banking.Dtos.TransferRequest request) =>
{
    Guid userId = ReadUserId(context.User);

    Result<FinanceFlow.Application.Banking.Dtos.TransferPreview> result =
        await transferService.PreviewAsync(userId, request);

    return ToHttp(result);
}).RequireAuthorization();

app.MapPost("/banking/transfers", async (
    HttpContext context,
    FinanceFlow.Application.Banking.TransferService transferService,
    FinanceFlow.Application.Banking.SecurityService securityService,
    FinanceFlow.Application.Banking.Dtos.TransferRequest request) =>
{
    Guid userId = ReadUserId(context.User);

    // The confirmation of a sensitive operation is checked on the server.
    FinanceFlow.Application.Banking.Dtos.StepUpRequirement requirement =
        await securityService.GetRequirementAsync(userId, ReadAuthTime(context.User));

    if (requirement.RequiresPin)
    {
        Result pinResult = await securityService.VerifyTransactionPinAsync(userId, request.Pin ?? string.Empty);

        if (pinResult.IsFailure())
        {
            return Results.BadRequest(new { error = pinResult.Error, code = "step_up_required" });
        }
    }
    else if (requirement.RequiresPassword)
    {
        Result passwordResult = await securityService.VerifyPasswordAsync(userId, request.Password ?? string.Empty);

        if (passwordResult.IsFailure())
        {
            return Results.BadRequest(new { error = passwordResult.Error, code = "step_up_required" });
        }
    }

    Result<FinanceFlow.Application.Banking.Dtos.TransferOutcome> result =
        await transferService.ExecuteAsync(userId, request);

    return ToHttp(result);
}).RequireAuthorization().RequireRateLimiting("money")
    .Add(builder => builder.Metadata.Add(new RequireAntiforgeryMetadata()));

app.MapGet("/banking/accounts", async (
    HttpContext context,
    FinanceFlow.Application.Banking.AccountService accountService) =>
{
    Guid userId = ReadUserId(context.User);

    IReadOnlyList<FinanceFlow.Application.Banking.Dtos.AccountSummary> accounts =
        await accountService.GetAccountsAsync(userId);

    return Results.Ok(accounts);
}).RequireAuthorization();

app.MapGet("/banking/history", async (
    HttpContext context,
    FinanceFlow.Application.Banking.AccountService accountService,
    int? take) =>
{
    Guid userId = ReadUserId(context.User);

    IReadOnlyList<FinanceFlow.Application.Banking.Dtos.LedgerMovementItem> history =
        await accountService.GetHistoryAsync(userId, take ?? 50);

    return Results.Ok(history);
}).RequireAuthorization();

// ---------------------------------------------------------------------------------------------
// Deposits. The money of a deposit lives on its own account, every movement is a ledger
// transfer, therefore the conservation of the money is kept.
// ---------------------------------------------------------------------------------------------
app.MapGet("/banking/deposits", async (
    HttpContext context,
    FinanceFlow.Application.Banking.DepositService depositService) =>
{
    Guid userId = ReadUserId(context.User);

    IReadOnlyList<FinanceFlow.Application.Banking.Dtos.DepositItem> deposits =
        await depositService.GetDepositsAsync(userId);

    return Results.Ok(deposits);
}).RequireAuthorization();

app.MapPost("/banking/deposits", async (
    HttpContext context,
    FinanceFlow.Application.Banking.DepositService depositService,
    OpenDepositRequest request) =>
{
    Guid userId = ReadUserId(context.User);

    Result<FinanceFlow.Application.Banking.Dtos.DepositItem> result =
        await depositService.OpenAsync(
            userId,
            request.Name,
            request.AnnualRatePercent,
            request.TermMonths);

    return ToHttp(result);
}).RequireAuthorization().Add(builder => builder.Metadata.Add(new RequireAntiforgeryMetadata()));

app.MapPost("/banking/deposits/{id:guid}/contribute", async (
    HttpContext context,
    Guid id,
    FinanceFlow.Application.Banking.DepositService depositService,
    DepositAmountRequest request) =>
{
    Guid userId = ReadUserId(context.User);

    Result<Money> result = await depositService.ContributeAsync(userId, id, request.Amount);

    return ToHttp(result);
}).RequireAuthorization().Add(builder => builder.Metadata.Add(new RequireAntiforgeryMetadata()));

app.MapPost("/banking/deposits/{id:guid}/withdraw", async (
    HttpContext context,
    Guid id,
    FinanceFlow.Application.Banking.DepositService depositService,
    DepositAmountRequest request) =>
{
    Guid userId = ReadUserId(context.User);

    Result<Money> result = await depositService.WithdrawAsync(userId, id, request.Amount);

    return ToHttp(result);
}).RequireAuthorization().Add(builder => builder.Metadata.Add(new RequireAntiforgeryMetadata()));

app.MapGet("/banking/deposits/{id:guid}/interest", async (
    HttpContext context,
    Guid id,
    FinanceFlow.Application.Banking.DepositService depositService) =>
{
    Guid userId = ReadUserId(context.User);

    IReadOnlyList<FinanceFlow.Application.Banking.Dtos.DepositInterestItem> interest =
        await depositService.GetInterestHistoryAsync(userId, id);

    return Results.Ok(interest);
}).RequireAuthorization();

// ---------------------------------------------------------------------------------------------
// Security of the customer: the history of the sign ins, the sessions and the authenticator.
// ---------------------------------------------------------------------------------------------
app.MapGet("/security/overview", async (
    HttpContext context,
    FinanceFlow.Application.Banking.SecurityService securityService) =>
{
    Guid userId = ReadUserId(context.User);

    FinanceFlow.Application.Banking.Dtos.SecurityOverview overview =
        await securityService.GetOverviewAsync(userId, context.RequestAborted);

    return Results.Ok(overview);
}).RequireAuthorization();

app.MapGet("/security/step-up", async (
    HttpContext context,
    FinanceFlow.Application.Banking.SecurityService securityService) =>
{
    Guid userId = ReadUserId(context.User);

    FinanceFlow.Application.Banking.Dtos.StepUpRequirement requirement =
        await securityService.GetRequirementAsync(userId, ReadAuthTime(context.User), context.RequestAborted);

    return Results.Ok(requirement);
}).RequireAuthorization();

app.MapPost("/security/pin", async (
    HttpContext context,
    FinanceFlow.Application.Banking.SecurityService securityService,
    SetPinRequest request) =>
{
    Guid userId = ReadUserId(context.User);

    Result result = await securityService.SetTransactionPinAsync(
        userId,
        request.Pin,
        request.Password,
        context.RequestAborted);

    return result.IsFailure()
        ? Results.BadRequest(new { error = result.Error, code = "error" })
        : Results.Ok(new { ok = true });
}).RequireAuthorization().Add(builder => builder.Metadata.Add(new RequireAntiforgeryMetadata()));

app.MapPost("/security/totp/enable", async (
    HttpContext context,
    FinanceFlow.Application.Banking.SecurityService securityService,
    SetPasswordRequest request) =>
{
    Guid userId = ReadUserId(context.User);

    Result<string> result = await securityService.EnableTotpAsync(userId, request.Password, context.RequestAborted);

    return ToHttp(result);
}).RequireAuthorization().Add(builder => builder.Metadata.Add(new RequireAntiforgeryMetadata()));

app.MapPost("/security/totp/verify", async (
    HttpContext context,
    FinanceFlow.Application.Banking.SecurityService securityService,
    TotpRequest request) =>
{
    Guid userId = ReadUserId(context.User);

    Result result = await securityService.VerifyTotpAsync(userId, request.Code, context.RequestAborted);

    return result.IsFailure()
        ? Results.BadRequest(new { error = result.Error, code = "error" })
        : Results.Ok(new { ok = true });
}).RequireAuthorization().Add(builder => builder.Metadata.Add(new RequireAntiforgeryMetadata()));

app.MapPost("/security/sessions/{id:guid}/terminate", async (
    HttpContext context,
    Guid id,
    FinanceFlow.Application.Banking.SecurityService securityService) =>
{
    Guid userId = ReadUserId(context.User);

    Result result = await securityService.TerminateSessionAsync(
        userId,
        id,
        ReadIpAddress(context),
        context.RequestAborted);

    return result.IsFailure()
        ? Results.BadRequest(new { error = result.Error, code = "error" })
        : Results.Ok(new { ok = true });
}).RequireAuthorization().Add(builder => builder.Metadata.Add(new RequireAntiforgeryMetadata()));

// ---------------------------------------------------------------------------------------------
// Virtual cards and the receipts of the transfers.
// ---------------------------------------------------------------------------------------------
app.MapGet("/banking/cards", async (
    HttpContext context,
    FinanceFlow.Application.Banking.VirtualCardService cardService) =>
{
    Guid userId = ReadUserId(context.User);

    IReadOnlyList<FinanceFlow.Application.Banking.Dtos.VirtualCardItem> cards =
        await cardService.GetCardsAsync(userId, context.RequestAborted);

    return Results.Ok(cards);
}).RequireAuthorization();

app.MapPost("/banking/cards", async (
    HttpContext context,
    FinanceFlow.Application.Banking.VirtualCardService cardService,
    CreateCardRequest request) =>
{
    Guid userId = ReadUserId(context.User);

    Result<FinanceFlow.Application.Banking.Dtos.VirtualCardItem> result =
        await cardService.CreateAsync(
            userId,
            request.LinkedAccountId,
            request.Nickname,
            request.DailyLimit,
            request.MonthlyLimit,
            context.RequestAborted);

    return ToHttp(result);
}).RequireAuthorization().Add(builder => builder.Metadata.Add(new RequireAntiforgeryMetadata()));

app.MapPost("/banking/cards/{id:guid}/freeze", async (
    HttpContext context,
    Guid id,
    FinanceFlow.Application.Banking.VirtualCardService cardService,
    FreezeCardRequest request) =>
{
    Guid userId = ReadUserId(context.User);

    Result<FinanceFlow.Application.Banking.Dtos.VirtualCardItem> result =
        await cardService.SetFrozenAsync(userId, id, request.Frozen, context.RequestAborted);

    return ToHttp(result);
}).RequireAuthorization().Add(builder => builder.Metadata.Add(new RequireAntiforgeryMetadata()));

app.MapPost("/banking/cards/{id:guid}/limits", async (
    HttpContext context,
    Guid id,
    FinanceFlow.Application.Banking.VirtualCardService cardService,
    CardLimitsRequest request) =>
{
    Guid userId = ReadUserId(context.User);

    Result<FinanceFlow.Application.Banking.Dtos.VirtualCardItem> result =
        await cardService.SetLimitsAsync(
            userId,
            id,
            request.DailyLimit,
            request.MonthlyLimit,
            context.RequestAborted);

    return ToHttp(result);
}).RequireAuthorization().Add(builder => builder.Metadata.Add(new RequireAntiforgeryMetadata()));

app.MapGet("/banking/transfers/{id:guid}/receipt.pdf", async (
    HttpContext context,
    Guid id,
    FinanceFlow.Application.Banking.ReceiptService receiptService) =>
{
    Guid userId = ReadUserId(context.User);

    Result<byte[]> result = await receiptService.RenderAsync(userId, id, context.RequestAborted);

    if (result.IsFailure())
    {
        return Results.NotFound();
    }

    return Results.File(result.ValueOr([]), "application/pdf", $"BarsiG-receipt-{id:N}.pdf");
}).RequireAuthorization();

// ---------------------------------------------------------------------------------------------
// Administration of the Demo Bank. Every endpoint checks the role on the server.
// ---------------------------------------------------------------------------------------------
app.MapGet("/admin/overview", async (
    FinanceFlow.Application.Banking.AdminService adminService) =>
{
    FinanceFlow.Application.Banking.Dtos.AdminOverview overview = await adminService.GetOverviewAsync();

    return Results.Ok(overview);
}).RequireAuthorization("Admin");

app.MapPost("/admin/funding", async (
    HttpContext context,
    FinanceFlow.Application.Banking.AdminService adminService,
    AdminFundingRequest request) =>
{
    Guid adminId = ReadUserId(context.User);

    Result<FinanceFlow.Application.Banking.Dtos.AdminFundingOutcome> result =
        await adminService.FundCustomerAsync(
            adminId,
            request.CustomerUserId,
            request.Amount,
            request.Description,
            request.IdempotencyKey,
            context.RequestAborted);

    return ToHttp(result);
}).RequireAuthorization("Admin").Add(builder => builder.Metadata.Add(new RequireAntiforgeryMetadata()));

// The demonstration of the interest needs a clock that can be moved forward.
// The tool only exists in the development environment, a production server never has it.
app.MapPost("/admin/time/advance", async (
    IHostEnvironment environment,
    FinanceFlow.Infrastructure.Time.AdjustableClock clock,
    FinanceFlow.Application.Banking.DepositService depositService,
    AdvanceTimeRequest request) =>
{
    if (!environment.IsDevelopment())
    {
        return Results.NotFound();
    }

    clock.Advance(TimeSpan.FromDays(request.Days));

    Result<int> accrued = await depositService.AccrueInterestAsync();

    return Results.Ok(new
    {
        now = clock.UtcNow,
        offsetDays = clock.Offset.TotalDays,
        accruedMonths = accrued.ValueOr(0)
    });
}).RequireAuthorization("Admin");

    }

    /// <summary>Applies the migrations and fills the database with the demonstration data.</summary>
    public static async Task PrepareAsync(WebApplication app)
    {
        await ApplyMigrationsAsync(app);
        if (FinanceFlow.Web.LocalPhoneVerification.IsDemoModeEnabled(
                app.Environment.EnvironmentName,
                app.Configuration.GetValue("PhoneVerification:EnableLocalDemo", false)) &&
            app.Configuration.GetValue("DemoData:EnableSeedAccount", false))
        {
            await DemoDataSeeder.SeedAsync(
                app.Services,
                Environment.GetEnvironmentVariable("BARSIG_DEMO_PASSWORD"));
        }

        await BankingSeeder.SeedAsync(app.Services, app.Configuration);

        AdminSeedOutcome outcome = await AdminSeeder.SeedAsync(app.Services, app.Configuration);

        if (outcome is AdminSeedOutcome.RefusedExistingAccount or AdminSeedOutcome.RejectedWeakPassword)
        {
            app.Logger.LogWarning(
                "The administrator of the Demo Bank is not available ({Outcome}). " +
                "The application works, but the administrative pages stay closed.",
                outcome);
        }
    }


private static async Task SignInAsync(HttpContext context, UserSession session, Guid? sessionId = null)
{
    List<Claim> claims =
    [
        new(ClaimTypes.NameIdentifier, session.UserId.ToString()),
        new(ClaimTypes.Name, session.DisplayName),
        new(ClaimTypes.Email, session.Email),
        new(ClaimTypes.Role, session.Role),
        new("auth_time", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString())
    ];

    if (sessionId is not null)
    {
        claims.Add(new Claim("sid", sessionId.Value.ToString("N")));
    }

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    var principal = new ClaimsPrincipal(identity);

    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
}

private static async Task ApplyMigrationsAsync(WebApplication app)
{
    await using AsyncServiceScope scope = app.Services.CreateAsyncScope();

    FinanceFlowDbContext dbContext = scope.ServiceProvider.GetRequiredService<FinanceFlowDbContext>();
    await dbContext.Database.MigrateAsync();
}

private static Guid ReadUserId(ClaimsPrincipal principal)
{
    string? value = principal.FindFirstValue(ClaimTypes.NameIdentifier);
    return Guid.TryParse(value, out Guid userId) ? userId : Guid.Empty;
}

/// <summary>Reads the address of the client.</summary>
private static string ReadIpAddress(HttpContext context) =>
    context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

private static bool IsLocalPhoneDemoEnabled(IConfiguration configuration, IWebHostEnvironment environment) =>
    FinanceFlow.Web.LocalPhoneVerification.IsDemoModeEnabled(
        environment.EnvironmentName,
        configuration.GetValue("PhoneVerification:EnableLocalDemo", false));

private static void AppendPhoneChallengeCookie(HttpContext context, Guid challengeId) =>
    context.Response.Cookies.Append(
        "BarsiG.PhoneChallenge",
        challengeId.ToString("N"),
        new CookieOptions
        {
            HttpOnly = true,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            IsEssential = true,
            // Keep only the opaque flow reference long enough for a user to recover an
            // expired in-memory challenge. The OTP itself still expires after five minutes.
            MaxAge = TimeSpan.FromHours(24),
            Path = "/account/phone"
        });

private static bool TryReadPhoneChallengeCookie(HttpContext context, out Guid challengeId) =>
    Guid.TryParseExact(context.Request.Cookies["BarsiG.PhoneChallenge"], "N", out challengeId);

/// <summary>Reads the moment of the last successful sign in from the cookie.</summary>
private static DateTimeOffset? ReadAuthTime(ClaimsPrincipal principal)
{
    string? value = principal.FindFirstValue("auth_time");

    return long.TryParse(value, out long seconds)
        ? DateTimeOffset.FromUnixTimeSeconds(seconds)
        : null;
}

/// <summary>Builds the key of the throttle of a sign in attempt.</summary>
private static string BuildThrottleKey(string? identifier, string ipAddress)
{
    string normalized = (identifier ?? string.Empty).Trim().ToLowerInvariant();
    if (!normalized.Contains('@') &&
        FinanceFlow.Domain.Validation.Validation.PhoneNumber(normalized, "Identifier") is Ok<string> phone)
    {
        normalized = phone.Value;
    }

    return $"{normalized}|{ipAddress}";
}

/// <summary>Turns a result of the application layer into the answer of the server.</summary>
private static IResult ToHttp<T>(Result<T> result) => result switch
{
    Ok<T> ok => Results.Ok(ok.Value),
    Fail<T> fail => Results.BadRequest(new { error = fail.Error, code = fail.Code }),
    _ => Results.BadRequest(new { error = "The request could not be processed.", code = "error" })
};

}

/// <summary>The body of the request that finds a recipient by the e-mail address.</summary>
public sealed record ResolveRecipientRequest(string Email);

/// <summary>The body of the request that funds a customer out of the reserve of the Demo Bank.</summary>
public sealed record AdminFundingRequest(
    Guid CustomerUserId,
    decimal Amount,
    string? Description,
    string IdempotencyKey);

/// <summary>The body of the request that opens a savings deposit.</summary>
public sealed record OpenDepositRequest(
    string Name,
    decimal AnnualRatePercent,
    int TermMonths);

/// <summary>The body of the request that moves money into or out of a deposit.</summary>
public sealed record DepositAmountRequest(decimal Amount);

/// <summary>The body of the request that moves the simulated clock forward.</summary>
public sealed record AdvanceTimeRequest(int Days);

/// <summary>The body of the request that sets the transaction PIN.</summary>
public sealed record SetPinRequest(string Pin, string Password);

/// <summary>The body of the request that confirms the password of the customer.</summary>
public sealed record SetPasswordRequest(string Password);

/// <summary>The body of the request that checks the code of the authenticator.</summary>
public sealed record TotpRequest(string Code);

/// <summary>The body of the request that creates a virtual card.</summary>
public sealed record CreateCardRequest(
    Guid LinkedAccountId,
    string Nickname,
    decimal DailyLimit,
    decimal MonthlyLimit);

/// <summary>The body of the request that freezes a card or lets it be used again.</summary>
public sealed record FreezeCardRequest(bool Frozen);

/// <summary>The body of the request that changes the limits of a card.</summary>
public sealed record CardLimitsRequest(decimal DailyLimit, decimal MonthlyLimit);
