using System.Net.WebSockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CallForMe.Api.Contracts;
using CallForMe.Api.Hubs;
using CallForMe.Api.Models;
using CallForMe.Api.Options;
using CallForMe.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

const string AdminCookieName = "callforme_admin";
const string UserCookieScheme = "callforme_user";

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.Services.Configure<TwilioOptions>(builder.Configuration.GetSection(TwilioOptions.SectionName));
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection(AiOptions.SectionName));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection(AdminOptions.SectionName));
builder.Services.PostConfigure<TwilioOptions>(options =>
{
    options.AccountSid = FirstPresent(options.AccountSid, "TWILIO_ACCOUNT_SID", "TWILIO__ACCOUNT_SID");
    options.AuthToken = FirstPresent(options.AuthToken, "TWILIO_AUTH_TOKEN", "TWILIO__AUTH_TOKEN");
    options.FromNumber = FirstPresent(options.FromNumber, "TWILIO_FROM_NUMBER", "TWILIO_PHONE_NUMBER", "TWILIO__FROM_NUMBER");
    options.PublicBaseUrl = FirstPresent(options.PublicBaseUrl, "TWILIO_PUBLIC_BASE_URL", "TWILIO__PUBLIC_BASE_URL");
    if (!options.Enabled &&
        !string.IsNullOrWhiteSpace(options.AccountSid) &&
        !string.IsNullOrWhiteSpace(options.AuthToken))
    {
        options.Enabled = true;
    }
});
builder.Services.PostConfigure<AiOptions>(options =>
{
    options.ApiKey = FirstPresent(options.ApiKey, "OPENAI_API_KEY", "AI__APIKEY", "AI__API_KEY");
    if (!options.Enabled && !string.IsNullOrWhiteSpace(options.ApiKey))
    {
        options.Enabled = true;
    }
});
builder.Services.PostConfigure<AdminOptions>(options =>
{
    options.Password = FirstPresent(options.Password, "CALLFORME_ADMIN_PASSWORD", "ADMIN_PASSWORD", "ADMIN__PASSWORD");
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyHeader().AllowAnyMethod().AllowCredentials()
            .SetIsOriginAllowed(_ => true));
});
builder.Services.AddAuthentication(UserCookieScheme)
    .AddCookie(UserCookieScheme, options =>
    {
        options.Cookie.Name = "callforme_user";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
    });
builder.Services.AddSignalR();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddHttpClient<TwilioCallService>();
builder.Services.AddHttpClient<AiConversationService>();
builder.Services.AddSingleton<TwilioRequestValidator>();
builder.Services.AddSingleton<SqliteDatabase>();
builder.Services.AddSingleton<ICallRepository, SqliteCallRepository>();
builder.Services.AddSingleton<IBillingRepository, SqliteBillingRepository>();
builder.Services.AddSingleton<IUserRepository, SqliteUserRepository>();
builder.Services.AddSingleton<ActiveRelayRegistry>();
builder.Services.AddSingleton<ConversationOrchestrator>();
builder.Services.AddSingleton<CallBillingService>();
builder.Services.AddSingleton<TwilioCostRefreshService>();
builder.Services.AddSingleton<LocalSettingsWriter>();

var app = builder.Build();

app.UseCors();
app.UseAuthentication();
app.UseWebSockets();
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        context.Context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        context.Context.Response.Headers.Pragma = "no-cache";
        context.Context.Response.Headers.Expires = "0";
    }
});

app.MapGet("/health", () => Results.Ok(new
{
    service = "CallForMe.Api",
    status = "ok",
    docs = "/api"
}));

app.MapGet("/api", (IOptionsMonitor<TwilioOptions> twilio, IOptionsMonitor<AiOptions> ai) => Results.Ok(new
{
    endpoints = new[]
    {
        "POST /api/calls",
        "GET /api/calls",
        "GET /api/calls/{id}",
        "POST /api/calls/{id}/messages",
        "POST /api/calls/{id}/autopilot",
        "POST /api/calls/{id}/end",
        "GET /api/balance/{clientId}",
        "POST /api/promocodes/redeem",
        "GET /api/admin/promocodes",
        "POST /api/admin/promocodes",
        "GET /api/auth/me",
        "POST /api/auth/register",
        "POST /api/auth/login",
        "POST /api/auth/logout",
        "WS /twilio/conversation-relay",
        "SignalR /hubs/calls"
    },
    twilioEnabled = twilio.CurrentValue.IsConfigured,
    aiEnabled = ai.CurrentValue.IsConfigured
}));

