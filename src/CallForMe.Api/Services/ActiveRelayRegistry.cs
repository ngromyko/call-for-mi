using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace CallForMe.Api.Services;

public sealed class ActiveRelayRegistry
{
    private readonly ConcurrentDictionary<Guid, RelayConnection> _connections = new();

    public void Register(Guid callId, WebSocket socket) =>
        _connections.AddOrUpdate(callId, _ => new RelayConnection(socket), (_, previous) =>
        {
            previous.Dispose();
            return new RelayConnection(socket);
        });

    public void Unregister(Guid callId, WebSocket socket)
    {
        if (_connections.TryGetValue(callId, out var connection) && ReferenceEquals(connection.Socket, socket))
        {
            _connections.TryRemove(callId, out _);
            connection.Dispose();
        }
    }

    public async Task<bool> SendTextAsync(Guid callId, string text, CancellationToken cancellationToken)
    {
        if (!_connections.TryGetValue(callId, out var connection) ||
            connection.Socket.State != WebSocketState.Open)
        {
            return false;
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = "text",
            token = text,
            last = true,
            interruptible = true,
            preemptible = true
        });

        await connection.SendGate.WaitAsync(cancellationToken);
        try
        {
            if (connection.Socket.State != WebSocketState.Open)
            {
                _connections.TryRemove(callId, out _);
                return false;
            }

            await connection.Socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken);
            return true;
        }
        catch (WebSocketException)
        {
            _connections.TryRemove(callId, out _);
            return false;
        }
        catch (ObjectDisposedException)
        {
            _connections.TryRemove(callId, out _);
            return false;
        }
        finally
        {
            try
            {
                connection.SendGate.Release();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private sealed class RelayConnection(WebSocket socket) : IDisposable
    {
        public WebSocket Socket { get; } = socket;
        public SemaphoreSlim SendGate { get; } = new(1, 1);
        public void Dispose() => SendGate.Dispose();
    }
}

public static class ConversationRelayMessageParser
{
    public static Guid? GetCallId(JsonElement root)
    {
        if (!root.TryGetProperty("customParameters", out var parameters) ||
            !parameters.TryGetProperty("callId", out var callId))
        {
            return null;
        }

        return Guid.TryParse(callId.GetString(), out var parsed) ? parsed : null;
    }

    public static string? GetVoicePrompt(JsonElement root) =>
        root.TryGetProperty("voicePrompt", out var prompt) ? prompt.GetString() : null;

    public static bool IsFinal(JsonElement root) =>
        !root.TryGetProperty("last", out var last) || last.GetBoolean();
}
