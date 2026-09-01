using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RedisSetupTool.DockerManagement.Topologies;

/// <summary>
/// Builds the paste-ready connection text for each shape. One place, so the instance card, discovery
/// and the tests all agree on what a connection string looks like.
/// </summary>
internal static class ConnectionInfoFactory
{
    /// <summary>The command-line client every image except Valkey suggests.</summary>
    internal const string RedisCli = "redis-cli";

    /// <summary>The command-line client the Valkey image suggests.</summary>
    internal const string ValkeyCli = "valkey-cli";

    internal static ConnectionInfo Build(ConnectionShape shape, IReadOnlyList<RedisEndpoint> endpoints,
        string password, string serviceName = null, IReadOnlyList<RedisUser> users = null,
        IReadOnlyList<string> notes = null, string cliExecutable = RedisCli)
    {
        var all = endpoints ?? [];
        return new ConnectionInfo
        {
            Shape = shape,
            Endpoints = all,
            Username = "default",
            Password = password,
            ServiceName = serviceName,
            AdditionalUsers = users ?? [],
            Notes = notes ?? [],
            ConnectionString = BuildConnectionString(shape, all, password, serviceName),
            CliCommand = BuildCliCommand(shape, all, password, cliExecutable),
        };
    }

    internal static string BuildConnectionString(ConnectionShape shape,
        IReadOnlyList<RedisEndpoint> endpoints, string password, string serviceName)
    {
        if (endpoints is null || endpoints.Count == 0)
        {
            return string.Empty;
        }

        if (shape == ConnectionShape.IndependentQuorum)
        {
            var lines = new StringBuilder();
            foreach (var endpoint in endpoints)
            {
                if (lines.Length > 0)
                {
                    lines.Append('\n');
                }

                lines.Append(endpoint).Append(Suffix(password, allowAdmin: true));
            }

            return lines.ToString();
        }

        var text = new StringBuilder();
        foreach (var endpoint in endpoints)
        {
            if (shape == ConnectionShape.Sentinel && !endpoint.IsSentinel)
            {
                continue;
            }

            if (text.Length > 0)
            {
                text.Append(',');
            }

            text.Append(endpoint);
        }

        if (shape == ConnectionShape.Sentinel)
        {
            text.Append(",serviceName=").Append(serviceName);
            if (!string.IsNullOrEmpty(password))
            {
                text.Append(",password=").Append(password);
            }

            text.Append(",abortConnect=False");
            return text.ToString();
        }

        text.Append(Suffix(password, allowAdmin: true));
        return text.ToString();
    }

    internal static string BuildCliCommand(ConnectionShape shape,
        IReadOnlyList<RedisEndpoint> endpoints, string password, string cliExecutable)
    {
        if (endpoints is null || endpoints.Count == 0)
        {
            return string.Empty;
        }

        RedisEndpoint first = null;
        foreach (var endpoint in endpoints)
        {
            if (shape != ConnectionShape.Sentinel || endpoint.IsSentinel)
            {
                first = endpoint;
                break;
            }
        }

        first ??= endpoints[0];

        var text = new StringBuilder(cliExecutable);
        if (shape == ConnectionShape.Cluster)
        {
            text.Append(" -c");
        }

        text.Append(" -h ").Append(first.Host);
        text.Append(" -p ").Append(first.Port.ToString(CultureInfo.InvariantCulture));

        if (!string.IsNullOrEmpty(password) && shape != ConnectionShape.Sentinel)
        {
            text.Append(" -a ").Append(password).Append(" --no-auth-warning");
        }

        return text.ToString();
    }

    private static string Suffix(string password, bool allowAdmin)
    {
        var text = new StringBuilder();
        if (!string.IsNullOrEmpty(password))
        {
            text.Append(",password=").Append(password);
        }

        if (allowAdmin)
        {
            text.Append(",allowAdmin=True");
        }

        text.Append(",abortConnect=False");
        return text.ToString();
    }
}
