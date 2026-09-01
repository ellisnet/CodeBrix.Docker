using System;
using System.Net;
using System.Text.Json;

namespace CodeBrix.Docker;

/// <summary>
/// Thrown when the Docker daemon responds to an Engine API request with a non-success status code.
/// </summary>
public class DockerApiException : DockerException
{
    /// <summary>
    /// Gets the HTTP status code returned by the daemon.
    /// </summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// Gets the raw response body returned by the daemon (may be an empty string).
    /// </summary>
    public string ResponseBody { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DockerApiException"/> class.
    /// </summary>
    /// <param name="statusCode">The HTTP status code returned by the daemon.</param>
    /// <param name="responseBody">The raw response body returned by the daemon.</param>
    /// <param name="message">
    /// An optional message. When <see langword="null"/>, the daemon's <c>{"message": "..."}</c> payload is
    /// used when present, otherwise a generic message built from the status code.
    /// </param>
    public DockerApiException(HttpStatusCode statusCode, string responseBody, string message = null)
        : base(message ?? BuildMessage(statusCode, responseBody))
    {
        StatusCode = statusCode;
        ResponseBody = responseBody ?? string.Empty;
    }

    /// <summary>
    /// Attempts to extract the daemon's error text from a <c>{"message": "..."}</c> response body.
    /// </summary>
    /// <param name="responseBody">The raw response body.</param>
    /// <returns>The extracted message, or <see langword="null"/> when the body is not a daemon error payload.</returns>
    public static string TryExtractDaemonMessage(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("message", out var messageElement)
                && messageElement.ValueKind == JsonValueKind.String)
            {
                var text = messageElement.GetString();
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }
        }
        catch (JsonException)
        {
            // Not JSON — fall through and let the caller use the raw body.
        }

        return null;
    }

    private static string BuildMessage(HttpStatusCode statusCode, string responseBody)
    {
        var daemonMessage = TryExtractDaemonMessage(responseBody);
        if (daemonMessage is not null)
        {
            return daemonMessage;
        }

        var body = (responseBody ?? string.Empty).Trim();
        return body.Length == 0
            ? $"Docker API responded with status code {(int)statusCode} ({statusCode})."
            : $"Docker API responded with status code {(int)statusCode} ({statusCode}): {body}";
    }
}