app.MapGet("/api/auth/me", async (
    HttpContext context,
    IUserRepository users,
    CancellationToken cancellationToken) =>
{
    var userId = CurrentUserId(context);
    if (userId is null)
    {
        return Results.Ok(new { authenticated = false });
    }

    var user = await users.GetAsync(userId.Value, cancellationToken);
    return user is null
        ? Results.Ok(new { authenticated = false })
        : Results.Ok(new
        {
            authenticated = true,
            user,
            balanceClientId = BalanceClientId(user.Id)
        });
});

app.MapPost("/api/auth/register", async (
    AuthRequest request,
    HttpContext context,
    IUserRepository users,
    CancellationToken cancellationToken) =>
{
    var (user, error) = await users.RegisterAsync(request.Username, request.Password, cancellationToken);
    if (user is null)
    {
        return Results.Problem(title: "Регистрация не удалась", detail: error, statusCode: StatusCodes.Status400BadRequest);
    }

    await SignInUserAsync(context, user);
    return Results.Ok(new { authenticated = true, user, balanceClientId = BalanceClientId(user.Id) });
});

app.MapPost("/api/auth/login", async (
    AuthRequest request,
    HttpContext context,
    IUserRepository users,
    CancellationToken cancellationToken) =>
{
    var user = await users.ValidateLoginAsync(request.Username, request.Password, cancellationToken);
    if (user is null)
    {
        return Results.Problem(title: "Вход не удался", detail: "Неверный username или пароль.", statusCode: StatusCodes.Status401Unauthorized);
    }

    await SignInUserAsync(context, user);
    return Results.Ok(new { authenticated = true, user, balanceClientId = BalanceClientId(user.Id) });
});

app.MapPost("/api/auth/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(UserCookieScheme);
    return Results.NoContent();
});

app.MapGet("/api/admin/status", (HttpRequest request, IOptionsMonitor<AdminOptions> admin) => Results.Ok(new
{
    configured = admin.CurrentValue.IsConfigured,
    authenticated = IsAdmin(request, admin.CurrentValue)
}));

app.MapPost("/api/admin/login", (
    AdminLoginRequest request,
    HttpContext context,
    IOptionsMonitor<AdminOptions> admin) =>
{
    var options = admin.CurrentValue;
    if (!options.IsConfigured)
    {
        return Results.Problem(
            title: "Admin password is not configured",
            detail: "Set Admin:Password in appsettings.Local.json or CALLFORME_ADMIN_PASSWORD.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    if (!SecretEquals(request.Password, options.Password))
    {
        return Results.Problem(
            title: "Admin login failed",
            detail: "Неверный admin пароль.",
            statusCode: StatusCodes.Status401Unauthorized);
    }

    context.Response.Cookies.Append(AdminCookieName, AdminToken(options), new CookieOptions
    {
        HttpOnly = true,
        Secure = context.Request.IsHttps,
        SameSite = SameSiteMode.Lax,
        Expires = DateTimeOffset.UtcNow.AddHours(12)
    });
    return Results.Ok(new { authenticated = true });
});

app.MapPost("/api/admin/logout", (HttpContext context) =>
{
    context.Response.Cookies.Delete(AdminCookieName);
    return Results.NoContent();
});

app.MapGet("/api/balance/{clientId}", async (
    string clientId,
    IBillingRepository billing,
    CancellationToken cancellationToken) =>
{
    if (!IsValidClientId(clientId))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["clientId"] = ["Некорректный идентификатор клиента."]
        });
    }

    return Results.Ok(await billing.GetBalanceAsync(clientId, cancellationToken));
});

app.MapPost("/api/promocodes/redeem", async (
    RedeemPromoCodeRequest request,
    IBillingRepository billing,
    CancellationToken cancellationToken) =>
{
    var errors = ValidateRedeemPromoCode(request);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var (balance, error) = await billing.RedeemPromoCodeAsync(
        request.ClientId.Trim(),
        request.Code.Trim(),
        cancellationToken);
    return balance is null
        ? Results.Problem(title: "Промокод не применён", detail: error, statusCode: StatusCodes.Status400BadRequest)
        : Results.Ok(balance);
});

