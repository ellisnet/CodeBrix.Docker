using System.Net;

namespace CodeBrix.Docker;

/// <summary>
/// Thrown when the Docker daemon reports that a referenced container does not exist.
/// </summary>
public sealed class DockerContainerNotFoundException : DockerApiException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DockerContainerNotFoundException"/> class.
    /// </summary>
    /// <param name="statusCode">The HTTP status code returned by the daemon (normally 404).</param>
    /// <param name="responseBody">The raw response body returned by the daemon.</param>
    /// <param name="message">An optional message overriding the daemon's own error text.</param>
    public DockerContainerNotFoundException(HttpStatusCode statusCode, string responseBody, string message = null)
        : base(statusCode, responseBody, message)
    {
    }
}
