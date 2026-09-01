using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using StackExchange.Redis;

namespace RedisSetupTool.RedisManagement;

/// <summary>
/// Turns a descriptor into the text a person pastes and into the options the client library takes.
/// One place, so the copy button and the connection code cannot drift apart.
/// </summary>
public static class RedisConnectionStringBuilder
{
    /// <summary>Builds the paste-ready configuration string.</summary>
    /// <param name="descriptor">The descriptor.</param>
    /// <returns>The configuration string; one line per master for a quorum.</returns>
    public static string Build(RedisConnectionDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var password = descriptor.Credentials?.Password;

        if (descriptor.Shape == RedisConnectionShape.IndependentQuorum)
        {
            var lines = new StringBuilder();
            foreach (var endpoint in descriptor.Endpoints)
            {
                if (lines.Length > 0)
                {
                    lines.Append('\n');
                }

                lines.Append(endpoint).Append(Suffix(password, descriptor.AllowAdmin));
            }

            return lines.ToString();
        }

        var text = new StringBuilder();
        foreach (var endpoint in descriptor.Endpoints)
        {
            if (descriptor.Shape == RedisConnectionShape.Sentinel && !endpoint.IsSentinel)
            {
                continue;
            }

            if (text.Length > 0)
            {
                text.Append(',');
            }

            text.Append(endpoint);
        }

        if (descriptor.Shape == RedisConnectionShape.Sentinel)
        {
            text.Append(",serviceName=").Append(descriptor.ServiceName);
            if (!string.IsNullOrEmpty(password))
            {
                text.Append(",password=").Append(password);
            }

            text.Append(",abortConnect=False");
            return text.ToString();
        }

        text.Append(Suffix(password, descriptor.AllowAdmin));
        return text.ToString();
    }

    /// <summary>Builds the client library's options object.</summary>
    /// <param name="descriptor">The descriptor.</param>
    /// <returns>The options.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown for <see cref="RedisConnectionShape.IndependentQuorum"/>: a quorum is N separate
    /// multiplexers, not one, so the caller uses the lock service instead.
    /// </exception>
    public static ConfigurationOptions BuildOptions(RedisConnectionDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (descriptor.Shape == RedisConnectionShape.IndependentQuorum)
        {
            throw new InvalidOperationException(
                "An independent quorum is N separate connections, not one. Use IRedlockService.");
        }

        var options = new ConfigurationOptions
        {
            AbortOnConnectFail = false,
            AllowAdmin = descriptor.AllowAdmin,
            ConnectTimeout = descriptor.ConnectTimeoutMs,
            SyncTimeout = descriptor.SyncTimeoutMs,
        };

        foreach (var endpoint in descriptor.Endpoints)
        {
            if (descriptor.Shape == RedisConnectionShape.Sentinel && !endpoint.IsSentinel)
            {
                continue;
            }

            options.EndPoints.Add(endpoint.Host, endpoint.Port);
        }

        if (descriptor.Shape == RedisConnectionShape.Sentinel)
        {
            //The sentinel connection itself carries no data-node password and speaks a reduced
            //  command set; the password belongs to the master connection built from it.
            options.ServiceName = descriptor.ServiceName;
            options.TieBreaker = string.Empty;
            options.CommandMap = CommandMap.Sentinel;
            options.AllowAdmin = false;
            return options;
        }

        ApplyCredentials(options, descriptor.Credentials);
        return options;
    }

    /// <summary>Builds the options for the data nodes a sentinel points at.</summary>
    /// <param name="descriptor">The descriptor.</param>
    /// <returns>The options handed to the master connection.</returns>
    public static ConfigurationOptions BuildSentinelMasterOptions(
        RedisConnectionDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var options = new ConfigurationOptions
        {
            ServiceName = descriptor.ServiceName,
            AbortOnConnectFail = false,
            AllowAdmin = descriptor.AllowAdmin,
            ConnectTimeout = descriptor.ConnectTimeoutMs,
            SyncTimeout = descriptor.SyncTimeoutMs,
        };

        ApplyCredentials(options, descriptor.Credentials);
        return options;
    }

    /// <summary>Builds a paste-ready command-line client invocation.</summary>
    /// <param name="descriptor">The descriptor.</param>
    /// <param name="endpointIndex">Which endpoint to dial.</param>
    /// <param name="cliExecutable">The client executable name.</param>
    /// <returns>The command line.</returns>
    public static string BuildCliCommand(RedisConnectionDescriptor descriptor,
        int endpointIndex = 0, string cliExecutable = "redis-cli")
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (descriptor.Endpoints.Count == 0)
        {
            return string.Empty;
        }

        var index = endpointIndex >= 0 && endpointIndex < descriptor.Endpoints.Count
            ? endpointIndex
            : 0;
        var endpoint = descriptor.Endpoints[index];

        var text = new StringBuilder(cliExecutable);
        if (descriptor.Shape == RedisConnectionShape.Cluster)
        {
            text.Append(" -c");
        }

        text.Append(" -h ").Append(endpoint.Host);
        text.Append(" -p ").Append(endpoint.Port.ToString(CultureInfo.InvariantCulture));

        var password = descriptor.Credentials?.Password;
        if (!string.IsNullOrEmpty(password) && !endpoint.IsSentinel)
        {
            text.Append(" -a ").Append(password).Append(" --no-auth-warning");
        }

        return text.ToString();
    }

    private static void ApplyCredentials(ConfigurationOptions options, RedisCredentials credentials)
    {
        if (credentials is null)
        {
            return;
        }

        if (!string.IsNullOrEmpty(credentials.Username)
            && !string.Equals(credentials.Username, "default", StringComparison.Ordinal))
        {
            options.User = credentials.Username;
        }

        if (!string.IsNullOrEmpty(credentials.Password))
        {
            options.Password = credentials.Password;
        }
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
