using System.Text.Json;
using System.Text.Json.Serialization;
using CallForMe.Api.Models;
using CallForMe.Api.Options;
using Microsoft.Extensions.Options;

namespace CallForMe.Api.Services;

public interface ICallRepository
{
    Task<CallSession> CreateAsync(CallSession call, CancellationToken cancellationToken);
    Task<CallSession?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<CallSession>> ListAsync(CancellationToken cancellationToken);
    Task<CallSession?> MutateAsync(Guid id, Action<CallSession> mutate, CancellationToken cancellationToken);
}

public sealed class JsonCallRepository : ICallRepository
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public JsonCallRepository(IOptions<StorageOptions> options, IHostEnvironment environment)
    {
        _path = Path.IsPathRooted(options.Value.FilePath)
            ? options.Value.FilePath
            : Path.Combine(environment.ContentRootPath, options.Value.FilePath);
    }

    public async Task<CallSession> CreateAsync(CallSession call, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var calls = await LoadAsync(cancellationToken);
            calls.Add(call);
            await SaveAsync(calls, cancellationToken);
            return call;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CallSession?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return (await LoadAsync(cancellationToken)).FirstOrDefault(call => call.Id == id && IsRealCall(call));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<CallSession>> ListAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return (await LoadAsync(cancellationToken))
                .Where(IsRealCall)
                .OrderByDescending(call => call.CreatedAt)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CallSession?> MutateAsync(
        Guid id,
        Action<CallSession> mutate,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var calls = await LoadAsync(cancellationToken);
            var call = calls.FirstOrDefault(candidate => candidate.Id == id);
            if (call is null)
            {
                return null;
            }

            mutate(call);
            await SaveAsync(calls, cancellationToken);
            return call;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<CallSession>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        await using var stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<List<CallSession>>(stream, _jsonOptions, cancellationToken) ?? [];
    }

    private static bool IsRealCall(CallSession call) =>
        !call.Hidden &&
        (string.IsNullOrWhiteSpace(call.TwilioCallSid) ||
        !call.TwilioCallSid.StartsWith("SIM", StringComparison.OrdinalIgnoreCase));

    private async Task SaveAsync(List<CallSession> calls, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, calls, _jsonOptions, cancellationToken);
        }

        File.Move(temporaryPath, _path, true);
    }
}
