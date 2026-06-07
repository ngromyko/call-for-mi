using System.Net.WebSockets;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using CallForMe.Api.Contracts;
using CallForMe.Api.Hubs;
using CallForMe.Api.Models;
using CallForMe.Api.Options;
using CallForMe.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using QRCoder;

const string UserCookieScheme = "callforme_user";
const string AdminUsername = "admin";
const decimal CallPrice = CallPricing.CreditsPerMinuteUsd;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.Services.Configure<TwilioOptions>(builder.Configuration.GetSection(TwilioOptions.SectionName));
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection(AiOptions.SectionName));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection(AdminOptions.SectionName));
builder.Services.Configure<TonPaymentsOptions>(builder.Configuration.GetSection(TonPaymentsOptions.SectionName));
builder.Services.Configure<UsdtPaymentsOptions>(builder.Configuration.GetSection(UsdtPaymentsOptions.SectionName));
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
builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
        options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddHttpClient<TwilioCallService>();
builder.Services.AddHttpClient<AiConversationService>();
builder.Services.AddHttpClient<TonApiClient>();
builder.Services.AddSingleton<TwilioRequestValidator>();
builder.Services.AddSingleton<SqliteDatabase>();
builder.Services.AddSingleton<ICallRepository, SqliteCallRepository>();
builder.Services.AddSingleton<IBillingRepository, SqliteBillingRepository>();
builder.Services.AddSingleton<IUserRepository, SqliteUserRepository>();
builder.Services.AddSingleton<ActiveRelayRegistry>();
builder.Services.AddSingleton<ConversationOrchestrator>();
builder.Services.AddSingleton<CallBillingService>();
builder.Services.AddSingleton<TwilioCostRefreshService>();
builder.Services.AddSingleton<TonDepositMonitor>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<TonDepositMonitor>());
builder.Services.AddHostedService<CallMinuteBillingService>();
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
        "GET /api/ton/deposit-info",
        "GET /api/ton/deposits",
        "POST /api/ton/refresh",
        "GET /api/usdt/deposit-info",
        "POST /api/promocodes/redeem",
        "GET /api/admin/users",
        "GET /api/admin/ton-payments",
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

