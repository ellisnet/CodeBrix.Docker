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

    /// <summary>The SSH scheme, which reaches a remote daemon through the system SSH client.</summary>
    public const string SshScheme = "ssh";

    /// <summary>The port used when an <c>ssh://</c> endpoint does not name one.</summary>
    public const int DefaultSshPort = 22;

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
        UserName = string.Empty;
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

    /// <summary>Gets the TCP or SSH host name.</summary>
    public string Host { get; private init; }

    /// <summary>Gets the TCP or SSH port.</summary>
    public int Port { get; private init; }

    /// <summary>
    /// Gets the SSH user name, or an empty string when the endpoint leaves the user to the SSH
    /// client's own configuration.
    /// </summary>
    public string UserName { get; private init; }

    /// <summary>
    /// Gets the SSH destination in <c>[user@]host</c> form, as it is handed to the SSH client.
    /// </summary>
    public string SshDestination => UserName.Length == 0 ? Host : $"{UserName}@{Host}";

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
    /// <c>unix:///var/run/docker.sock</c>, <c>tcp://127.0.0.1:2375</c> or
    /// <c>ssh://user@host:2222</c>.
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

            case SshScheme:
                return ParseSsh(endpoint, remainder);

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
        var (host, port) = ParseAuthority(endpoint, remainder.Split('/', 2)[0], defaultPort);
        return new DockerEndpoint(DockerEndpointKind.Tcp, endpoint) { Host = host, Port = port };
    }

    /// <summary>
    /// Parses <c>ssh://[user@]host[:port]</c>. The remote socket is never named here: the far end is
    /// always whatever <c>docker system dial-stdio</c> opens there, exactly as with Docker's own CLI.
    /// </summary>
    private static DockerEndpoint ParseSsh(string endpoint, string remainder)
    {
        var slash = remainder.IndexOf('/');
        var authority = slash < 0 ? remainder : remainder[..slash];
        var path = slash < 0 ? string.Empty : remainder[(slash + 1)..].Trim('/');

        if (path.Length > 0)
        {
            throw new DockerException(
                $"'{endpoint}' carries the path '{path}', which an ssh:// endpoint cannot use. Expected a " +
                "value such as 'ssh://user@host' or 'ssh://user@host:2222'; the daemon on the far end is " +
                "always the one 'docker system dial-stdio' reaches there.");
        }

        var user = string.Empty;
        var at = authority.LastIndexOf('@');
        if (at >= 0)
        {
            user = authority[..at];
            authority = authority[(at + 1)..];

            if (user.Length == 0)
            {
                throw new DockerException(
                    $"'{endpoint}' has an empty user name. Write 'ssh://host' to let the SSH client choose " +
                    "the user, or 'ssh://user@host' to name one.");
            }
        }

        var (host, port) = ParseAuthority(endpoint, authority, DefaultSshPort);
        return new DockerEndpoint(DockerEndpointKind.Ssh, endpoint) { Host = host, Port = port, UserName = user };
    }

    /// <summary>Splits a <c>host</c>, <c>host:port</c> or <c>[v6::address]:port</c> authority.</summary>
    private static (string Host, int Port) ParseAuthority(string endpoint, string authority, int defaultPort)
    {
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

        return (host, port);
    }
}
