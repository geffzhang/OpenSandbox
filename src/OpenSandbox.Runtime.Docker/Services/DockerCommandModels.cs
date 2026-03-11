using System.Text.Json.Serialization;

namespace OpenSandbox.Runtime.Docker;

internal sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);

internal sealed class DockerInspectResponse
{
    [JsonPropertyName("Id")]
    public string? Id { get; set; }

    [JsonPropertyName("State")]
    public DockerStateResponse? State { get; set; }
}

internal sealed class DockerStateResponse
{
    [JsonPropertyName("Status")]
    public string? Status { get; set; }

    [JsonPropertyName("Paused")]
    public bool Paused { get; set; }

    [JsonPropertyName("ExitCode")]
    public int ExitCode { get; set; }

    [JsonPropertyName("Error")]
    public string? Error { get; set; }
}

internal sealed class DockerStatsResponse
{
    [JsonPropertyName("CPUPerc")]
    public string? CpuPercent { get; set; }

    [JsonPropertyName("MemUsage")]
    public string? MemoryUsage { get; set; }

    [JsonPropertyName("MemPerc")]
    public string? MemoryPercent { get; set; }
}