app.MapGet("/api/admin/promocodes", async (
    HttpRequest request,
    IOptionsMonitor<AdminOptions> admin,
    IBillingRepository billing,
    CancellationToken cancellationToken) =>
{
    if (!IsAdmin(request, admin.CurrentValue))
    {
        return AdminRequired();
    }

    return Results.Ok(await billing.ListPromoCodesAsync(cancellationToken));
});

app.MapPost("/api/admin/promocodes", async (
    CreatePromoCodeRequest request,
    HttpRequest httpRequest,
    IOptionsMonitor<AdminOptions> admin,
    IBillingRepository billing,
    CancellationToken cancellationToken) =>
{
    if (!IsAdmin(httpRequest, admin.CurrentValue))
    {
        return AdminRequired();
    }

    var errors = ValidateCreatePromoCode(request);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var promo = await billing.CreatePromoCodeAsync(
        request.Code.Trim(),
        request.Amount,
        request.MaxRedemptions,
        request.ExpiresAt,
        cancellationToken);
    return Results.Created($"/api/admin/promocodes/{promo.Id}", promo);
});

app.MapPatch("/api/admin/promocodes/{id:guid}", async (
    Guid id,
    SetPromoCodeActiveRequest request,
    HttpRequest httpRequest,
    IOptionsMonitor<AdminOptions> admin,
    IBillingRepository billing,
    CancellationToken cancellationToken) =>
{
    if (!IsAdmin(httpRequest, admin.CurrentValue))
    {
        return AdminRequired();
    }

    var promo = await billing.SetPromoCodeActiveAsync(id, request.Active, cancellationToken);
    return promo is null ? Results.NotFound() : Results.Ok(promo);
});

app.MapGet("/api/config", (HttpRequest request, IOptionsMonitor<TwilioOptions> twilio, IOptionsMonitor<AiOptions> ai, IOptionsMonitor<AdminOptions> admin) =>
{
    var twilioOptions = twilio.CurrentValue;
    var aiOptions = ai.CurrentValue;
    var twilioCredentialsOk = twilioOptions.CredentialsValid is not false;
    var readyForRealCalls = twilioOptions.IsConfigured && aiOptions.IsConfigured && twilioCredentialsOk;
    var setupReason = SetupReason(twilioOptions, aiOptions);
    return Results.Ok(new
    {
        twilioEnabled = twilioOptions.IsConfigured,
        aiEnabled = aiOptions.IsConfigured,
        readyForRealCalls,
        setupReason,
        twilioCredentialsValid = twilioOptions.CredentialsValid,
        twilioMissing = twilioOptions.Missing(),
        aiMissing = aiOptions.Missing(),
        publicBaseUrl = twilioOptions.PublicBaseUrl,
        fromNumber = string.IsNullOrWhiteSpace(twilioOptions.FromNumber) ? "" : twilioOptions.FromNumber,
        accountSid = MaskSecret(twilioOptions.AccountSid),
        hasAuthToken = !string.IsNullOrWhiteSpace(twilioOptions.AuthToken),
        aiModel = aiOptions.Model,
        hasAiKey = !string.IsNullOrWhiteSpace(aiOptions.ApiKey),
        adminConfigured = admin.CurrentValue.IsConfigured,
        adminAuthenticated = IsAdmin(request, admin.CurrentValue)
    });
});

app.MapPost("/api/config/openai", async (
    HttpRequest httpRequest,
    SaveOpenAiSettingsRequest request,
    IOptionsMonitor<AdminOptions> admin,
    LocalSettingsWriter writer,
    CancellationToken cancellationToken) =>
{
    if (!IsAdmin(httpRequest, admin.CurrentValue))
    {
        return AdminRequired();
    }

    if (string.IsNullOrWhiteSpace(request.ApiKey) ||
        !request.ApiKey.Trim().StartsWith("sk-", StringComparison.Ordinal))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["apiKey"] = ["OpenAI key должен начинаться с sk-."]
        });
    }

    await writer.SaveOpenAiAsync(request.ApiKey.Trim(), request.Model?.Trim(), cancellationToken);
    return Results.Ok(new { aiEnabled = true, message = "OpenAI key saved locally." });
});

