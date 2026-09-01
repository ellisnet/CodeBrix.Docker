using System;

namespace CodeBrix.Docker;

/// <summary>
/// Base type for every exception thrown by CodeBrix.Docker.
/// </summary>
public class DockerException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DockerException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public DockerException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DockerException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused the current exception.</param>
    public DockerException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
