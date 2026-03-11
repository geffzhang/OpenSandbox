namespace OpenSandbox.Abstractions.Contracts;

public sealed class SandboxRuntimeState
{
    public string State { get; set; } = SandboxStateNames.Error;
    public string? Reason { get; set; }
    public string? Message { get; set; }
    public string? ContainerId { get; set; }
}

public sealed class SandboxRuntimeUsage
{
    public decimal? CpuPercent { get; set; }
    public decimal? MemoryPercent { get; set; }
    public string? MemoryUsage { get; set; }
    public string? MemoryLimit { get; set; }
    public DateTimeOffset CollectedAt { get; set; }
}

public sealed class SandboxCommandResult
{
    public int ExitCode { get; set; }
    public string StdOut { get; set; } = string.Empty;
    public string StdErr { get; set; } = string.Empty;
}
