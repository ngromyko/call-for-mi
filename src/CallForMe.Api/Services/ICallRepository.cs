using CallForMe.Api.Models;

namespace CallForMe.Api.Services;

public interface ICallRepository
{
    Task<CallSession> CreateAsync(CallSession call, CancellationToken cancellationToken);
    Task<CallSession?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<CallSession>> ListAsync(CancellationToken cancellationToken);
    Task<CallSession?> MutateAsync(Guid id, Action<CallSession> mutate, CancellationToken cancellationToken);
}
