namespace OpenSandbox.Sdk.Models;

public sealed class SandboxImageReference
{
    public string Uri { get; set; } = string.Empty;
}

public sealed class SandboxResourceLimits
{
    public string? Cpu { get; set; }
    public string? Memory { get; set; }
}

public sealed class SandboxHostVolume
{
    public string Path { get; set; } = string.Empty;
}

public sealed class SandboxVolume
{
    public string Name { get; set; } = string.Empty;
    public string MountPath { get; set; } = string.Empty;
    public bool? ReadOnly { get; set; }
    public string? SubPath { get; set; }
    public SandboxHostVolume? Host { get; set; }
}

public sealed class SandboxNetworkPolicy
{
    public string? DefaultAction { get; set; }
}

public sealed class SandboxStatus
{
    public string State { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string? Message { get; set; }
}

public sealed class PaginationInfo
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages { get; set; }
    public bool HasNextPage { get; set; }
}