app.MapPost("/api/config/twilio", async (
    HttpRequest httpRequest,
    SaveTwilioSettingsRequest request,
    IOptionsMonitor<TwilioOptions> twilio,
    IOptionsMonitor<AdminOptions> admin,
    LocalSettingsWriter writer,
    CancellationToken cancellationToken) =>
{
    if (!IsAdmin(httpRequest, admin.CurrentValue))
    {
        return AdminRequired();
    }

    var current = twilio.CurrentValue;
    var accountSid = KeepCurrentIfBlankOrMasked(request.AccountSid, current.AccountSid);
    var authToken = KeepCurrentIfBlankOrMasked(request.AuthToken, current.AuthToken);
    var fromNumber = KeepCurrentIfBlankOrMasked(request.FromNumber, current.FromNumber);
    var publicBaseUrl = KeepCurrentIfBlankOrMasked(request.PublicBaseUrl, current.PublicBaseUrl);
    var errors = new Dictionary<string, string[]>();
    if (string.IsNullOrWhiteSpace(accountSid) || !accountSid.StartsWith("AC", StringComparison.Ordinal))
    {
        errors["accountSid"] = ["Вставьте полный Twilio Account SID."];
    }

    if (string.IsNullOrWhiteSpace(authToken) || authToken.Length < 16)
    {
        errors["authToken"] = ["Вставьте Twilio Auth Token."];
    }

    if (string.IsNullOrWhiteSpace(fromNumber) || !fromNumber.Trim().StartsWith('+'))
    {
        errors["fromNumber"] = ["Twilio номер должен быть в формате +1234567890."];
    }

    if (!TwilioOptions.IsUsablePublicBaseUrl(publicBaseUrl))
    {
        errors["publicBaseUrl"] = ["Нужен публичный HTTPS URL, не localhost."];
    }

    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    await writer.SaveTwilioAsync(
        accountSid,
        authToken,
        fromNumber.Trim(),
        publicBaseUrl.Trim(),
        cancellationToken);
    return Results.Ok(new { twilioEnabled = true, accountSid = MaskSecret(accountSid) });
});

app.MapPost("/api/config/twilio/check", async (
    HttpRequest httpRequest,
    TwilioCallService twilio,
    IOptionsMonitor<AdminOptions> admin,
    LocalSettingsWriter writer,
    CancellationToken cancellationToken) =>
{
    if (!IsAdmin(httpRequest, admin.CurrentValue))
    {
        return AdminRequired();
    }

    try
    {
        await twilio.CheckCredentialsAsync(cancellationToken);
        await writer.SaveTwilioCredentialCheckAsync(true, null, cancellationToken);
        return Results.Ok(new { ok = true, message = "Twilio credentials are valid." });
    }
    catch (TwilioApiException exception)
    {
        await writer.SaveTwilioCredentialCheckAsync(false, exception.Message, cancellationToken);
        return Results.Problem(
            title: "Twilio credentials failed",
            detail: exception.Message,
            statusCode: StatusCodes.Status400BadRequest);
    }
});

app.MapGet("/api/calls", async (HttpContext context, ICallRepository repository, CancellationToken cancellationToken) =>
{
    var userId = CurrentUserId(context);
    var calls = await repository.ListAsync(cancellationToken);
    return Results.Ok(calls.Where(call => CanAccessCall(call, userId)));
});

app.MapGet("/api/calls/{id:guid}", async (Guid id, HttpContext context, ICallRepository repository, CancellationToken cancellationToken) =>
{
    var call = await repository.GetAsync(id, cancellationToken);
    return call is null || !CanAccessCall(call, CurrentUserId(context)) ? Results.NotFound() : Results.Ok(call);
});

