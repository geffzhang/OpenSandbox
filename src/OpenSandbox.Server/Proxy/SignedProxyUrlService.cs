using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using OpenSandbox.Server.Options;

namespace OpenSandbox.Server.Proxy;

public sealed class SignedProxyUrlService
{
    private const string ExpiresQueryKey = "ose";
    private const string SignatureQueryKey = "oss";
    private const string CookieName = "opensandbox_proxy_auth";

    private readonly byte[] _signingKey;
    private readonly int _lifetimeMinutes;

    public SignedProxyUrlService(IOptions<OpenSandboxServerOptions> options)
    {
        var value = options.Value;
        var secret = !string.IsNullOrWhiteSpace(value.Proxy.SignedUrlSecret)
            ? value.Proxy.SignedUrlSecret
            : value.Tokens.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(secret))
        {
            secret = Guid.NewGuid().ToString("N");
        }

        _signingKey = Encoding.UTF8.GetBytes(secret);
        _lifetimeMinutes = Math.Max(1, value.Proxy.SignedUrlLifetimeMinutes);
    }

    public SignedProxyAccess CreateAccess(HttpContext httpContext, string sandboxId, int port)
    {
        var basePath = $"{httpContext.Request.PathBase}/v1/sandboxes/{Uri.EscapeDataString(sandboxId)}/proxy/{port}";
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(_lifetimeMinutes);
        var expiresUnixTimeSeconds = expiresAt.ToUnixTimeSeconds();
        var signature = ComputeSignature(basePath, expiresUnixTimeSeconds);
        var relativePathAndQuery = $"{basePath}/?{ExpiresQueryKey}={expiresUnixTimeSeconds}&{SignatureQueryKey}={signature}";

        return new SignedProxyAccess(
            relativePathAndQuery,
            $"{httpContext.Request.Scheme}://{httpContext.Request.Host}{relativePathAndQuery}",
            expiresAt);
    }

    public bool TryAuthorize(HttpContext httpContext)
    {
        if (!TryGetProxyBasePath(httpContext.Request.PathBase, httpContext.Request.Path, out var basePath))
        {
            return false;
        }

        if (!TryValidateFromQuery(httpContext, basePath, out var expiresAt)
            && !TryValidateFromCookie(httpContext, basePath, out expiresAt))
        {
            return false;
        }

        var renewedExpiresAt = RenewExpiresAt(expiresAt);
        AppendCookie(httpContext, basePath, renewedExpiresAt);
        httpContext.Items["OpenSandbox.ProxySignedExpiresAt"] = renewedExpiresAt;
        RemoveAuthQueryParameters(httpContext);
        return true;
    }

    private DateTimeOffset RenewExpiresAt(DateTimeOffset currentExpiresAt)
    {
        var now = DateTimeOffset.UtcNow;
        var start = currentExpiresAt > now ? currentExpiresAt : now;
        return start.AddMinutes(_lifetimeMinutes);
    }

    private bool TryValidateFromQuery(HttpContext httpContext, string basePath, out DateTimeOffset expiresAt)
    {
        expiresAt = default;
        var expiresRaw = httpContext.Request.Query[ExpiresQueryKey].ToString();
        var signature = httpContext.Request.Query[SignatureQueryKey].ToString();
        return TryValidateSignature(basePath, expiresRaw, signature, out expiresAt);
    }

    private bool TryValidateFromCookie(HttpContext httpContext, string basePath, out DateTimeOffset expiresAt)
    {
        expiresAt = default;
        if (!httpContext.Request.Cookies.TryGetValue(CookieName, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split('.', 2, StringSplitOptions.TrimEntries);
        return parts.Length == 2 && TryValidateSignature(basePath, parts[0], parts[1], out expiresAt);
    }

    private bool TryValidateSignature(string basePath, string expiresRaw, string signature, out DateTimeOffset expiresAt)
    {
        expiresAt = default;
        if (string.IsNullOrWhiteSpace(expiresRaw)
            || string.IsNullOrWhiteSpace(signature)
            || !long.TryParse(expiresRaw, out var expiresUnixTimeSeconds))
        {
            return false;
        }

        expiresAt = DateTimeOffset.FromUnixTimeSeconds(expiresUnixTimeSeconds);
        if (expiresAt <= DateTimeOffset.UtcNow)
        {
            return false;
        }

        var expectedSignature = ComputeSignature(basePath, expiresUnixTimeSeconds);
        return signature.Length == expectedSignature.Length
               && CryptographicOperations.FixedTimeEquals(
                   Encoding.ASCII.GetBytes(signature),
                   Encoding.ASCII.GetBytes(expectedSignature));
    }

    private void AppendCookie(HttpContext httpContext, string basePath, DateTimeOffset expiresAt)
    {
        var expiresUnixTimeSeconds = expiresAt.ToUnixTimeSeconds();
        var signature = ComputeSignature(basePath, expiresUnixTimeSeconds);
        var value = $"{expiresUnixTimeSeconds}.{signature}";

        httpContext.Response.Cookies.Append(CookieName, value, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            Path = basePath,
            Secure = httpContext.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Expires = expiresAt
        });
    }

    private void RemoveAuthQueryParameters(HttpContext httpContext)
    {
        if (!httpContext.Request.Query.ContainsKey(ExpiresQueryKey)
            && !httpContext.Request.Query.ContainsKey(SignatureQueryKey))
        {
            return;
        }

        var pairs = new List<KeyValuePair<string, string?>>();
        foreach (var pair in httpContext.Request.Query)
        {
            if (string.Equals(pair.Key, ExpiresQueryKey, StringComparison.Ordinal)
                || string.Equals(pair.Key, SignatureQueryKey, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var value in pair.Value)
            {
                pairs.Add(new KeyValuePair<string, string?>(pair.Key, value));
            }
        }

        httpContext.Request.QueryString = QueryString.Create(pairs);
    }

    private string ComputeSignature(string basePath, long expiresUnixTimeSeconds)
    {
        using var hmac = new HMACSHA256(_signingKey);
        var payload = Encoding.UTF8.GetBytes($"{basePath}|{expiresUnixTimeSeconds}");
        return Convert.ToHexString(hmac.ComputeHash(payload)).ToLowerInvariant();
    }

    private static bool TryGetProxyBasePath(PathString pathBase, PathString path, out string basePath)
    {
        basePath = string.Empty;
        var value = path.Value;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var segments = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 5
            || !string.Equals(segments[0], "v1", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(segments[1], "sandboxes", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(segments[3], "proxy", StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(segments[4], out _))
        {
            return false;
        }

        basePath = $"{pathBase}/{string.Join('/', segments.Take(5))}";
        return true;
    }
}

public readonly record struct SignedProxyAccess(string RelativePathAndQuery, string AbsoluteUrl, DateTimeOffset ExpiresAt);
