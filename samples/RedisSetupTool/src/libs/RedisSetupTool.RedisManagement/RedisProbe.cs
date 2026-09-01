using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using RedisSetupTool.RedisManagement.Exercises;
using RedisSetupTool.RedisManagement.Redlock;
using RedisSetupTool.RedisManagement.Results;

namespace RedisSetupTool.RedisManagement;

/// <summary>
/// The default probe. Everything here goes through <see cref="IRedisConnection"/>, so the
/// verification logic can be exercised against a fake with no daemon in sight.
/// </summary>
public sealed class RedisProbe : IRedisProbe
{
    private readonly IRedisConnectionFactory _factory;
    private readonly IRedlockService _locks;

    /// <summary>Creates the probe.</summary>
    /// <param name="factory">The connection factory.</param>
    /// <param name="locks">The lock service, used by the quorum verification.</param>
    public RedisProbe(IRedisConnectionFactory factory, IRedlockService locks = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _locks = locks;
    }

    /// <inheritdoc />
    public async Task<RedisPingResult> PingAsync(RedisConnectionDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var endpoint = descriptor.Endpoints.Count > 0 ? descriptor.Endpoints[0].ToString() : "none";
        try
        {
            await using var connection = await ConnectAsync(descriptor, cancellationToken)
                .ConfigureAwait(false);
            var roundTrip = await connection.PingAsync(cancellationToken).ConfigureAwait(false);
            return new RedisPingResult
            {
                Succeeded = true, RoundTrip = roundTrip, Endpoint = endpoint,
            };
        }
        catch (Exception exception)
        {
            return new RedisPingResult
            {
                Succeeded = false, Endpoint = endpoint, ErrorMessage = exception.Message,
            };
        }
    }

    /// <inheritdoc />
    public async Task<RedisServerInfo> GetServerInfoAsync(RedisConnectionDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        await using var connection = await ConnectAsync(descriptor, cancellationToken)
            .ConfigureAwait(false);
        var info = ParseInfo(await connection.ExecuteAsync("INFO").ConfigureAwait(false));

        return new RedisServerInfo
        {
            Version = Read(info, "redis_version"),
            Mode = Read(info, "redis_mode"),
            Role = Read(info, "role"),
            Os = Read(info, "os"),
            ArchBits = (int)ReadLong(info, "arch_bits"),
            UptimeSeconds = ReadLong(info, "uptime_in_seconds"),
            ConnectedClients = ReadLong(info, "connected_clients"),
            UsedMemoryBytes = ReadLong(info, "used_memory"),
            MaxMemoryBytes = ReadLong(info, "maxmemory"),
            MaxMemoryPolicy = Read(info, "maxmemory_policy"),
            AofEnabled = ReadLong(info, "aof_enabled") == 1,
            RdbLastSaveTime = ReadLong(info, "rdb_last_save_time"),
            TotalKeys = ReadKeyCount(info),
        };
    }

    /// <inheritdoc />
    public async Task<RedisReplicationView> GetReplicationAsync(RedisConnectionDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        await using var connection = await ConnectAsync(descriptor, cancellationToken)
            .ConfigureAwait(false);
        return ParseReplication(await connection.ExecuteAsync("INFO", "replication")
            .ConfigureAwait(false));
    }