app.MapPost("/api/calls", async (
    CreateCallRequest request,
    HttpContext context,
    ICallRepository repository,
    TwilioCallService twilio,
    IOptionsMonitor<AiOptions> ai,
    CancellationToken cancellationToken) =>
{
    var errors = RequestValidation.Validate(request);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    if (!twilio.IsConfigured)
    {
        return Results.Problem(
            title: "Twilio is not configured",
            detail: "Set Twilio:Enabled=true, AccountSid, AuthToken, FromNumber, and PublicBaseUrl to start real calls.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    if (!ai.CurrentValue.IsConfigured)
    {
        return Results.Problem(
            title: "AI is not configured",
            detail: "Set AI:Enabled=true and AI:ApiKey to translate and answer real calls.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var call = new CallSession
    {
        Id = Guid.NewGuid(),
        UserId = CurrentUserId(context),
        DisplayName = CreateDisplayName(request.DisplayName, request.Prompt),
        PhoneNumber = request.PhoneNumber.Trim(),
        Prompt = request.Prompt.Trim(),
        Language = NormalizeCallLanguage(request.Language),
        UserLanguage = string.IsNullOrWhiteSpace(request.UserLanguage) ? "ru-RU" : request.UserLanguage.Trim(),
        AutoPilot = request.AutoPilot,
        RecordingConsentConfirmed = request.RecordingConsentConfirmed,
        Status = CallStatus.Queued,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    await repository.CreateAsync(call, cancellationToken);

    try
    {
        var sid = await twilio.StartCallAsync(call, cancellationToken);
        call = (await repository.MutateAsync(call.Id, stored =>
        {
            stored.TwilioCallSid = sid;
            stored.Status = CallStatus.Calling;
            stored.Error = null;
            stored.UpdatedAt = DateTimeOffset.UtcNow;
        }, cancellationToken))!;
    }
    catch (TwilioApiException exception)
    {
        await repository.MutateAsync(call.Id, stored =>
        {
            stored.Status = CallStatus.Failed;
            stored.Error = exception.Message;
            stored.UpdatedAt = DateTimeOffset.UtcNow;
        }, cancellationToken);

        return Results.Problem(
            title: "Twilio call failed",
            detail: exception.Message,
            statusCode: StatusCodes.Status400BadRequest);
    }
    catch (Exception exception)
    {
        await repository.MutateAsync(call.Id, stored =>
        {
            stored.Status = CallStatus.Failed;
            stored.Error = exception.Message;
            stored.UpdatedAt = DateTimeOffset.UtcNow;
        }, cancellationToken);
        throw;
    }

    return Results.Created($"/api/calls/{call.Id}", call);
});

app.MapPost("/api/calls/{id:guid}/messages", async (
    Guid id,
    HttpContext context,
    SendMessageRequest request,
    ICallRepository repository,
    ConversationOrchestrator orchestrator,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Text))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["text"] = ["Message text is required."]
        });
    }

    if (!await CanAccessCallAsync(id, context, repository, cancellationToken))
    {
        return Results.NotFound();
    }

    var call = await orchestrator.SendOperatorMessageAsync(
        id,
        request.Text.Trim(),
        request.SpokenText?.Trim(),
        cancellationToken);
    return call is null ? Results.NotFound() : Results.Ok(call);
});

app.MapPost("/api/calls/{id:guid}/autopilot", async (
    Guid id,
    HttpContext context,
    SetAutoPilotRequest request,
    ICallRepository repository,
    ConversationOrchestrator orchestrator,
    CancellationToken cancellationToken) =>
{
    if (!await CanAccessCallAsync(id, context, repository, cancellationToken))
    {
        return Results.NotFound();
    }

    var call = await orchestrator.SetAutoPilotAsync(id, request.Enabled, cancellationToken);
    return call is null ? Results.NotFound() : Results.Ok(call);
});

app.MapPost("/api/calls/{id:guid}/end", async (
    Guid id,
    HttpContext context,
    ICallRepository repository,
    ConversationOrchestrator orchestrator,
    TwilioCallService twilio,
    TwilioCostRefreshService costRefresh,
    CancellationToken cancellationToken) =>
{
    if (!await CanAccessCallAsync(id, context, repository, cancellationToken))
    {
        return Results.NotFound();
    }

    var call = await orchestrator.EndAsync(id, cancellationToken);
    if (call is null)
    {
        return Results.NotFound();
    }

    try
    {
        await twilio.EndCallAsync(call, cancellationToken);
    }
    catch (TwilioApiException exception) when (IsAlreadyEndedTwilioError(exception))
    {
    }
    costRefresh.Queue(call.Id);
    return Results.Ok(call);
});

app.MapPost("/api/calls/{id:guid}/summary", async (
    Guid id,
    HttpContext context,
    ICallRepository repository,
    ConversationOrchestrator orchestrator,
    CancellationToken cancellationToken) =>
{
    if (!await CanAccessCallAsync(id, context, repository, cancellationToken))
    {
        return Results.NotFound();
    }

    var call = await orchestrator.EnsureSummaryAsync(id, cancellationToken);
    return call is null ? Results.NotFound() : Results.Ok(call);
});

