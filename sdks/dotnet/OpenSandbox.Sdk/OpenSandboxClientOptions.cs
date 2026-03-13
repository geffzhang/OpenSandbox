namespace OpenSandbox.Sdk;

public sealed class OpenSandboxClientOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public TimeSpan Timeout { get; set; } = System.Threading.Timeout.InfiniteTimeSpan;
    public TimeSpan WebSocketKeepAliveInterval { get; set; } = TimeSpan.FromSeconds(20);
    public TimeSpan TerminalConnectTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public OpenSandboxAuthenticationMode AuthenticationMode { get; set; } = OpenSandboxAuthenticationMode.ApiKey;
    public string? ApiKey { get; set; }
    public string? BearerToken { get; set; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            throw new InvalidOperationException($"{nameof(OpenSandboxClientOptions)}.{nameof(BaseUrl)} is required.");
        }

        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException($"{nameof(OpenSandboxClientOptions)}.{nameof(BaseUrl)} must be an absolute URL.");
        }

        if (Timeout != System.Threading.Timeout.InfiniteTimeSpan && Timeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"{nameof(OpenSandboxClientOptions)}.{nameof(Timeout)} must be greater than zero or {nameof(System.Threading.Timeout.InfiniteTimeSpan)}.");
        }

        if (WebSocketKeepAliveInterval < TimeSpan.Zero)
        {
            throw new InvalidOperationException($"{nameof(OpenSandboxClientOptions)}.{nameof(WebSocketKeepAliveInterval)} cannot be negative.");
        }

        if (TerminalConnectTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"{nameof(OpenSandboxClientOptions)}.{nameof(TerminalConnectTimeout)} must be greater than zero.");
        }

        if (AuthenticationMode == OpenSandboxAuthenticationMode.ApiKey && string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException($"{nameof(ApiKey)} is required when using API key authentication.");
        }

        if (AuthenticationMode == OpenSandboxAuthenticationMode.Bearer && string.IsNullOrWhiteSpace(BearerToken))
        {
            throw new InvalidOperationException($"{nameof(BearerToken)} is required when using bearer authentication.");
        }
    }

    public Uri GetApiBaseUri()
    {
        var normalized = BaseUrl.Trim().TrimEnd('/');
        if (!normalized.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            normalized += "/v1";
        }

        return new Uri(normalized + "/", UriKind.Absolute);
    }
}
