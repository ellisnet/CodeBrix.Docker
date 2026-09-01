using System;

namespace CodeBrix.Docker;

/// <summary>
/// A parsed Docker daemon endpoint.
/// </summary>
internal sealed class DockerEndpoint
{
    /// <summary>The Windows named pipe scheme.</summary>
    public const string NamedPipeScheme = "npipe";

    /// <summary>The Unix domain socket scheme.</summary>
    public const string UnixSocketScheme = "unix";

    /// <summary>The default endpoint used on Windows when nothing else is configured.</summary>
    public const string WindowsDefault = "npipe://./pipe/docker_engine";

    /// <summary>The default endpoint used on Linux and macOS when nothing else is configured.</summary>
    public const string UnixDefault = "unix:///var/run/docker.sock";

    private DockerEndpoint(DockerEndpointKind kind, string original)
    {
        Kind = kind;
        Original = original;
        PipeServer = ".";
        PipeName = string.Empty;
        SocketPath = string.Empty;
        Host = string.Empty;
    }

    /// <summary>Gets the transport kind.</summary>
    public DockerEndpointKind Kind { get; }

    /// <summary>Gets the endpoint string this instance was parsed from.</summary>
    public string Original { get; }

    /// <summary>Gets the named-pipe server name (<c>.</c> for the local machine).</summary>
    public string PipeServer { get; private init; }

    /// <summary>Gets the named-pipe name, without the <c>\\.\pipe\</c> prefix.</summary>
    public string PipeName { get; private init; }

    /// <summary>Gets the Unix domain socket path.</summary>
    public string SocketPath { get; private init; }

    /// <summary>Gets the TCP host name.</summary>
    public string Host { get; private init; }

    /// <summary>Gets the TCP port.</summary>
    public int Port { get; private init; }

    /// <summary>
    /// Resolves the daemon endpoint: the explicit option, then <c>DOCKER_HOST</c>, then the platform default.
    /// </summary>
    /// <param name="explicitEndpoint">The endpoint configured on <see cref="DockerClientOptions"/>, if any.</param>
    /// <returns>The resolved endpoint string.</returns>
    public static string Resolve(string explicitEndpoint)
    {
        if (!string.IsNullOrWhiteSpace(explicitEndpoint))
        {
            return explicitEndpoint.Trim();
        }

        var fromEnvironment = Environment.GetEnvironmentVariable("DOCKER_HOST");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment.Trim();
        }

        return OperatingSystem.IsWindows() ? WindowsDefault : UnixDefault;
    }

    /// <summary>
    /// Parses an endpoint string such as <c>npipe://./pipe/docker_engine</c>,
    /// <c>unix:///var/run/docker.sock</c> or <c>tcp://127.0.0.1:2375</c>.
    /// </summary>
    /// <param name="endpoint">The endpoint string.</param>
    /// <returns>The parsed endpoint.</returns>
    /// <exception cref="DockerException">The endpoint is empty or malformed.</exception>
    /// <exception cref="NotSupportedException">The scheme is recognized but not supported in this version.</exception>
    public static DockerEndpoint Parse(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new DockerException("The Docker endpoint is empty.");
        }

        endpoint = endpoint.Trim();

        var separator = endpoint.IndexOf("://", StringComparison.Ordinal);
        if (separator <= 0)
        {
            throw new DockerException(
                $"'{endpoint}' is not a valid Docker endpoint. Expected a value such as " +
                $"'{WindowsDefault}', '{UnixDefault}' or 'tcp://127.0.0.1:2375'.");
        }

        var scheme = endpoint[..separator].ToLowerInvariant();
        var remainder = endpoint[(separator + 3)..];

        switch (scheme)
        {
            case NamedPipeScheme:
                return ParseNamedPipe(endpoint, remainder);

            case UnixSocketScheme:
                var path = remainder.Length == 0 ? string.Empty : "/" + remainder.TrimStart('/');
                if (path.Length <= 1)
                {
                    throw new DockerException($"'{endpoint}' does not contain a Unix socket path.");
                }

                return new DockerEndpoint(DockerEndpointKind.UnixSocket, endpoint) { SocketPath = path };

            case "tcp":
            case "http":
                return ParseTcp(endpoint, remainder, defaultPort: 2375);

            case "https":
                throw new NotSupportedException(
                    "TLS-secured Docker endpoints (https://) are not supported in this version of CodeBrix.Docker.");

            default:
                throw new NotSupportedException($"The Docker endpoint scheme '{scheme}' is not supported.");
        }
    }

    private static DockerEndpoint ParseNamedPipe(string endpoint, string remainder)
    {
        // Accepted forms: npipe://./pipe/docker_engine, npipe:////./pipe/docker_engine,
        // npipe://localhost/pipe/docker_engine, npipe://\\.\pipe\docker_engine
        var normalized = remainder.Replace('\\', '/').TrimStart('/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length < 3 || !string.Equals(segments[1], "pipe", StringComparison.OrdinalIgnoreCase))
        {
            throw new DockerException(
                $"'{endpoint}' is not a valid named-pipe endpoint. Expected a value such as '{WindowsDefault}'.");
        }

        var server = segments[0];
        if (server is "localhost" or "")
        {
            server = ".";
        }

        return new DockerEndpoint(DockerEndpointKind.NamedPipe, endpoint)
        {
            PipeServer = server,
            PipeName = string.Join('/', segments[2..]),
        };
    }

    private static DockerEndpoint ParseTcp(string endpoint, string remainder, int defaultPort)
    {
        var authority = remainder.Split('/', 2)[0];
        if (authority.Length == 0)
        {
            throw new DockerException($"'{endpoint}' does not contain a host name.");
        }

        var host = authority;
        var port = defaultPort;

        var colon = authority.LastIndexOf(':');
        var closingBracket = authority.LastIndexOf(']');
        if (colon > closingBracket && colon >= 0)
        {
            host = authority[..colon];
            if (!int.TryParse(authority[(colon + 1)..], out port))
            {
                throw new DockerException($"'{endpoint}' does not contain a valid port number.");
            }
        }

        host = host.Trim('[', ']');
        if (host.Length == 0)
        {
            throw new DockerException($"'{endpoint}' does not contain a host name.");
        }

        return new DockerEndpoint(DockerEndpointKind.Tcp, endpoint) { Host = host, Port = port };
    }
}
