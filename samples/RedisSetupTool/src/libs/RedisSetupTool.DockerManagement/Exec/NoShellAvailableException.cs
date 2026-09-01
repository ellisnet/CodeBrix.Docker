using System;

namespace RedisSetupTool.DockerManagement.Exec;

/// <summary>Thrown when no candidate shell exists inside the container.</summary>
public sealed class NoShellAvailableException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">The message, naming the image and the candidates tried.</param>
    public NoShellAvailableException(string message)
        : base(message)
    {
    }

    /// <summary>Gets the probe result that produced the failure.</summary>
    public ShellProbeResult Result { get; init; }
}
