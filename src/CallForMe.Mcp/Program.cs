using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

var baseUrl = Environment.GetEnvironmentVariable("CALLFORME_API_URL")?.TrimEnd('/') ?? "http://localhost:5226";
var adminPassword = Environment.GetEnvironmentVariable("CALLFORME_ADMIN_PASSWORD") ??
    Environment.GetEnvironmentVariable("ADMIN_PASSWORD");
var cookies = new CookieContainer();
using var http = new HttpClient(new HttpClientHandler { CookieContainer = cookies })
{
    BaseAddress = new Uri(baseUrl)
};

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    WriteIndented = false
};

while (await Console.In.ReadLineAsync() is { } line)
{
    if (string.IsNullOrWhiteSpace(line))
    {
        continue;
    }

    JsonNode? request;
    try
    {
        request = JsonNode.Parse(line);
    }
    catch
    {
        continue;
    }

    var id = request?["id"]?.DeepClone();
    var method = request?["method"]?.GetValue<string>();
    if (id is null)
    {
        continue;
    }

    try
    {
        var result = method switch
        {
            "initialize" => InitializeResult(),
            "tools/list" => ToolsListResult(),
            "tools/call" => await CallToolAsync(request?["params"] as JsonObject),
            _ => throw new InvalidOperationException($"Unknown method: {method}")
        };
        await WriteResponseAsync(id, result);
    }
    catch (Exception exception)
    {
        await WriteErrorAsync(id, exception);
    }
}

JsonObject InitializeResult() => new()
{
    ["protocolVersion"] = "2024-11-05",
    ["serverInfo"] = new JsonObject
    {
        ["name"] = "call-for-me",
        ["version"] = "0.1.0"
    },
    ["capabilities"] = new JsonObject
    {
        ["tools"] = new JsonObject()
    }
};

JsonObject ToolsListResult() => new()
{
    ["tools"] = new JsonArray
    {
        Tool("list_calls", "List recent calls.", new JsonObject()),
        Tool("get_call", "Get one call by id.", Schema(("id", "string", "Call id"))),
        Tool("create_call", "Start a new outbound call.", Schema(
            ("phoneNumber", "string", "E.164 phone number"),
            ("prompt", "string", "Call goal"),
            ("displayName", "string", "Optional display name"),
            ("userLanguage", "string", "User language, default ru-RU"),
            ("language", "string", "Remote party language"),
            ("autoPilot", "boolean", "Whether AI may answer automatically"))),
        Tool("send_call_message", "Send an operator message into a live call.", Schema(
            ("id", "string", "Call id"),
            ("text", "string", "Message in user's language"),
            ("spokenText", "string", "Optional message already translated to call language"))),
        Tool("end_call", "End a live call.", Schema(("id", "string", "Call id"))),
        Tool("get_balance", "Get balance for a client id.", Schema(("clientId", "string", "Client id"))),
        Tool("redeem_promo_code", "Redeem a promo code for a client id.", Schema(
            ("clientId", "string", "Client id"),
            ("code", "string", "Promo code"))),
        Tool("list_promo_codes", "Admin: list promo codes.", new JsonObject()),
        Tool("create_promo_code", "Admin: create or update a promo code.", Schema(
            ("code", "string", "Promo code"),
            ("amount", "number", "Balance amount to add"),
            ("maxRedemptions", "number", "Optional activation limit")))
    }
};

async Task<JsonObject> CallToolAsync(JsonObject? parameters)
{
    var name = parameters?["name"]?.GetValue<string>() ?? throw new InvalidOperationException("Tool name is required.");
    var arguments = parameters?["arguments"] as JsonObject ?? new JsonObject();
    var result = name switch
    {
        "list_calls" => await GetJsonAsync("/api/calls"),
        "get_call" => await GetJsonAsync($"/api/calls/{Required(arguments, "id")}"),
        "create_call" => await PostJsonAsync("/api/calls", new
        {
            phoneNumber = Required(arguments, "phoneNumber"),
            prompt = Required(arguments, "prompt"),
            displayName = Optional(arguments, "displayName"),
            userLanguage = Optional(arguments, "userLanguage") ?? "ru-RU",
            language = Optional(arguments, "language") ?? "pl-PL",
            autoPilot = OptionalBool(arguments, "autoPilot") ?? true
        }),
        "send_call_message" => await PostJsonAsync($"/api/calls/{Required(arguments, "id")}/messages", new
        {
            text = Required(arguments, "text"),
            spokenText = Optional(arguments, "spokenText")
        }),
        "end_call" => await PostJsonAsync($"/api/calls/{Required(arguments, "id")}/end", new { }),
        "get_balance" => await GetJsonAsync($"/api/balance/{Required(arguments, "clientId")}"),
        "redeem_promo_code" => await PostJsonAsync("/api/promocodes/redeem", new
        {
            clientId = Required(arguments, "clientId"),
            code = Required(arguments, "code")
        }),
        "list_promo_codes" => await AdminGetJsonAsync("/api/admin/promocodes"),
        "create_promo_code" => await AdminPostJsonAsync("/api/admin/promocodes", new
        {
            code = Required(arguments, "code"),
            amount = RequiredDecimal(arguments, "amount"),
            maxRedemptions = OptionalInt(arguments, "maxRedemptions")
        }),
        _ => throw new InvalidOperationException($"Unknown tool: {name}")
    };

    return ToolTextResult(result.ToJsonString(jsonOptions));
}

