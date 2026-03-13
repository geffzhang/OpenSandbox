using OpenSandbox.Sdk;
using OpenSandbox.Sdk.Models;

var baseUrl = Environment.GetEnvironmentVariable("OPEN_SANDBOX_BASE_URL") ?? "http://localhost:8080";
var apiKey = Environment.GetEnvironmentVariable("OPEN_SANDBOX_API_KEY");
var bearerToken = Environment.GetEnvironmentVariable("OPEN_SANDBOX_BEARER_TOKEN");
var sampleMode = Environment.GetEnvironmentVariable("OPEN_SANDBOX_SAMPLE_MODE") ?? "basic";

if (string.IsNullOrWhiteSpace(apiKey) && string.IsNullOrWhiteSpace(bearerToken))
{
    Console.WriteLine("Set OPEN_SANDBOX_API_KEY or OPEN_SANDBOX_BEARER_TOKEN before running the sample.");
    return;
}

var options = new OpenSandboxClientOptions
{
    BaseUrl = baseUrl,
    AuthenticationMode = string.IsNullOrWhiteSpace(bearerToken)
        ? OpenSandboxAuthenticationMode.ApiKey
        : OpenSandboxAuthenticationMode.Bearer,
    ApiKey = apiKey,
    BearerToken = bearerToken,
    WebSocketKeepAliveInterval = TimeSpan.FromSeconds(20),
    TerminalConnectTimeout = TimeSpan.FromSeconds(30)
};

using var httpClient = new HttpClient();
var client = new OpenSandboxClient(httpClient, options);

var ping = await client.PingAsync();
Console.WriteLine($"Ping: {ping}");

var sandboxes = await client.ListSandboxesAsync(new ListSandboxesRequest { Page = 1, PageSize = 10 });
Console.WriteLine($"Sandboxes: {sandboxes.Items.Count}");

if (string.Equals(sampleMode, "openclaw", StringComparison.OrdinalIgnoreCase))
{
    var request = BuildOpenClawRequest();
    var created = await client.CreateSandboxAsync(request);

    Console.WriteLine($"Created OpenClaw sandbox: {created.Id}");
    Console.WriteLine($"State: {created.Status?.State ?? "unknown"}");
    Console.WriteLine($"Terminal WS: {client.BuildTerminalWebSocketUri(created.Id)}");
    var endpoint = await client.GetSandboxEndpointAsync(created.Id, 18789);
    Console.WriteLine($"Proxy URL: {endpoint?.Url ?? "not ready"}");
    if (endpoint?.ExpiresAt.HasValue == true)
    {
        Console.WriteLine($"Proxy expires at (UTC): {endpoint.ExpiresAt.Value:O}");
    }
}
else
{
    Console.WriteLine("Set OPEN_SANDBOX_SAMPLE_MODE=openclaw to run the OpenClaw deployment example.");
}

static CreateSandboxRequest BuildOpenClawRequest()
{
    var image = Environment.GetEnvironmentVariable("OPENCLAW_IMAGE") ?? "ghcr.io/openclaw/openclaw:latest";
    var gatewayToken = Environment.GetEnvironmentVariable("OPENCLAW_GATEWAY_TOKEN") ?? $"oc_{Guid.NewGuid():N}";
    var routinApiKey = Environment.GetEnvironmentVariable("ROUTIN_API_KEY") ?? "replace-me";
    var hostVolumePath = Environment.GetEnvironmentVariable("OPENCLAW_HOST_VOLUME") ?? "/data/opensandbox";
    var dataDirectory = Environment.GetEnvironmentVariable("OPENCLAW_DATA_DIRECTORY") ?? "users/demo/main";
    var openClawPort = ParseInt(Environment.GetEnvironmentVariable("OPENCLAW_GATEWAY_PORT"), 18789);

    var entrypoint = BuildOpenClawEntrypoint(dataDirectory, openClawPort);

    return new CreateSandboxRequest
    {
        Image = new SandboxImageReference { Uri = image },
        Timeout = 86400,
        Entrypoint = new List<string> { "sh", "-lc", entrypoint },
        ResourceLimits = new SandboxResourceLimits
        {
            Cpu = Environment.GetEnvironmentVariable("OPENCLAW_CPU") ?? "2",
            Memory = Environment.GetEnvironmentVariable("OPENCLAW_MEMORY") ?? "4Gi"
        },
        Env = new Dictionary<string, string>
        {
            ["HOME"] = "/home/node",
            ["TERM"] = "xterm-256color",
            ["NODE_ENV"] = "production",
            ["OPENCLAW_GATEWAY_TOKEN"] = gatewayToken,
            ["ROUTIN_API_KEY"] = routinApiKey,
            ["XDG_CONFIG_HOME"] = "/home/node/.openclaw",
            ["OPENCLAW_STATE_DIR"] = "/home/node/.openclaw"
        },
        Metadata = new Dictionary<string, string>
        {
            ["name"] = "openclaw-demo",
            ["scenario"] = "opensandbox-sdk-sample"
        },
        Volumes = new List<SandboxVolume>
        {
            new()
            {
                Name = "openclaw-root",
                MountPath = "/mnt/openclaw-data",
                ReadOnly = false,
                Host = new SandboxHostVolume { Path = hostVolumePath }
            }
        },
        NetworkPolicy = new SandboxNetworkPolicy
        {
            DefaultAction = "Allow"
        }
    };
}

static string BuildOpenClawEntrypoint(string dataDirectory, int port)
{
    return $"set -e\n" +
           "mkdir -p /mnt/openclaw-data\n" +
           $"DATA_DIR=\"/mnt/openclaw-data/{EscapeShellDoubleQuoted(dataDirectory)}\"\n" +
           "mkdir -p \"$DATA_DIR/workspace\" \"$DATA_DIR/.npm-global\" \"$DATA_DIR/.npm-cache\" \"$DATA_DIR/extensions\"\n" +
           "mkdir -p /home/node\n" +
           "if [ -e /home/node/.openclaw ] && [ ! -L /home/node/.openclaw ]; then rm -rf /home/node/.openclaw; fi\n" +
           "ln -sfn \"$DATA_DIR\" /home/node/.openclaw\n" +
           "chmod 755 \"$DATA_DIR\" \"$DATA_DIR/workspace\" \"$DATA_DIR/.npm-global\" \"$DATA_DIR/.npm-cache\" \"$DATA_DIR/extensions\" /home/node 2>/dev/null || true\n" +
           "export HOME=/home/node\n" +
           "export XDG_CONFIG_HOME=/home/node/.openclaw\n" +
           "export OPENCLAW_STATE_DIR=/home/node/.openclaw\n" +
           "export NPM_CONFIG_PREFIX=\"$DATA_DIR/.npm-global\"\n" +
           "export NPM_CONFIG_CACHE=\"$DATA_DIR/.npm-cache\"\n" +
           "export PATH=\"$NPM_CONFIG_PREFIX/bin:$PATH\"\n" +
           "exec node dist/index.js gateway --bind lan --port " + port;
}

static string EscapeShellDoubleQuoted(string value)
{
    return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}

static int ParseInt(string? value, int fallback)
{
    return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
}