app.MapPost("/api/calls/{id:guid}/hide", async (
    Guid id,
    HttpContext context,
    ICallRepository repository,
    CancellationToken cancellationToken) =>
{
    if (!await CanAccessCallAsync(id, context, repository, cancellationToken))
    {
        return Results.NotFound();
    }

    var call = await repository.MutateAsync(id, stored =>
    {
        stored.Hidden = true;
        stored.UpdatedAt = DateTimeOffset.UtcNow;
    }, cancellationToken);
    return call is null ? Results.NotFound() : Results.NoContent();
});

app.MapPost("/twilio/voice", async (
    HttpRequest request,
    ICallRepository repository,
    TwilioRequestValidator validator,
    IOptionsMonitor<TwilioOptions> options,
    CancellationToken cancellationToken) =>
{
    var form = await request.ReadFormAsync(cancellationToken);
    if (!validator.IsValid(request, form))
    {
        return Results.Unauthorized();
    }

    if (!Guid.TryParse(request.Query["callId"], out var callId))
    {
        return Results.BadRequest("Missing callId.");
    }

    var call = await repository.GetAsync(callId, cancellationToken);
    if (call is null)
    {
        return Results.NotFound();
    }

    var xml = TwimlFactory.CreateConversationRelay(call, options.CurrentValue);
    return Results.Text(xml, "application/xml", Encoding.UTF8);
});

app.MapPost("/twilio/status", async (
    HttpRequest request,
    ICallRepository repository,
    TwilioRequestValidator validator,
    ConversationOrchestrator orchestrator,
    CallBillingService billing,
    TwilioCostRefreshService costRefresh,
    Microsoft.AspNetCore.SignalR.IHubContext<CallHub> hub,
    CancellationToken cancellationToken) =>
{
    var form = await request.ReadFormAsync(cancellationToken);
    if (!validator.IsValid(request, form))
    {
        return Results.Unauthorized();
    }

    if (!Guid.TryParse(request.Query["callId"], out var callId))
    {
        return Results.BadRequest("Missing callId.");
    }

    var status = form["CallStatus"].ToString();
    var sid = form["CallSid"].ToString();
    long? duration = long.TryParse(form["CallDuration"], out var parsedDuration) ? parsedDuration : null;
    var call = await repository.MutateAsync(callId, stored =>
    {
        var now = DateTimeOffset.UtcNow;
        stored.Status = TwilioStatusMapper.Map(status);
        if (!string.IsNullOrWhiteSpace(sid))
        {
            stored.TwilioCallSid = sid;
        }

        if (stored.Status == CallStatus.Ringing)
        {
            stored.RingingAt ??= now;
        }
        else if (stored.Status == CallStatus.InProgress)
        {
            stored.AnsweredAt ??= now;
        }
        else if (!IsLiveStatus(stored.Status))
        {
            stored.CompletedAt ??= now;
        }

        if (ShouldClearCallError(stored.Status))
        {
            stored.Error = null;
        }

        stored.DurationSeconds = duration ?? stored.DurationSeconds;
        stored.Usage = CallUsageMetrics.From(stored);
        if (!IsLiveStatus(stored.Status))
        {
            stored.Billing = billing.Calculate(stored);
        }

        stored.UpdatedAt = now;
    }, cancellationToken);

    if (call is null)
    {
        return Results.NotFound();
    }

    await hub.Clients.Group(CallHub.GroupName(call.Id)).SendAsync("CallUpdated", call, cancellationToken);

    if (call.Status == CallStatus.Completed)
    {
        costRefresh.Queue(call.Id);
        await orchestrator.EnsureSummaryAsync(call.Id, cancellationToken);
    }

    return Results.NoContent();
});

