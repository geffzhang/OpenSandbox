namespace OpenSandbox.Abstractions.Contracts;

public sealed class SandboxFileEntry
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public long? SizeBytes { get; set; }
    public DateTimeOffset? LastModifiedAt { get; set; }
}

public sealed class SandboxFileReadResult
{
    public string Path { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public byte[] Content { get; set; } = [];
}
