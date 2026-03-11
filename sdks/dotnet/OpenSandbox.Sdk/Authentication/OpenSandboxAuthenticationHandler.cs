using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace OpenSandbox.Sdk.Authentication;

internal sealed class OpenSandboxAuthenticationHandler(IOptions<OpenSandboxClientOptions> options) : DelegatingHandler
{
    private readonly OpenSandboxClientOptions _options = options.Value;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _options.Validate();

        request.Headers.Remove("OPEN-SANDBOX-API-KEY");
        request.Headers.Remove("OPEN_SANDBOX_API_KEY");
        request.Headers.Authorization = null;

        if (_options.AuthenticationMode == OpenSandboxAuthenticationMode.ApiKey)
        {
            request.Headers.TryAddWithoutValidation("OPEN-SANDBOX-API-KEY", _options.ApiKey);
        }
        else
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.BearerToken);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
