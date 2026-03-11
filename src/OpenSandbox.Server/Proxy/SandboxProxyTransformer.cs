using System.Net;
using Yarp.ReverseProxy.Forwarder;

namespace OpenSandbox.Server.Proxy;

internal sealed class SandboxProxyTransformer(string sandboxId, int port) : HttpTransformer
{
    public override async ValueTask TransformRequestAsync(HttpContext httpContext, HttpRequestMessage proxyRequest, string destinationPrefix, CancellationToken cancellationToken)
    {
        await base.TransformRequestAsync(httpContext, proxyRequest, destinationPrefix, cancellationToken);
        proxyRequest.Headers.Remove("OPEN-SANDBOX-API-KEY");
        proxyRequest.Headers.Remove("OPEN_SANDBOX_API_KEY");
    }

    public override ValueTask<bool> TransformResponseAsync(HttpContext httpContext, HttpResponseMessage? proxyResponse, CancellationToken cancellationToken)
    {
        if (proxyResponse == null)
        {
            return ValueTask.FromResult(true);
        }

        if (proxyResponse.StatusCode == HttpStatusCode.Unauthorized)
        {
            httpContext.Response.Headers["Cache-Control"] = "no-store";
        }

        if (proxyResponse.Headers.Location != null)
        {
            var location = proxyResponse.Headers.Location;
            var rewrittenPath = location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString;
            if (!string.IsNullOrWhiteSpace(rewrittenPath))
            {
                var queryIndex = rewrittenPath.IndexOf('?');
                var pathOnly = queryIndex >= 0 ? rewrittenPath[..queryIndex] : rewrittenPath;
                var queryOnly = queryIndex >= 0 ? rewrittenPath[(queryIndex + 1)..] : string.Empty;
                var normalizedPath = pathOnly.Trim();
                if (string.Equals(normalizedPath, ".", StringComparison.Ordinal) || string.Equals(normalizedPath, "./", StringComparison.Ordinal))
                {
                    normalizedPath = string.Empty;
                }
                else if (normalizedPath.StartsWith("./", StringComparison.Ordinal))
                {
                    normalizedPath = normalizedPath[2..];
                }
                else if (normalizedPath.StartsWith("/", StringComparison.Ordinal))
                {
                    normalizedPath = normalizedPath[1..];
                }

                var proxyBase = $"/v1/sandboxes/{sandboxId}/proxy/{port}";
                var finalPath = string.IsNullOrWhiteSpace(normalizedPath) ? $"{proxyBase}/" : $"{proxyBase}/{normalizedPath}";
                var finalLocation = string.IsNullOrWhiteSpace(queryOnly) ? finalPath : $"{finalPath}?{queryOnly}";
                proxyResponse.Headers.Location = new Uri(finalLocation, UriKind.Relative);
            }
        }

        base.TransformResponseAsync(httpContext, proxyResponse, cancellationToken);
        return ValueTask.FromResult(true);
    }
}
