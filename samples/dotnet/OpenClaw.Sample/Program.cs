using System.Text.Json;
using OpenSandbox.Sdk;
using OpenSandbox.Sdk.Models;

const string openClawHomePath = "/home/node";
const string mountedRootPath = "/mnt/openclaw-data";
const int defaultGatewayPort = 18789;
const string defaultImage = "hejiale010426/openclaw:local";
const string defaultCpu = "1000m";
const string defaultMemory = "2Gi";

var baseUrl = Environment.GetEnvironmentVariable("OPEN_SANDBOX_BASE_URL") ?? "http://localhost:8080";
var apiKey = Environment.GetEnvironmentVariable("OPEN_SANDBOX_API_KEY");
var bearerToken = Environment.GetEnvironmentVariable("OPEN_SANDBOX_BEARER_TOKEN");
var hostVolume = Environment.GetEnvironmentVariable("OPENCLAW_HOST_VOLUME") ?? "/data/openclaw";
var dataDirectory = Environment.GetEnvironmentVariable("OPENCLAW_DATA_DIRECTORY") ?? "users/demo/main";
var openClawImage = Environment.GetEnvironmentVariable("OPENCLAW_IMAGE") ?? defaultImage;
var routinApiKey = Environment.GetEnvironmentVariable("OPENCLAW_ROUTIN_API_KEY") ?? Environment.GetEnvironmentVariable("ROUTIN_API_KEY");
var gatewayToken = Environment.GetEnvironmentVariable("OPENCLAW_GATEWAY_TOKEN") ?? $"oc_demo_{Guid.NewGuid():N}";
var gatewayPort = ParseInt(Environment.GetEnvironmentVariable("OPENCLAW_GATEWAY_PORT"), defaultGatewayPort);

if (string.IsNullOrWhiteSpace(apiKey) && string.IsNullOrWhiteSpace(bearerToken))
{
    Console.WriteLine("Set OPEN_SANDBOX_API_KEY or OPEN_SANDBOX_BEARER_TOKEN before running the sample.");
    return;
}