    /// <inheritdoc />
    public async Task<RedisClusterView> GetClusterAsync(RedisConnectionDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        await using var connection = await ConnectAsync(descriptor, cancellationToken)
            .ConfigureAwait(false);
        var info = ParseInfo(await connection.ExecuteAsync("CLUSTER", "INFO").ConfigureAwait(false));
        var nodes = ParseClusterNodes(
            await connection.ExecuteAsync("CLUSTER", "NODES").ConfigureAwait(false));

        return new RedisClusterView
        {
            State = Read(info, "cluster_state"),
            SlotsAssigned = (int)ReadLong(info, "cluster_slots_assigned"),
            SlotsOk = (int)ReadLong(info, "cluster_slots_ok"),
            KnownNodes = (int)ReadLong(info, "cluster_known_nodes"),
            Size = (int)ReadLong(info, "cluster_size"),
            Nodes = nodes,
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RedisModuleInfo>> GetModulesAsync(
        RedisConnectionDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        await using var connection = await ConnectAsync(descriptor, cancellationToken)
            .ConfigureAwait(false);
        return ParseModules(await connection.ExecuteAsync("MODULE", "LIST").ConfigureAwait(false));
    }

    /// <inheritdoc />
    public async Task<RedisTopologyVerification> VerifyAsync(RedisConnectionDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var checks = new List<RedisVerificationCheck>();

        try
        {
            switch (descriptor.Shape)
            {
                case RedisConnectionShape.PrimaryReplica:
                    await VerifyPrimaryReplicaAsync(descriptor, checks, cancellationToken)
                        .ConfigureAwait(false);
                    break;

                case RedisConnectionShape.Sentinel:
                    await VerifySentinelAsync(descriptor, checks, cancellationToken)
                        .ConfigureAwait(false);
                    break;

                case RedisConnectionShape.Cluster:
                    await VerifyClusterAsync(descriptor, checks, cancellationToken)
                        .ConfigureAwait(false);
                    break;

                case RedisConnectionShape.IndependentQuorum:
                    await VerifyQuorumAsync(descriptor, checks, cancellationToken)
                        .ConfigureAwait(false);
                    break;

                default:
                    await VerifyStandaloneAsync(descriptor, checks, cancellationToken)
                        .ConfigureAwait(false);
                    break;
            }
        }
        catch (Exception exception)
        {
            checks.Add(Fail("verification completed", exception.Message));
        }

        return Summarize(descriptor.Shape, checks);
    }

    /// <inheritdoc />
    public async Task<RedisExerciseResult> ExerciseAsync(RedisConnectionDescriptor descriptor,
        RedisExerciseOptions options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var settings = options ?? new RedisExerciseOptions();
        var steps = new List<RedisExerciseStep>();
        var clock = Stopwatch.StartNew();

        try
        {
            await using var connection = await ConnectAsync(descriptor, cancellationToken)
                .ConfigureAwait(false);

            await StepAsync(steps, "SET, GET and DEL", async () =>
            {
                var key = settings.KeyPrefix + ":scalar";
                await connection.StringSetAsync(key, "hello", TimeSpan.FromMinutes(5))
                    .ConfigureAwait(false);
                var read = await connection.StringGetAsync(key).ConfigureAwait(false);
                await connection.KeyDeleteAsync(key).ConfigureAwait(false);
                return string.Equals(read, "hello", StringComparison.Ordinal)
                    ? "round trip matched"
                    : throw new InvalidOperationException("Read back " + (read ?? "nothing") + ".");
            }).ConfigureAwait(false);

            await StepAsync(steps, "INCR", async () =>
            {
                var key = settings.KeyPrefix + ":counter";
                await connection.KeyDeleteAsync(key).ConfigureAwait(false);
                await connection.ExecuteAsync("INCR", key).ConfigureAwait(false);
                var value = await connection.ExecuteAsync("INCRBY", key, 41).ConfigureAwait(false);
                await connection.KeyDeleteAsync(key).ConfigureAwait(false);
                return "counter reached " + value;
            }).ConfigureAwait(false);

            if (settings.IncludeDataTypes)
            {
                await StepAsync(steps, "list push and range", async () =>
                {
                    var key = settings.KeyPrefix + ":list";
                    await connection.KeyDeleteAsync(key).ConfigureAwait(false);
                    await connection.ExecuteAsync("RPUSH", key, "a", "b", "c").ConfigureAwait(false);
                    var range = await connection.ExecuteAsync("LRANGE", key, 0, -1)
                        .ConfigureAwait(false);
                    await connection.KeyDeleteAsync(key).ConfigureAwait(false);
                    return range.Replace("\n", ",", StringComparison.Ordinal);
                }).ConfigureAwait(false);

                await StepAsync(steps, "hash set and read", async () =>
                {
                    var key = settings.KeyPrefix + ":hash";
                    await connection.KeyDeleteAsync(key).ConfigureAwait(false);
                    await connection.ExecuteAsync("HSET", key, "one", "1", "two", "2")
                        .ConfigureAwait(false);
                    var all = await connection.ExecuteAsync("HGETALL", key).ConfigureAwait(false);
                    await connection.KeyDeleteAsync(key).ConfigureAwait(false);
                    return all.Replace("\n", ",", StringComparison.Ordinal);
                }).ConfigureAwait(false);
            }

            if (settings.IncludePipeline)
            {
                await StepAsync(steps, "pipelined batch", async () =>
                {
                    var count = Math.Max(1, settings.KeyCount);
                    var writes = new Task<bool>[count];
                    for (var index = 0; index < count; index++)
                    {
                        //Hash tags are absent on purpose: in a cluster these keys land in different
                        //  slots, which is what proves redirection works.
                        writes[index] = connection.StringSetAsync(
                            settings.KeyPrefix + ":batch:" + index.ToString(CultureInfo.InvariantCulture),
                            index.ToString(CultureInfo.InvariantCulture), TimeSpan.FromMinutes(5));
                    }

                    await Task.WhenAll(writes).ConfigureAwait(false);

                    for (var index = 0; index < count; index++)
                    {
                        await connection.KeyDeleteAsync(settings.KeyPrefix + ":batch:"
                            + index.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
                    }

                    return count.ToString(CultureInfo.InvariantCulture) + " keys written and removed";
                }).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            steps.Add(new RedisExerciseStep
            {
                Name = "connect", Passed = false, Detail = exception.Message,
            });
        }

        var passed = 0;
        foreach (var step in steps)
        {
            if (step.Passed)
            {
                passed++;
            }
        }

        return new RedisExerciseResult
        {
            Succeeded = passed == steps.Count && steps.Count > 0,
            Steps = steps,
            Elapsed = clock.Elapsed,
            Summary = string.Format(CultureInfo.InvariantCulture, "{0} of {1} steps passed", passed,
                steps.Count),
        };
    }

    /// <summary>Parses a Redis <c>INFO</c> reply into a flat dictionary.</summary>
    /// <param name="text">The reply.</param>
    /// <returns>The fields.</returns>
    internal static Dictionary<string, string> ParseInfo(string text)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(text))
        {
            return fields;
        }

        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var separator = line.IndexOf(':');
            if (separator > 0)
            {
                fields[line[..separator]] = line[(separator + 1)..];
            }
        }

        return fields;
    }

    /// <summary>Turns an <c>INFO replication</c> reply into a view.</summary>
    /// <param name="text">The reply.</param>
    /// <returns>The view.</returns>
    internal static RedisReplicationView ParseReplication(string text)
    {
        var info = ParseInfo(text);
        var replicas = new List<RedisReplicaView>();

        for (var index = 0; ; index++)
        {
            var key = "slave" + index.ToString(CultureInfo.InvariantCulture);
            if (!info.TryGetValue(key, out var line))
            {
                break;
            }

            var parts = ParsePairs(line);
            replicas.Add(new RedisReplicaView
            {
                Endpoint = Read(parts, "ip") + ":" + Read(parts, "port"),
                State = Read(parts, "state"),
                Offset = ReadLong(parts, "offset"),
                LagSeconds = ReadLong(parts, "lag"),
            });
        }

        var master = Read(info, "master_host");
        var masterEndpoint = string.IsNullOrEmpty(master)
            ? null
            : master + ":" + Read(info, "master_port");

        return new RedisReplicationView
        {
            MasterEndpoint = masterEndpoint,
            Role = Read(info, "role"),
            ConnectedReplicas = (int)ReadLong(info, "connected_slaves"),
            Replicas = replicas,
            MasterLinkStatus = Read(info, "master_link_status"),
            FailoverState = Read(info, "master_failover_state"),
        };
    }

    /// <summary>Turns a <c>CLUSTER NODES</c> reply into views.</summary>
    /// <param name="text">The reply.</param>
    /// <returns>The nodes.</returns>
    internal static IReadOnlyList<RedisClusterNodeView> ParseClusterNodes(string text)
    {
        var nodes = new List<RedisClusterNodeView>();
        if (string.IsNullOrEmpty(text))
        {
            return nodes;
        }

        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 8)
            {
                continue;
            }

            var address = fields[1];
            var at = address.IndexOf('@');
            if (at > 0)
            {
                address = address[..at];
            }

            var slots = new List<string>();
            for (var index = 8; index < fields.Length; index++)
            {
                if (fields[index].Length > 0 && char.IsAsciiDigit(fields[index][0]))
                {
                    slots.Add(fields[index]);
                }
            }

            nodes.Add(new RedisClusterNodeView
            {
                NodeId = fields[0],
                Endpoint = address,
                IsPrimary = fields[2].Contains("master", StringComparison.Ordinal),
                PrimaryNodeId = fields[3] == "-" ? null : fields[3],
                SlotRanges = string.Join(' ', slots),
                IsConnected = string.Equals(fields[7], "connected", StringComparison.Ordinal),
            });
        }