app.Map("/twilio/conversation-relay", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    var validator = context.RequestServices.GetRequiredService<TwilioRequestValidator>();
    if (!validator.IsValidWebSocket(context.Request))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    var socket = await context.WebSockets.AcceptWebSocketAsync();
    var registry = context.RequestServices.GetRequiredService<ActiveRelayRegistry>();
    var orchestrator = context.RequestServices.GetRequiredService<ConversationOrchestrator>();
    Guid? callId = null;
    var buffer = new byte[32 * 1024];

    try
    {
        while (socket.State == WebSocketState.Open)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, context.RequestAborted);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed", context.RequestAborted);
                    return;
                }

                message.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            using var document = JsonDocument.Parse(message.ToArray());
            var root = document.RootElement;
            var type = root.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;

            if (type == "setup")
            {
                callId = ConversationRelayMessageParser.GetCallId(root);
                if (callId is not null)
                {
                    registry.Register(callId.Value, socket);
                    await orchestrator.MarkConnectedAsync(callId.Value, context.RequestAborted);
                }
            }
            else if (type == "prompt" && callId is not null && ConversationRelayMessageParser.IsFinal(root))
            {
                var text = ConversationRelayMessageParser.GetVoicePrompt(root);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    await orchestrator.HandleRemotePromptAsync(callId.Value, text, context.RequestAborted);
                }
            }
            else if (type == "interrupt" && callId is not null)
            {
                await orchestrator.MarkInterruptedAsync(callId.Value, context.RequestAborted);
            }
        }
    }
    finally
    {
        if (callId is not null)
        {
            registry.Unregister(callId.Value, socket);
        }
    }
});

app.MapHub<CallHub>("/hubs/calls");

app.Run();

static string MaskSecret(string value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return "";
    }

    return value.Length <= 8 ? "••••" : $"{value[..4]}…{value[^4..]}";
}

static IResult AdminRequired() => Results.Problem(
    title: "Admin required",
    detail: "Откройте настройки как админ.",
    statusCode: StatusCodes.Status401Unauthorized);

static bool IsAdmin(HttpRequest request, AdminOptions admin)
{
    if (!admin.IsConfigured)
    {
        return false;
    }

    return request.Cookies.TryGetValue(AdminCookieName, out var cookie) &&
        SecretEquals(cookie, AdminToken(admin));
}

static string AdminToken(AdminOptions admin)
{
    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(admin.Password));
    return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes("call-for-me-admin-session-v1")));
}

static async Task SignInUserAsync(HttpContext context, UserAccountView user)
{
    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new(ClaimTypes.Name, user.Username)
    };
    var identity = new ClaimsIdentity(claims, UserCookieScheme);
    await context.SignInAsync(
        UserCookieScheme,
        new ClaimsPrincipal(identity),
        new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30)
        });
}

static Guid? CurrentUserId(HttpContext context)
{
    var value = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
    return Guid.TryParse(value, out var id) ? id : null;
}

static string BalanceClientId(Guid userId) => $"user-{userId:N}";

static bool CanAccessCall(CallSession call, Guid? userId) =>
    call.UserId is null || (userId is not null && call.UserId == userId);

static async Task<bool> CanAccessCallAsync(
    Guid callId,
    HttpContext context,
    ICallRepository repository,
    CancellationToken cancellationToken)
{
    var call = await repository.GetAsync(callId, cancellationToken);
    return call is not null && CanAccessCall(call, CurrentUserId(context));
}

static bool SecretEquals(string? left, string? right)
{
    if (left is null || right is null)
    {
        return false;
    }

    var leftBytes = Encoding.UTF8.GetBytes(left);
    var rightBytes = Encoding.UTF8.GetBytes(right);
    return leftBytes.Length == rightBytes.Length &&
        CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
}

static bool IsValidClientId(string? clientId) =>
    !string.IsNullOrWhiteSpace(clientId) &&
    clientId.Length <= 96 &&
    clientId.All(character => char.IsLetterOrDigit(character) || character is '-' or '_');

static Dictionary<string, string[]> ValidateRedeemPromoCode(RedeemPromoCodeRequest request)
{
    var errors = new Dictionary<string, string[]>();
    if (!IsValidClientId(request.ClientId))
    {
        errors["clientId"] = ["Некорректный идентификатор клиента."];
    }

    if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Trim().Length < 3)
    {
        errors["code"] = ["Введите промокод."];
    }

    return errors;
}