if (string.IsNullOrWhiteSpace(routinApiKey))
{
    Console.WriteLine("Set OPENCLAW_ROUTIN_API_KEY or ROUTIN_API_KEY before running the sample.");
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

Console.WriteLine($"OpenSandbox: {baseUrl}");
Console.WriteLine($"OpenClaw image: {openClawImage}");
Console.WriteLine($"Host volume: {hostVolume}");
Console.WriteLine($"Data directory: {dataDirectory}");

var request = new CreateSandboxRequest
{
    Image = new SandboxImageReference { Uri = openClawImage },
    Timeout = 86400,
    ResourceLimits = new SandboxResourceLimits
    {
        Cpu = defaultCpu,
        Memory = defaultMemory
    },
    Metadata = new Dictionary<string, string>
    {
        ["name"] = "openclaw-sample",
        ["sample"] = "openclaw"
    },
    Env = new Dictionary<string, string>
    {
        ["HOME"] = openClawHomePath,
        ["TERM"] = "xterm-256color",
        ["NODE_ENV"] = "production",
        ["OPENCLAW_GATEWAY_TOKEN"] = gatewayToken,
        ["ROUTIN_API_KEY"] = routinApiKey,
        ["XDG_CONFIG_HOME"] = "/home/node/.openclaw",
        ["OPENCLAW_STATE_DIR"] = "/home/node/.openclaw",
        ["OPENCLAW_RUNTIME_VERSION"] = "2"
    },
    Volumes =
    [
        new SandboxVolume
        {
            Name = "openclaw-root",
            MountPath = mountedRootPath,
            ReadOnly = false,
            Host = new SandboxHostVolume
            {
                Path = hostVolume
            }
        }
    ],
    Entrypoint =
    [
        "sh",
        "-lc",
        BuildEntrypointScript(BuildDefaultConfigJson(gatewayToken), dataDirectory, gatewayPort)
    ]
};

var created = await client.CreateSandboxAsync(request);
Console.WriteLine($"Sandbox created: {created.Id}");
Console.WriteLine($"Terminal WS: {client.BuildTerminalWebSocketUri(created.Id)}");

SandboxEndpoint? endpoint = null;
for (var attempt = 1; attempt <= 20; attempt++)
{
    endpoint = await client.GetSandboxEndpointAsync(created.Id, gatewayPort);
    if (!string.IsNullOrWhiteSpace(endpoint?.Url))
    {
        break;
    }

    Console.WriteLine($"Waiting for endpoint... ({attempt}/20)");
    await Task.Delay(TimeSpan.FromSeconds(3));
}

if (!string.IsNullOrWhiteSpace(endpoint?.Url))
{
    Console.WriteLine($"OpenClaw URL: {endpoint.Url}");
    Console.WriteLine($"Gateway token: {gatewayToken}");
}
else
{
    Console.WriteLine("Endpoint not ready yet. Query it later with GetSandboxEndpointAsync.");
}

return;

static int ParseInt(string? raw, int fallback)
{
    return int.TryParse(raw, out var value) && value > 0 ? value : fallback;
}

static string BuildEntrypointScript(string finalConfigJson, string dataDirectory, int port)
{
    return $$"""
             set -e
             mkdir -p {{mountedRootPath}}
             DATA_DIR="{{mountedRootPath}}/{{Escape(dataDirectory)}}"
             mkdir -p "$DATA_DIR/workspace" "$DATA_DIR/.npm-global" "$DATA_DIR/.npm-cache" "$DATA_DIR/extensions"
             mkdir -p {{openClawHomePath}}
             if [ -e {{openClawHomePath}}/.openclaw ] && [ ! -L {{openClawHomePath}}/.openclaw ]; then
               rm -rf {{openClawHomePath}}/.openclaw
             fi
             ln -sfn "$DATA_DIR" {{openClawHomePath}}/.openclaw
             chmod 755 "$DATA_DIR" "$DATA_DIR/workspace" "$DATA_DIR/.npm-global" "$DATA_DIR/.npm-cache" "$DATA_DIR/extensions" {{openClawHomePath}} 2>/dev/null || true
             export HOME={{openClawHomePath}}
             export XDG_CONFIG_HOME={{openClawHomePath}}/.openclaw
             export OPENCLAW_STATE_DIR={{openClawHomePath}}/.openclaw
             export NPM_CONFIG_PREFIX="$DATA_DIR/.npm-global"
             export NPM_CONFIG_CACHE="$DATA_DIR/.npm-cache"
             export PATH="$NPM_CONFIG_PREFIX/bin:$PATH"
             cat <<'EOF' > {{openClawHomePath}}/.openclaw/openclaw.json
             {{finalConfigJson}}
             EOF
             exec node dist/index.js gateway --bind lan --port {{port}}
             """;
}

static string BuildDefaultConfigJson(string gatewayToken)
{
    var config = new
    {
        tools = new
        {
            allow = new[] { "*" },
            profile = "full"
        },
        agents = new
        {
            defaults = new
            {
                model = new
                {
                    primary = "routin/mimo-v2-flash"
                },
                workspace = "~/.openclaw/workspace"
            }
        },
        models = new
        {
            mode = "merge",
            providers = new Dictionary<string, object>
            {
                ["routin"] = new
                {
                    api = "openai-completions",
                    apiKey = "${ROUTIN_API_KEY}",
                    models = new object[]
                    {
                        new
                        {
                            id = "mimo-v2-flash",
                            name = "Mimo V2 Flash",
                            input = new[] { "text" },
                            maxTokens = 32000,
                            reasoning = false,
                            contextWindow = 200000
                        }
                    },
                    baseUrl = "https://api.routin.ai/v1"
                }
            }
        },
        gateway = new
        {
            auth = new
            {
                mode = "token",
                token = gatewayToken
            },
            bind = "lan",
            mode = "local",
            controlUi = new
            {
                enabled = true,
                dangerouslyDisableDeviceAuth = true,
                dangerouslyAllowHostHeaderOriginFallback = true
            }
        }
    };

    return JsonSerializer.Serialize(config, new JsonSerializerOptions
    {
        WriteIndented = true
    });
}

static string Escape(string value)
{
    return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
