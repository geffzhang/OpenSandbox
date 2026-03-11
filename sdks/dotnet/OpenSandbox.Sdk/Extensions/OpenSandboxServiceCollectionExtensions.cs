using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenSandbox.Sdk.Authentication;

namespace OpenSandbox.Sdk.Extensions;

public static class OpenSandboxServiceCollectionExtensions
{
    public static IServiceCollection AddOpenSandboxSdk(this IServiceCollection services, Action<OpenSandboxClientOptions> configure)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configure == null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        services.AddOptions<OpenSandboxClientOptions>().Configure(configure);
        services.AddTransient<OpenSandboxAuthenticationHandler>();
        services.AddHttpClient<IOpenSandboxClient, OpenSandboxClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<OpenSandboxClientOptions>>().Value;
            options.Validate();
            client.BaseAddress = options.GetApiBaseUri();
            client.Timeout = options.Timeout;
            if (!client.DefaultRequestHeaders.Accept.Any(x => string.Equals(x.MediaType, "application/json", StringComparison.OrdinalIgnoreCase)))
            {
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            }
        }).AddHttpMessageHandler<OpenSandboxAuthenticationHandler>();

        return services;
    }
}
