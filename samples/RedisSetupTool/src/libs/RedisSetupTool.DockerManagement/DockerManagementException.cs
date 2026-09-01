using System;

namespace RedisSetupTool.DockerManagement;

/// <summary>
/// The one exception type this library throws for daemon failures. Every CodeBrix.Docker exception is
/// translated into this, so no consumer needs the library's exception hierarchy.
/// </summary>
public sealed class DockerManagementException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">The message, already daemon-readable.</param>
    /// <param name="innerException">The exception being translated, when there is one.</param>
    public DockerManagementException(string message, Exception innerException = null)
        : base(message, innerException)
    {
    }

    /// <summary>Gets a value indicating whether the daemon reported the resource as missing.</summary>
    public bool IsNotFound { get; init; }

    /// <summary>Gets the HTTP status code the daemon answered with, when the failure came from the API.</summary>
    public int? StatusCode { get; init; }

    /// <summary>Gets any extra detail - a response body, or a command's standard error.</summary>
    public string Detail { get; init; }
}