static Dictionary<string, string[]> ValidateCreatePromoCode(CreatePromoCodeRequest request)
{
    var errors = new Dictionary<string, string[]>();
    var code = request.Code?.Trim();
    if (string.IsNullOrWhiteSpace(code) || code.Length < 3 || code.Length > 32 ||
        !code.All(character => char.IsLetterOrDigit(character) || character is '-' or '_'))
    {
        errors["code"] = ["Код должен быть 3-32 символа: буквы, цифры, дефис или подчёркивание."];
    }

    if (request.Amount <= 0)
    {
        errors["amount"] = ["Сумма должна быть больше нуля."];
    }

    if (request.MaxRedemptions is <= 0)
    {
        errors["maxRedemptions"] = ["Лимит активаций должен быть больше нуля."];
    }

    return errors;
}

static string FirstPresent(string configured, params string[] envNames)
{
    if (!string.IsNullOrWhiteSpace(configured))
    {
        return configured;
    }

    foreach (var name in envNames)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }
    }

    return configured;
}

static string KeepCurrentIfBlankOrMasked(string? incoming, string current)
{
    if (string.IsNullOrWhiteSpace(incoming) ||
        incoming.Contains('…') ||
        incoming.Contains("...", StringComparison.Ordinal))
    {
        return current;
    }

    return incoming;
}

static string NormalizeCallLanguage(string? language)
{
    if (string.IsNullOrWhiteSpace(language) ||
        language.Equals("auto", StringComparison.OrdinalIgnoreCase))
    {
        return "auto";
    }

    return language.Trim();
}

static bool ShouldClearCallError(CallStatus status) => status is
    CallStatus.Queued or
    CallStatus.Calling or
    CallStatus.Ringing or
    CallStatus.InProgress or
    CallStatus.Completed;

static bool IsAlreadyEndedTwilioError(TwilioApiException exception) =>
    (exception.StatusCode == StatusCodes.Status400BadRequest ||
     exception.StatusCode == StatusCodes.Status404NotFound) &&
    (exception.Message.Contains("completed", StringComparison.OrdinalIgnoreCase) ||
     exception.Message.Contains("not in-progress", StringComparison.OrdinalIgnoreCase) ||
     exception.Message.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
     exception.TwilioCode is 20001 or 21220);

static bool IsLiveStatus(CallStatus status) => status is
    CallStatus.Created or
    CallStatus.Queued or
    CallStatus.Calling or
    CallStatus.Ringing or
    CallStatus.InProgress;

static string CreateDisplayName(string? displayName, string prompt)
{
    if (!string.IsNullOrWhiteSpace(displayName))
    {
        return displayName.Trim();
    }

    var lower = prompt.ToLowerInvariant();
    if (lower.Contains("врач") || lower.Contains("клиник") || lower.Contains("doctor") || lower.Contains("clinic"))
    {
        return "Клиника";
    }

    if (lower.Contains("банк") || lower.Contains("bank"))
    {
        return "Банк";
    }

    if (lower.Contains("достав") || lower.Contains("delivery") || lower.Contains("courier"))
    {
        return "Доставка";
    }

    if (lower.Contains("документ") || lower.Contains("карта") || lower.Contains("urząd") || lower.Contains("office"))
    {
        return "Документы";
    }

    if (lower.Contains("поддерж") || lower.Contains("support") || lower.Contains("сервис"))
    {
        return "Сервис";
    }

    var words = prompt.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(word => word.Length > 1)
        .Take(4)
        .ToArray();
    return words.Length == 0 ? "Звонок" : string.Join(' ', words);
}

static string SetupReason(TwilioOptions twilio, AiOptions ai)
{
    if (!ai.IsConfigured && !twilio.IsConfigured)
    {
        return "Нужно настроить OpenAI и Twilio.";
    }

    if (!ai.IsConfigured)
    {
        return "Нужно настроить OpenAI key.";
    }

    if (!twilio.IsConfigured)
    {
        return "Нужно заполнить Twilio параметры.";
    }

    if (twilio.CredentialsValid is false)
    {
        return "Twilio Account SID/Auth Token не прошли проверку.";
    }

    if (twilio.CredentialsValid is null)
    {
        return "Twilio поля заполнены. Нажмите «Проверить Twilio».";
    }

    return "Готово к реальным звонкам.";
}

public partial class Program;

public sealed record SaveOpenAiSettingsRequest(string ApiKey, string? Model);

public sealed record AdminLoginRequest(string Password);

public sealed record AuthRequest(string Username, string Password);

public sealed record SaveTwilioSettingsRequest(
    string AccountSid,
    string AuthToken,
    string FromNumber,
    string PublicBaseUrl);
