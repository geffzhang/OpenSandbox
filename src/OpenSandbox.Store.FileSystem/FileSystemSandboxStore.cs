using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenSandbox.Abstractions.Contracts;
using OpenSandbox.Abstractions.Services;
using OpenSandbox.Store.FileSystem.Options;

namespace OpenSandbox.Store.FileSystem;

public sealed class FileSystemSandboxStore : ISandboxStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _storePath;

    public FileSystemSandboxStore(IHostEnvironment environment, IOptions<FileSystemStoreOptions> options)
    {
        var configuredPath = options.Value.StorePath;
        _storePath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(environment.ContentRootPath, configuredPath);

        var directory = Path.GetDirectoryName(_storePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public async Task<IReadOnlyList<SandboxRecord>> ListAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            return (await LoadInternalAsync(cancellationToken)).ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<SandboxRecord?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var items = await LoadInternalAsync(cancellationToken);
            return items.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpsertAsync(SandboxRecord record, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var items = await LoadInternalAsync(cancellationToken);
            var index = items.FindIndex(x => string.Equals(x.Id, record.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                items[index] = record;
            }
            else
            {
                items.Add(record);
            }

            await SaveInternalAsync(items, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RemoveAsync(string id, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var items = await LoadInternalAsync(cancellationToken);
            items.RemoveAll(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            await SaveInternalAsync(items, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<List<SandboxRecord>> LoadInternalAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_storePath))
        {
            return new List<SandboxRecord>();
        }

        await using var stream = File.OpenRead(_storePath);
        var items = await JsonSerializer.DeserializeAsync<List<SandboxRecord>>(stream, JsonOptions, cancellationToken);
        return items ?? new List<SandboxRecord>();
    }

    private async Task SaveInternalAsync(List<SandboxRecord> items, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(_storePath);
        await JsonSerializer.SerializeAsync(stream, items, JsonOptions, cancellationToken);
    }
}