        return nodes;
    }

    /// <summary>Turns a <c>MODULE LIST</c> reply into module records.</summary>
    /// <param name="text">The reply, already flattened to one value per line.</param>
    /// <returns>The modules.</returns>
    internal static IReadOnlyList<RedisModuleInfo> ParseModules(string text)
    {
        var modules = new List<RedisModuleInfo>();
        if (string.IsNullOrEmpty(text))
        {
            return modules;
        }

        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        string name = null;
        for (var index = 0; index < lines.Length - 1; index++)
        {
            if (string.Equals(lines[index], "name", StringComparison.Ordinal))
            {
                name = lines[index + 1];
            }
            else if (string.Equals(lines[index], "ver", StringComparison.Ordinal) && name is not null)
            {
                modules.Add(new RedisModuleInfo { Name = name, Version = lines[index + 1] });
                name = null;
            }
        }

        return modules;
    }

    private static RedisTopologyVerification Summarize(RedisConnectionShape shape,
        IReadOnlyList<RedisVerificationCheck> checks)
    {
        var passed = 0;
        var firstFailure = (string)null;
        foreach (var check in checks)
        {
            if (check.Passed)
            {
                passed++;
            }
            else
            {
                firstFailure ??= check.Name;
            }
        }

        var succeeded = checks.Count > 0 && passed == checks.Count;
        return new RedisTopologyVerification
        {
            Shape = shape,
            Succeeded = succeeded,
            Checks = checks,
            Summary = succeeded
                ? string.Format(CultureInfo.InvariantCulture, "All {0} checks passed", checks.Count)
                : string.Format(CultureInfo.InvariantCulture, "{0} of {1} checks passed; {2} failed",
                    passed, checks.Count, firstFailure ?? "one"),
        };
    }

    private Task<IRedisConnection> ConnectAsync(RedisConnectionDescriptor descriptor,
        CancellationToken cancellationToken) =>
        descriptor.Shape == RedisConnectionShape.IndependentQuorum
            ? _factory.ConnectSingleAsync(descriptor.Endpoints[0], descriptor.Credentials,
                cancellationToken)
            : _factory.ConnectAsync(descriptor, cancellationToken);

    private async Task VerifyStandaloneAsync(RedisConnectionDescriptor descriptor,
        List<RedisVerificationCheck> checks, CancellationToken cancellationToken)
    {
        await using var connection = await _factory.ConnectAsync(descriptor, cancellationToken)
            .ConfigureAwait(false);

        var roundTrip = await connection.PingAsync(cancellationToken).ConfigureAwait(false);
        checks.Add(Pass("reachable", connection.Description));
        checks.Add(Pass("PING", roundTrip.TotalMilliseconds.ToString("0.0",
            CultureInfo.InvariantCulture) + " ms"));

        var info = ParseInfo(await connection.ExecuteAsync("INFO").ConfigureAwait(false));
        checks.Add(Check("auth accepted", info.Count > 0, info.Count + " INFO fields"));

        var role = Read(info, "role");
        checks.Add(Check("role is master",
            string.Equals(role, "master", StringComparison.OrdinalIgnoreCase), "role: " + role));

        if (!string.IsNullOrEmpty(descriptor.ExpectedVersionPrefix))
        {
            var version = Read(info, "redis_version");
            checks.Add(Check("version matches the image",
                version.StartsWith(descriptor.ExpectedVersionPrefix, StringComparison.Ordinal),
                "reported " + version));
        }

        foreach (var expected in descriptor.ExpectedConfig)
        {
            var reply = await connection.ExecuteAsync("CONFIG", "GET", expected.Key)
                .ConfigureAwait(false);
            checks.Add(Check(expected.Key + " is " + expected.Value,
                reply.Contains(expected.Value, StringComparison.OrdinalIgnoreCase),
                reply.Replace("\n", " ", StringComparison.Ordinal)));
        }

        if (descriptor.ExpectedUsers.Count > 0)
        {
            var acl = await connection.ExecuteAsync("ACL", "LIST").ConfigureAwait(false);
            foreach (var user in descriptor.ExpectedUsers)
            {
                checks.Add(Check("ACL user " + user + " exists",
                    acl.Contains("user " + user, StringComparison.Ordinal), "ACL LIST read"));
            }
        }

        if (descriptor.ExpectedModules.Count > 0)
        {
            var modules = ParseModules(
                await connection.ExecuteAsync("MODULE", "LIST").ConfigureAwait(false));
            foreach (var expected in descriptor.ExpectedModules)
            {
                var found = false;
                foreach (var module in modules)
                {
                    if (string.Equals(module.Name, expected, StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        break;
                    }
                }

                checks.Add(Check("module " + expected + " loaded", found,
                    modules.Count + " modules loaded"));
            }
        }
    }

    private async Task VerifyPrimaryReplicaAsync(RedisConnectionDescriptor descriptor,
        List<RedisVerificationCheck> checks, CancellationToken cancellationToken)
    {
        if (descriptor.Endpoints.Count < 2)
        {
            checks.Add(Fail("two endpoints supplied", "only "
                + descriptor.Endpoints.Count + " given"));
            return;
        }

        await using var primary = await _factory.ConnectSingleAsync(descriptor.Endpoints[0],
            descriptor.Credentials, cancellationToken).ConfigureAwait(false);
        await using var replica = await _factory.ConnectSingleAsync(descriptor.Endpoints[1],
            descriptor.Credentials, cancellationToken).ConfigureAwait(false);

        checks.Add(Pass("primary reachable", descriptor.Endpoints[0].ToString()));
        checks.Add(Pass("replica reachable", descriptor.Endpoints[1].ToString()));

        var onPrimary = ParseReplication(
            await primary.ExecuteAsync("INFO", "replication").ConfigureAwait(false));
        checks.Add(Check("primary has a replica attached", onPrimary.ConnectedReplicas >= 1,
            "connected_slaves: " + onPrimary.ConnectedReplicas));

        var onReplica = ParseReplication(
            await replica.ExecuteAsync("INFO", "replication").ConfigureAwait(false));
        checks.Add(Check("replica link is up",
            string.Equals(onReplica.MasterLinkStatus, "up", StringComparison.OrdinalIgnoreCase),
            "master_link_status: " + onReplica.MasterLinkStatus));

        var key = "redissetup:verify:" + Guid.NewGuid().ToString("N")[..8];
        await primary.StringSetAsync(key, "propagated", TimeSpan.FromMinutes(1))
            .ConfigureAwait(false);

        var propagated = false;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var value = await replica.StringGetAsync(key).ConfigureAwait(false);
            if (string.Equals(value, "propagated", StringComparison.Ordinal))
            {
                propagated = true;
                break;
            }

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }

        await primary.KeyDeleteAsync(key).ConfigureAwait(false);
        checks.Add(Check("a write reaches the replica within 2 s", propagated, "key " + key));
    }

    private async Task VerifySentinelAsync(RedisConnectionDescriptor descriptor,
        List<RedisVerificationCheck> checks, CancellationToken cancellationToken)
    {
        var sentinels = new List<RedisHostPort>();
        foreach (var endpoint in descriptor.Endpoints)
        {
            if (endpoint.IsSentinel)
            {
                sentinels.Add(endpoint);
            }
        }

        if (sentinels.Count == 0)
        {
            checks.Add(Fail("sentinel endpoints supplied", "none were marked as sentinels"));
            return;
        }

        string agreedAddress = null;
        foreach (var sentinel in sentinels)
        {
            var single = new RedisConnectionDescriptor
            {
                Shape = RedisConnectionShape.Sentinel,
                Endpoints = [sentinel],
                ServiceName = descriptor.ServiceName,
                Credentials = descriptor.Credentials,
            };

            await using var connection = await _factory.ConnectSentinelsAsync(single,
                cancellationToken).ConfigureAwait(false);

            var address = await connection.ExecuteAsync("SENTINEL", "get-master-addr-by-name",
                descriptor.ServiceName).ConfigureAwait(false);
            address = address.Replace("\n", ":", StringComparison.Ordinal);

            checks.Add(Pass(sentinel + " reachable", "answered"));
            agreedAddress ??= address;
            checks.Add(Check(sentinel + " agrees on the master",
                string.Equals(agreedAddress, address, StringComparison.Ordinal),
                "reported " + address));

            var peers = await connection.ExecuteAsync("SENTINEL", "sentinels",
                descriptor.ServiceName).ConfigureAwait(false);
            checks.Add(Check(sentinel + " sees two peers", Count(peers, "runid") == 2,
                Count(peers, "runid") + " peers"));

            var replicas = await connection.ExecuteAsync("SENTINEL", "replicas",
                descriptor.ServiceName).ConfigureAwait(false);
            checks.Add(Check(sentinel + " sees two replicas", Count(replicas, "runid") == 2,
                Count(replicas, "runid") + " replicas"));
        }

        try
        {
            await using var master = await _factory.ConnectAsync(descriptor, cancellationToken)
                .ConfigureAwait(false);
            var roundTrip = await master.PingAsync(cancellationToken).ConfigureAwait(false);
            checks.Add(Pass("the reported master answers PING",
                roundTrip.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture) + " ms"));
        }
        catch (Exception exception)
        {
            checks.Add(Fail("the reported master answers PING", exception.Message));
        }
    }

    private async Task VerifyClusterAsync(RedisConnectionDescriptor descriptor,
        List<RedisVerificationCheck> checks, CancellationToken cancellationToken)
    {
        foreach (var endpoint in descriptor.Endpoints)
        {
            try
            {
                await using var single = await _factory.ConnectSingleAsync(endpoint,
                    descriptor.Credentials, cancellationToken).ConfigureAwait(false);
                await single.PingAsync(cancellationToken).ConfigureAwait(false);
                checks.Add(Pass("seed " + endpoint + " reachable", "answered"));
            }
            catch (Exception exception)
            {
                checks.Add(Fail("seed " + endpoint + " reachable", exception.Message));
            }
        }

        await using var connection = await _factory.ConnectAsync(descriptor, cancellationToken)
            .ConfigureAwait(false);

        var info = ParseInfo(await connection.ExecuteAsync("CLUSTER", "INFO").ConfigureAwait(false));
        checks.Add(Check("cluster_state is ok",
            string.Equals(Read(info, "cluster_state"), "ok", StringComparison.Ordinal),
            "cluster_state: " + Read(info, "cluster_state")));
        checks.Add(Check("all 16384 slots are assigned",
            ReadLong(info, "cluster_slots_assigned") == 16384,
            "assigned: " + ReadLong(info, "cluster_slots_assigned")));
        checks.Add(Check("every node is known",
            ReadLong(info, "cluster_known_nodes") == descriptor.Endpoints.Count,
            "known_nodes: " + ReadLong(info, "cluster_known_nodes")));

        var nodes = ParseClusterNodes(
            await connection.ExecuteAsync("CLUSTER", "NODES").ConfigureAwait(false));
        var primaries = 0;
        var replicas = 0;
        foreach (var node in nodes)
        {
            if (node.IsPrimary)
            {
                primaries++;
            }
            else
            {
                replicas++;
            }
        }

        var expectedPrimaries = descriptor.Endpoints.Count / 2;
        checks.Add(Check("half the nodes are primaries", primaries == expectedPrimaries,
            primaries + " primaries, " + replicas + " replicas"));

        var written = 0;
        for (var index = 0; index < 3; index++)
        {
            var key = "redissetup:verify:{" + Guid.NewGuid().ToString("N")[..8] + "}";
            try
            {
                await connection.StringSetAsync(key, index.ToString(CultureInfo.InvariantCulture),
                    TimeSpan.FromMinutes(1)).ConfigureAwait(false);
                var read = await connection.StringGetAsync(key).ConfigureAwait(false);
                if (string.Equals(read, index.ToString(CultureInfo.InvariantCulture),
                        StringComparison.Ordinal))
                {
                    written++;
                }

                await connection.KeyDeleteAsync(key).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                checks.Add(Fail("redirected write " + index, exception.Message));
            }
        }

        checks.Add(Check("writes redirect across slots", written == 3,
            written + " of 3 keys round-tripped"));
    }

    private async Task VerifyQuorumAsync(RedisConnectionDescriptor descriptor,
        List<RedisVerificationCheck> checks, CancellationToken cancellationToken)
    {
        foreach (var endpoint in descriptor.Endpoints)
        {
            try
            {
                await using var connection = await _factory.ConnectSingleAsync(endpoint,
                    descriptor.Credentials, cancellationToken).ConfigureAwait(false);
                await connection.PingAsync(cancellationToken).ConfigureAwait(false);
                checks.Add(Pass(endpoint + " answers PING", "answered"));

                var fsync = await connection.ExecuteAsync("CONFIG", "GET", "appendfsync")
                    .ConfigureAwait(false);
                checks.Add(Check(endpoint + " has appendfsync always",
                    fsync.Contains("always", StringComparison.OrdinalIgnoreCase),
                    fsync.Replace("\n", " ", StringComparison.Ordinal)));

                var replication = ParseReplication(
                    await connection.ExecuteAsync("INFO", "replication").ConfigureAwait(false));
                checks.Add(Check(endpoint + " is an independent master",
                    string.Equals(replication.Role, "master", StringComparison.OrdinalIgnoreCase)
                    && replication.ConnectedReplicas == 0,
                    "role " + replication.Role + ", " + replication.ConnectedReplicas + " replicas"));
            }
            catch (Exception exception)
            {
                checks.Add(Fail(endpoint + " answers PING", exception.Message));
            }
        }

        if (_locks is null)
        {
            return;
        }

        try
        {
            var handle = await _locks.AcquireAsync(descriptor.Endpoints, descriptor.Credentials,
                "redissetup:verify:lock", TimeSpan.FromSeconds(10), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            await using (handle.ConfigureAwait(false))
            {
                checks.Add(Check("a Redlock quorum can be taken", handle.IsHeld,
                    handle.AcquiredCount + " of " + descriptor.Endpoints.Count
                    + " masters granted it, quorum " + handle.Quorum));
            }
        }
        catch (Exception exception)
        {
            checks.Add(Fail("a Redlock quorum can be taken", exception.Message));
        }
    }

    private static async Task StepAsync(List<RedisExerciseStep> steps, string name,
        Func<Task<string>> action)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            var detail = await action().ConfigureAwait(false);
            steps.Add(new RedisExerciseStep
            {
                Name = name, Passed = true, Detail = detail, Elapsed = clock.Elapsed,
            });
        }
        catch (Exception exception)
        {
            steps.Add(new RedisExerciseStep
            {
                Name = name, Passed = false, Detail = exception.Message, Elapsed = clock.Elapsed,
            });
        }
    }

    private static RedisVerificationCheck Pass(string name, string detail) =>
        new() { Name = name, Passed = true, Detail = detail };

    private static RedisVerificationCheck Fail(string name, string detail) =>
        new() { Name = name, Passed = false, Detail = detail };

    private static RedisVerificationCheck Check(string name, bool passed, string detail) =>
        new() { Name = name, Passed = passed, Detail = detail };

    private static Dictionary<string, string> ParsePairs(string line)
    {
        var pairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in (line ?? string.Empty).Split(',',
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            if (separator > 0)
            {
                pairs[part[..separator]] = part[(separator + 1)..];
            }
        }

        return pairs;
    }

    private static int Count(string text, string token)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var count = 0;
        var index = text.IndexOf(token, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(token, index + token.Length, StringComparison.Ordinal);
        }

        return count;
    }

    private static string Read(IReadOnlyDictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out var value) ? value : string.Empty;

    private static long ReadLong(IReadOnlyDictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out var value)
        && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : 0L;

    private static long ReadKeyCount(IReadOnlyDictionary<string, string> fields)
    {
        long total = 0;
        foreach (var field in fields)
        {
            if (!field.Key.StartsWith("db", StringComparison.Ordinal))
            {
                continue;
            }

            var pairs = ParsePairs(field.Value);
            total += ReadLong(pairs, "keys");
        }

        return total;
    }
}