async Task<JsonNode> GetJsonAsync(string path)
{
    using var response = await http.GetAsync(path);
    return await ReadJsonResponseAsync(response);
}

async Task<JsonNode> PostJsonAsync(string path, object body)
{
    using var response = await http.PostAsJsonAsync(path, body, jsonOptions);
    return await ReadJsonResponseAsync(response);
}

async Task<JsonNode> AdminGetJsonAsync(string path)
{
    await EnsureAdminAsync();
    return await GetJsonAsync(path);
}

async Task<JsonNode> AdminPostJsonAsync(string path, object body)
{
    await EnsureAdminAsync();
    return await PostJsonAsync(path, body);
}

async Task EnsureAdminAsync()
{
    if (string.IsNullOrWhiteSpace(adminPassword))
    {
        throw new InvalidOperationException("Set CALLFORME_ADMIN_PASSWORD for admin MCP tools.");
    }

    using var response = await http.PostAsJsonAsync("/api/admin/login", new { password = adminPassword }, jsonOptions);
    if (!response.IsSuccessStatusCode)
    {
        var detail = await response.Content.ReadAsStringAsync();
        throw new InvalidOperationException($"Admin login failed: {detail}");
    }
}

async Task<JsonNode> ReadJsonResponseAsync(HttpResponseMessage response)
{
    var text = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode)
    {
        throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {text}");
    }

    if (string.IsNullOrWhiteSpace(text))
    {
        return new JsonObject();
    }

    return JsonNode.Parse(text) ?? new JsonObject();
}

JsonObject Tool(string name, string description, JsonObject schema) => new()
{
    ["name"] = name,
    ["description"] = description,
    ["inputSchema"] = schema
};

JsonObject Schema(params (string Name, string Type, string Description)[] properties)
{
    var required = new JsonArray();
    var props = new JsonObject();
    foreach (var property in properties)
    {
        props[property.Name] = new JsonObject
        {
            ["type"] = property.Type,
            ["description"] = property.Description
        };
        if (!property.Description.StartsWith("Optional", StringComparison.OrdinalIgnoreCase) &&
            !property.Name.StartsWith("display", StringComparison.OrdinalIgnoreCase) &&
            property.Name is not "spokenText" and not "maxRedemptions" and not "language" and not "userLanguage" and not "autoPilot")
        {
            required.Add(property.Name);
        }
    }

    return new JsonObject
    {
        ["type"] = "object",
        ["properties"] = props,
        ["required"] = required
    };
}

JsonObject ToolTextResult(string text) => new()
{
    ["content"] = new JsonArray
    {
        new JsonObject
        {
            ["type"] = "text",
            ["text"] = text
        }
    }
};

string Required(JsonObject arguments, string name) =>
    Optional(arguments, name) ?? throw new InvalidOperationException($"Argument '{name}' is required.");

string? Optional(JsonObject arguments, string name) =>
    arguments.TryGetPropertyValue(name, out var value) ? value?.GetValue<string>() : null;

bool? OptionalBool(JsonObject arguments, string name) =>
    arguments.TryGetPropertyValue(name, out var value) && value is not null ? value.GetValue<bool>() : null;

int? OptionalInt(JsonObject arguments, string name) =>
    arguments.TryGetPropertyValue(name, out var value) && value is not null ? value.GetValue<int>() : null;

decimal RequiredDecimal(JsonObject arguments, string name)
{
    if (!arguments.TryGetPropertyValue(name, out var value) || value is null)
    {
        throw new InvalidOperationException($"Argument '{name}' is required.");
    }

    return value.GetValue<decimal>();
}

async Task WriteResponseAsync(JsonNode id, JsonObject result) =>
    await WriteJsonLineAsync(new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["result"] = result
    });

async Task WriteErrorAsync(JsonNode id, Exception exception) =>
    await WriteJsonLineAsync(new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["error"] = new JsonObject
        {
            ["code"] = -32000,
            ["message"] = exception.Message
        }
    });

async Task WriteJsonLineAsync(JsonObject payload)
{
    var bytes = Encoding.UTF8.GetBytes(payload.ToJsonString(jsonOptions) + Environment.NewLine);
    await Console.OpenStandardOutput().WriteAsync(bytes);
    await Console.OpenStandardOutput().FlushAsync();
}
