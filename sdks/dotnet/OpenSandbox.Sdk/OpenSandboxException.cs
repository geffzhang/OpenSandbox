using System.Net;
using System.Text.Json;

namespace OpenSandbox.Sdk;

public sealed class OpenSandboxException : Exception
{
    public OpenSandboxException(string message, HttpStatusCode? statusCode = null, string? errorCode = null, string? responseBody = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        ResponseBody = responseBody;
    }

    public HttpStatusCode? StatusCode { get; }
    public string? ErrorCode { get; }
    public string? ResponseBody { get; }

    internal static OpenSandboxException FromResponse(string operation, HttpStatusCode statusCode, string? responseBody)
    {
        string? errorCode = null;
        string? errorMessage = null;

        if (!string.IsNullOrWhiteSpace(responseBody))
        {
            try
            {
                var envelope = JsonSerializer.Deserialize<OpenSandboxErrorEnvelope>(responseBody, OpenSandboxClient.JsonOptions);
                errorCode = envelope?.Error?.Code;
                errorMessage = envelope?.Error?.Message;
            }
            catch
            {
            }
        }

        var message = !string.IsNullOrWhiteSpace(errorMessage)
            ? $"{operation} failed: {errorMessage}"
            : $"{operation} failed with status code {(int)statusCode}.";
        return new OpenSandboxException(message, statusCode, errorCode, responseBody);
    }

    internal sealed class OpenSandboxErrorEnvelope
    {
        public OpenSandboxError? Error { get; set; }
    }

    internal sealed class OpenSandboxError
    {
        public string? Code { get; set; }
        public string? Message { get; set; }
    }
}
