namespace OpenSandbox.Server.Options;

public sealed class OpenSandboxServerOptions
{
    public const string SectionName = "OpenSandbox";

    public List<string> Tokens { get; set; } = new();
    public int CleanupIntervalSeconds { get; set; } = 30;
    public string ProxyUpstreamHost { get; set; } = "127.0.0.1";
    public string? EndpointHost { get; set; }
    public ProxyOptions Proxy { get; set; } = new();
}

public sealed class ProxyOptions
{
    public int ActivityTimeoutMinutes { get; set; } = 10;
    public int SignedUrlLifetimeMinutes { get; set; } = 10;
    public string? SignedUrlSecret { get; set; }
}
