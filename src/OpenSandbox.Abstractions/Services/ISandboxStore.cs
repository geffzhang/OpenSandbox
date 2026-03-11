using OpenSandbox.Abstractions.Contracts;

namespace OpenSandbox.Abstractions.Services;

public interface ISandboxStore
{
    Task<IReadOnlyList<SandboxRecord>> ListAsync(CancellationToken cancellationToken = default);
    Task<SandboxRecord?> GetAsync(string id, CancellationToken cancellationToken = default);
    Task UpsertAsync(SandboxRecord record, CancellationToken cancellationToken = default);
    Task RemoveAsync(string id, CancellationToken cancellationToken = default);
}