app.MapGet("/api/admin/users", async (
    HttpRequest request,
    IOptionsMonitor<AdminOptions> admin,
    IUserRepository users,
    CancellationToken cancellationToken) =>
{
    if (!IsAdmin(request, admin.CurrentValue))
    {
        return AdminRequired();
    }

    return Results.Ok(await users.ListWithStatsAsync(cancellationToken));
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

app.MapGet("/api/ton/deposits", async (
    HttpContext context,
    IBillingRepository billing,
    CancellationToken cancellationToken) =>
{
    var userId = CurrentUserId(context);
    if (userId is null)
    {
        return UserRequired();
    }

    return Results.Ok(await billing.ListTonPaymentsAsync(BalanceClientId(userId.Value), cancellationToken));
});

app.MapGet("/api/ton/deposit-info", (
    HttpContext context,
    IOptionsMonitor<TonPaymentsOptions> ton) =>
{
    var userId = CurrentUserId(context);
    if (userId is null)
    {
        return UserRequired();
    }

    var options = ton.CurrentValue;
    var clientId = BalanceClientId(userId.Value);
    var comment = TonDepositComment.FromClientId(clientId);
    if (!options.IsConfigured)
    {
        return Results.Ok(new
        {
            enabled = false,
            walletAddress = "",
            comment,
            creditsPerTon = options.CreditsPerTon,
            minTonAmount = options.MinTonAmount,
            paymentLink = ""
        });
    }

    var wallet = options.WalletAddress.Trim();
    return Results.Ok(new
    {
        enabled = true,
        walletAddress = wallet,
        comment,
        creditsPerTon = options.CreditsPerTon,
        minTonAmount = options.MinTonAmount,
        paymentLink = CreateTonTransferLink(wallet, options.MinTonAmount, comment)
    });
});

app.MapGet("/api/usdt/deposit-info", (
    HttpContext context,
    IOptionsMonitor<UsdtPaymentsOptions> usdt) =>
{
    var userId = CurrentUserId(context);
    if (userId is null)
    {
        return UserRequired();
    }

    var options = usdt.CurrentValue;
    var clientId = BalanceClientId(userId.Value);
    var comment = TonDepositComment.FromClientId(clientId);
    if (!options.IsConfigured)
    {
        return Results.Ok(new
        {
            enabled = false,
            walletAddress = "",
            network = string.IsNullOrWhiteSpace(options.Network) ? "TRC20" : options.Network.Trim(),
            comment,
            creditsPerUsdt = options.CreditsPerUsdt,
            minUsdtAmount = options.MinUsdtAmount
        });
    }

    return Results.Ok(new
    {
        enabled = true,
        walletAddress = options.WalletAddress.Trim(),
        network = string.IsNullOrWhiteSpace(options.Network) ? "TRC20" : options.Network.Trim(),
        comment,
        creditsPerUsdt = options.CreditsPerUsdt,
        minUsdtAmount = options.MinUsdtAmount
    });
});

app.MapPost("/api/ton/refresh", async (
    HttpContext context,
    TonDepositMonitor monitor,
    IBillingRepository billing,
    CancellationToken cancellationToken) =>
{
    var userId = CurrentUserId(context);
    if (userId is null)
    {
        return UserRequired();
    }

    await monitor.CheckOnceAsync(cancellationToken);
    var clientId = BalanceClientId(userId.Value);
    return Results.Ok(new
    {
        balance = await billing.GetBalanceAsync(clientId, cancellationToken),
        deposits = await billing.ListTonPaymentsAsync(clientId, cancellationToken)
    });
});

app.MapGet("/api/ton/qr", (
    HttpContext context,
    string? amount,
    IOptionsMonitor<TonPaymentsOptions> ton) =>
{
    var userId = CurrentUserId(context);
    if (userId is null)
    {
        return UserRequired();
    }

    var options = ton.CurrentValue;
    if (!options.IsConfigured)
    {
        return Results.Problem(
            title: "TON payments are not configured",
            detail: "TON-пополнение пока не настроено.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var tonAmount = options.MinTonAmount;
    if (!string.IsNullOrWhiteSpace(amount) &&
        decimal.TryParse(amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
    {
        tonAmount = Math.Max(parsed, options.MinTonAmount);
    }

    var clientId = BalanceClientId(userId.Value);
    var comment = TonDepositComment.FromClientId(clientId);
    var transferLink = CreateTonTransferLink(options.WalletAddress.Trim(), tonAmount, comment);
    using var generator = new QRCodeGenerator();
    using var data = generator.CreateQrCode(transferLink, QRCodeGenerator.ECCLevel.Q);
    var qrCode = new PngByteQRCode(data);
    var bytes = qrCode.GetGraphic(8);
    return Results.File(bytes, "image/png");
});

app.MapGet("/api/usdt/qr", (
    HttpContext context,
    string? amount,
    IOptionsMonitor<UsdtPaymentsOptions> usdt) =>
{
    var userId = CurrentUserId(context);
    if (userId is null)
    {
        return UserRequired();
    }

    var options = usdt.CurrentValue;
    if (!options.IsConfigured)
    {
        return Results.Problem(
            title: "USDT payments are not configured",
            detail: "USDT-пополнение пока не настроено.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var usdtAmount = options.MinUsdtAmount;
    if (!string.IsNullOrWhiteSpace(amount) &&
        decimal.TryParse(amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
    {
        usdtAmount = Math.Max(parsed, options.MinUsdtAmount);
    }

    var clientId = BalanceClientId(userId.Value);
    var comment = TonDepositComment.FromClientId(clientId);
    var paymentText = CreateUsdtPaymentText(options.WalletAddress.Trim(), options.Network, usdtAmount, comment);
    using var generator = new QRCodeGenerator();
    using var data = generator.CreateQrCode(paymentText, QRCodeGenerator.ECCLevel.Q);
    var qrCode = new PngByteQRCode(data);
    var bytes = qrCode.GetGraphic(8);
    return Results.File(bytes, "image/png");
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

app.MapGet("/api/admin/ton-payments", async (
    HttpRequest request,
    IOptionsMonitor<AdminOptions> admin,
    IBillingRepository billing,
    CancellationToken cancellationToken) =>
{
    if (!IsAdmin(request, admin.CurrentValue))
    {
        return AdminRequired();
    }

    return Results.Ok(await billing.ListTonPaymentsAsync(null, cancellationToken));
});

app.MapGet("/api/config", (HttpRequest request, IOptionsMonitor<TwilioOptions> twilio, IOptionsMonitor<AiOptions> ai, IOptionsMonitor<AdminOptions> admin, IOptionsMonitor<TonPaymentsOptions> ton, IOptionsMonitor<UsdtPaymentsOptions> usdt) =>
{
    var twilioOptions = twilio.CurrentValue;
    var aiOptions = ai.CurrentValue;
    var twilioCredentialsOk = twilioOptions.CredentialsValid is not false;
    var readyForRealCalls = twilioOptions.IsConfigured && aiOptions.IsConfigured && twilioCredentialsOk;
    var setupReason = SetupReason(twilioOptions, aiOptions);
    var tonOptions = ton.CurrentValue;
    var usdtOptions = usdt.CurrentValue;
    return Results.Ok(new
    {
        twilioEnabled = twilioOptions.IsConfigured,
        aiEnabled = aiOptions.IsConfigured,
        readyForRealCalls,
        callPricePerMinute = CallPrice,
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
        adminAuthenticated = IsAdmin(request, admin.CurrentValue),
        tonPayments = new
        {
            enabled = tonOptions.IsConfigured,
            walletAddress = tonOptions.WalletAddress,
            creditsPerTon = tonOptions.CreditsPerTon,
            minTonAmount = tonOptions.MinTonAmount
        },
        usdtPayments = new
        {
            enabled = usdtOptions.IsConfigured,
            walletAddress = usdtOptions.WalletAddress,
            network = usdtOptions.Network,
            creditsPerUsdt = usdtOptions.CreditsPerUsdt,
            minUsdtAmount = usdtOptions.MinUsdtAmount
        }
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

app.MapPost("/api/config/ton", async (
    HttpRequest httpRequest,
    SaveTonPaymentsSettingsRequest request,
    IOptionsMonitor<AdminOptions> admin,
    LocalSettingsWriter writer,
    CancellationToken cancellationToken) =>
{
    if (!IsAdmin(httpRequest, admin.CurrentValue))
    {
        return AdminRequired();
    }

    var wallet = request.WalletAddress?.Trim() ?? "";
    var errors = new Dictionary<string, string[]>();
    if (!TonPaymentsOptions.IsLikelyTonAddress(wallet))
    {
        errors["walletAddress"] = ["Введите TON wallet address."];
    }

    if (request.CreditsPerTon <= 0)
    {
        errors["creditsPerTon"] = ["Курс должен быть больше нуля."];
    }

    if (request.MinTonAmount <= 0)
    {
        errors["minTonAmount"] = ["Минимальная сумма должна быть больше нуля."];
    }

    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    await writer.SaveTonPaymentsAsync(wallet, request.CreditsPerTon, request.MinTonAmount, cancellationToken);
    return Results.Ok(new { tonPaymentsEnabled = true });
});

app.MapPost("/api/config/usdt", async (
    HttpRequest httpRequest,
    SaveUsdtPaymentsSettingsRequest request,
    IOptionsMonitor<AdminOptions> admin,
    LocalSettingsWriter writer,
    CancellationToken cancellationToken) =>
{
    if (!IsAdmin(httpRequest, admin.CurrentValue))
    {
        return AdminRequired();
    }

    var wallet = request.WalletAddress?.Trim() ?? "";
    var network = request.Network?.Trim() ?? "";
    var errors = new Dictionary<string, string[]>();
    if (!UsdtPaymentsOptions.IsLikelyWalletAddress(wallet))
    {
        errors["walletAddress"] = ["Введите USDT wallet address."];
    }

    if (string.IsNullOrWhiteSpace(network) || network.Length > 32)
    {
        errors["network"] = ["Введите сеть USDT, например TRC20 или ERC20."];
    }

    if (request.CreditsPerUsdt <= 0)
    {
        errors["creditsPerUsdt"] = ["Курс должен быть больше нуля."];
    }

    if (request.MinUsdtAmount <= 0)
    {
        errors["minUsdtAmount"] = ["Минимальная сумма должна быть больше нуля."];
    }

    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    await writer.SaveUsdtPaymentsAsync(wallet, network, request.CreditsPerUsdt, request.MinUsdtAmount, cancellationToken);
    return Results.Ok(new { usdtPaymentsEnabled = true });
});

app.MapGet("/api/calls", async (HttpContext context, ICallRepository repository, CancellationToken cancellationToken) =>
{
    var userId = CurrentUserId(context);
    if (userId is null)
    {
        return UserRequired();
    }

    var calls = await repository.ListAsync(cancellationToken);
    return Results.Ok(calls.Where(call => CanAccessCall(call, userId)));
});

app.MapGet("/api/calls/{id:guid}", async (Guid id, HttpContext context, ICallRepository repository, CancellationToken cancellationToken) =>
{
    var userId = CurrentUserId(context);
    if (userId is null)
    {
        return UserRequired();
    }

    var call = await repository.GetAsync(id, cancellationToken);
    return call is null || !CanAccessCall(call, userId) ? Results.NotFound() : Results.Ok(call);
});

app.MapPost("/api/calls", async (
    CreateCallRequest request,
    HttpContext context,
    ICallRepository repository,
    IBillingRepository billing,
    TwilioCallService twilio,
    IOptionsMonitor<AiOptions> ai,
    CancellationToken cancellationToken) =>
{
    var userId = CurrentUserId(context);
    if (userId is null)
    {
        return UserRequired();
    }

    var errors = RequestValidation.Validate(request);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var balanceClientId = BalanceClientId(userId.Value);
    var balance = await billing.GetBalanceAsync(balanceClientId, cancellationToken);
    if (balance.Balance < CallPrice)
    {
        return Results.Problem(
            title: "Balance required",
            detail: $"Одна минута стоит {CallPrice:0.00} USD (кредиты). Пополните баланс, чтобы начать звонок.",
            statusCode: StatusCodes.Status402PaymentRequired);
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
        UserId = userId,
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

    var debit = await billing.DebitBalanceAsync(balanceClientId, CallPrice, cancellationToken);
    if (debit.Balance is null)
    {
        return Results.Problem(
            title: "Balance required",
            detail: debit.Error ?? $"Одна минута стоит {CallPrice:0.00} USD (кредиты).",
            statusCode: StatusCodes.Status402PaymentRequired);
    }

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
        await billing.CreditBalanceAsync(balanceClientId, CallPrice, CancellationToken.None);
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
        await billing.CreditBalanceAsync(balanceClientId, CallPrice, CancellationToken.None);
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

        AddTerminalStatusMessage(stored, now);

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

    if (!IsLiveStatus(call.Status))
    {
        costRefresh.Queue(call.Id);
    }

    if (call.Status == CallStatus.Completed)
    {
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
var adminHtmlPath = Path.Combine(app.Environment.WebRootPath, "index.html");
app.MapGet("/admin", () => File.Exists(adminHtmlPath)
    ? Results.File(adminHtmlPath, "text/html")
    : Results.Problem(
        title: "Admin page is unavailable",
        detail: "Не найден файл wwwroot/index.html.",
        statusCode: StatusCodes.Status500InternalServerError));
app.MapGet("/admin/{*path}", () => File.Exists(adminHtmlPath)
    ? Results.File(adminHtmlPath, "text/html")
    : Results.Problem(
        title: "Admin page is unavailable",
        detail: "Не найден файл wwwroot/index.html.",
        statusCode: StatusCodes.Status500InternalServerError));

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
    detail: "Войдите под пользователем admin.",
    statusCode: StatusCodes.Status401Unauthorized);

static IResult UserRequired() => Results.Problem(
    title: "Login required",
    detail: "Войдите в аккаунт, чтобы видеть и создавать свои звонки.",
    statusCode: StatusCodes.Status401Unauthorized);

static bool IsAdmin(HttpRequest request, AdminOptions admin)
{
    if (!admin.IsConfigured)
    {
        return false;
    }

    return string.Equals(
        request.HttpContext.User.FindFirstValue(ClaimTypes.Name),
        AdminUsername,
        StringComparison.Ordinal);
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
    userId is not null && call.UserId == userId;

static async Task<bool> CanAccessCallAsync(
    Guid callId,
    HttpContext context,
    ICallRepository repository,
    CancellationToken cancellationToken)
{
    var call = await repository.GetAsync(callId, cancellationToken);
    return call is not null && CanAccessCall(call, CurrentUserId(context));
}

static bool IsValidClientId(string? clientId) =>
    !string.IsNullOrWhiteSpace(clientId) &&
    clientId.Length <= 96 &&
    clientId.All(character => char.IsLetterOrDigit(character) || character is '-' or '_');

static string CreateTonTransferLink(string walletAddress, decimal tonAmount, string comment)
{
    var nanotons = decimal.ToInt64(decimal.Round(tonAmount * 1_000_000_000m, 0));
    return $"ton://transfer/{Uri.EscapeDataString(walletAddress)}?amount={nanotons}&text={Uri.EscapeDataString(comment)}";
}

static string CreateUsdtPaymentText(string walletAddress, string network, decimal usdtAmount, string comment)
{
    return string.Join('\n', new[]
    {
        "USDT payment",
        $"Network: {(string.IsNullOrWhiteSpace(network) ? "TRC20" : network.Trim())}",
        $"Address: {walletAddress}",
        $"Amount: {usdtAmount.ToString(CultureInfo.InvariantCulture)} USDT",
        $"Comment: {comment}"
    });
}

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

static void AddTerminalStatusMessage(CallSession call, DateTimeOffset timestamp)
{
    if (IsLiveStatus(call.Status) || call.Status == CallStatus.Completed)
    {
        return;
    }

    var text = call.Status switch
    {
        CallStatus.NoAnswer => "Собеседник не поднял трубку.",
        CallStatus.Busy => "Линия была занята.",
        CallStatus.Canceled => "Звонок отменён до разговора.",
        CallStatus.Failed => string.IsNullOrWhiteSpace(call.Error)
            ? "Звонок не удалось выполнить."
            : $"Звонок не удалось выполнить: {call.Error}",
        _ => ""
    };
    if (string.IsNullOrWhiteSpace(text) ||
        call.Transcript.Any(entry => entry.Speaker == TranscriptSpeaker.System && entry.Text == text))
    {
        return;
    }

    call.Transcript.Add(new TranscriptEntry
    {
        Speaker = TranscriptSpeaker.System,
        Text = text,
        Source = "twilio-status",
        Timestamp = timestamp
    });
}

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

public sealed record SaveTonPaymentsSettingsRequest(
    string WalletAddress,
    decimal CreditsPerTon,
    decimal MinTonAmount);

public sealed record SaveUsdtPaymentsSettingsRequest(
    string WalletAddress,
    string Network,
    decimal CreditsPerUsdt,
    decimal MinUsdtAmount);
