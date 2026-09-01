using System;
using System.Collections.Generic;

namespace RedisSetupTool.DockerManagement.Topologies;

/// <summary>
/// The thirteen approved topologies, described once. Nothing else in the library hard-codes a
/// container count, an image or a parameter list.
/// </summary>
public static class TopologyCatalog
{
    /// <summary>The Redis 8 image every topology uses unless it says otherwise.</summary>
    public const string Redis8Image = "redis:8-alpine";

    /// <summary>The Redis 6.2 compatibility floor image.</summary>
    public const string Redis62Image = "redis:6.2-alpine";

    /// <summary>The Valkey image.</summary>
    public const string ValkeyImage = "valkey/valkey:8.1-alpine";

    private static readonly TopologyDescriptor[] Descriptors = Build();

    private static readonly Dictionary<TopologyId, TopologyDescriptor> ById = Index();

    /// <summary>Gets every descriptor, in catalog order.</summary>
    public static IReadOnlyList<TopologyDescriptor> All => Descriptors;

    /// <summary>Gets one descriptor.</summary>
    /// <param name="id">The topology.</param>
    /// <returns>The descriptor.</returns>
    public static TopologyDescriptor Get(TopologyId id) =>
        ById.TryGetValue(id, out var descriptor)
            ? descriptor
            : throw new ArgumentOutOfRangeException(nameof(id), id, "No such topology.");

    /// <summary>Parses a two-character code such as <c>D2</c>.</summary>
    /// <param name="code">The code, in any casing.</param>
    /// <param name="id">The topology the code names.</param>
    /// <returns>True when the code is one of the thirteen.</returns>
    public static bool TryParseCode(string code, out TopologyId id)
    {
        id = default;
        return !string.IsNullOrWhiteSpace(code)
            && Enum.TryParse(code.Trim(), ignoreCase: true, out id)
            && ById.ContainsKey(id);
    }

    private static Dictionary<TopologyId, TopologyDescriptor> Index()
    {
        var map = new Dictionary<TopologyId, TopologyDescriptor>();
        foreach (var descriptor in Descriptors)
        {
            map.Add(descriptor.Id, descriptor);
        }

        return map;
    }

    private static TopologyParameter Password(bool required, string label = "Password") => new()
    {
        Key = "password",
        Label = label,
        Kind = TopologyParameterKind.Password,
        DefaultValue = required ? TopologyParameter.GeneratedToken : string.Empty,
        HelpText = required
            ? "Sets requirepass and masterauth. Leave the generated value unless you need a known one."
            : "Optional. When set, the node requires this password and the connection string carries it.",
        IsRequired = required,
    };

    private static TopologyDescriptor[] Build() =>
    [
        new()
        {
            Id = TopologyId.A1,
            Code = "A1",
            DisplayName = "Plain standalone",
            Category = TopologyCategory.SingleNode,
            Summary = "One Redis 8 node, no password, no persistence.",
            Detail = "The simplest thing that answers PING. Saving is switched off explicitly, so " +
                "\"plain\" really is plain rather than inheriting the image's built-in save policy. " +
                "Use it to try a client, or as the baseline the other single-node presets vary from.",
            Image = Redis8Image,
            ContainerCount = 1,
            DataPortCount = 1,
            ConnectionShape = ConnectionShape.Standalone,
            Highlights = ["no auth", "no persistence"],
        },
        new()
        {
            Id = TopologyId.A2,
            Code = "A2",
            DisplayName = "Standalone with a password",
            Category = TopologyCategory.SingleNode,
            Summary = "One Redis 8 node behind requirepass.",
            Detail = "A1 plus requirepass and masterauth, so the node can later be re-used as a " +
                "replica without surprises. The password is stored in a container label, which adds " +
                "no exposure - it is already visible in the command line through docker inspect - " +
                "and is what lets the tool show the connection string again after a restart.",
            Image = Redis8Image,
            ContainerCount = 1,
            DataPortCount = 1,
            ConnectionShape = ConnectionShape.Standalone,
            Parameters = [Password(required: true)],
            Highlights = ["requirepass"],
        },
        new()
        {
            Id = TopologyId.A3,
            Code = "A3",
            DisplayName = "Standalone with ACL users",
            Category = TopologyCategory.SingleNode,
            Summary = "One node with an admin password and extra ACL users.",
            Detail = "Every user line becomes its own --user argument on the redis-server command " +
                "line; repeated --user arguments append rather than overwrite, so no ACL file is " +
                "needed. The declared users are stored in a label and re-read on discovery, which " +
                "is how the instance card can still show them later.",
            Image = Redis8Image,
            ContainerCount = 1,
            DataPortCount = 1,
            ConnectionShape = ConnectionShape.Standalone,
            Parameters =
            [
                Password(required: true, "Admin password"),
                new TopologyParameter
                {
                    Key = "users",
                    Label = "ACL users",
                    Kind = TopologyParameterKind.MultiLineText,
                    DefaultValue = "app:{generated}:~app:* +@read +@write\n"
                        + "readonly:{generated}:~* +@read",
                    HelpText = "One user per line: name:password:permissions. "
                        + "Use {generated} for a random password.",
                    IsRequired = true,
                },
            ],
            Highlights = ["ACL", "multi-user"],
        },
        new()
        {
            Id = TopologyId.A5,
            Code = "A5",
            DisplayName = "RDB and AOF persistence",
            Category = TopologyCategory.SingleNode,
            Summary = "One node saving to disk both ways.",
            Detail = "Point-in-time snapshots and an append-only log at the same time, on the node's " +
                "own volume. The append directory appears under /data on the first write. Use it to " +
                "see what each mechanism costs and what survives a restart.",
            Image = Redis8Image,
            ContainerCount = 1,
            DataPortCount = 1,
            ConnectionShape = ConnectionShape.Standalone,
            Parameters =
            [
                new TopologyParameter
                {
                    Key = "savePolicy",
                    Label = "Save policy",
                    Kind = TopologyParameterKind.Text,
                    DefaultValue = "900 1 300 10 60 10000",
                    HelpText = "Seconds and change counts, in pairs. Blank switches snapshots off.",
                },
                new TopologyParameter
                {
                    Key = "appendfsync",
                    Label = "Append fsync",
                    Kind = TopologyParameterKind.Choice,
                    DefaultValue = "everysec",
                    Choices = ["everysec", "always", "no"],
                    HelpText = "How often the append-only log reaches the disk.",
                    IsRequired = true,
                },
                Password(required: false),
            ],
            Highlights = ["RDB", "AOF"],
        },
        new()
        {
            Id = TopologyId.A6,
            Code = "A6",
            DisplayName = "Memory cap and eviction",
            Category = TopologyCategory.SingleNode,
            Summary = "One node with maxmemory and an eviction policy.",
            Detail = "Redis's own memory ceiling, not the container's - that is G1. Fill it past the " +
                "cap and watch the policy choose what to drop. Every eviction policy Redis ships is " +
                "offered, including noeviction, which turns writes into errors instead.",
            Image = Redis8Image,
            ContainerCount = 1,
            DataPortCount = 1,
            ConnectionShape = ConnectionShape.Standalone,
            Parameters =
            [
                new TopologyParameter
                {
                    Key = "maxmemory",
                    Label = "Max memory",
                    Kind = TopologyParameterKind.Text,
                    DefaultValue = "64mb",
                    HelpText = "A Redis memory size, for example 64mb or 512mb.",
                    IsRequired = true,
                },
                new TopologyParameter
                {
                    Key = "policy",
                    Label = "Eviction policy",
                    Kind = TopologyParameterKind.Choice,
                    DefaultValue = "allkeys-lru",
                    Choices =
                    [
                        "allkeys-lru", "allkeys-lfu", "volatile-lru", "volatile-lfu",
                        "volatile-ttl", "volatile-random", "allkeys-random", "noeviction",
                    ],
                    HelpText = "What Redis drops when the cap is reached.",
                    IsRequired = true,
                },
                Password(required: false),
            ],
            Highlights = ["maxmemory", "eviction"],
        },
        new()
        {
            Id = TopologyId.B1,
            Code = "B1",
            DisplayName = "Primary and one replica",
            Category = TopologyCategory.Replication,
            Summary = "Two nodes, asynchronous replication.",
            Detail = "The replica follows the primary through the network gateway rather than by " +
                "container alias, so every address in the replication view is reachable from the " +
                "host too. Writes go to the primary; the replica answers reads. There is no " +
                "automatic failover here - that is C1.",
            Image = Redis8Image,
            ContainerCount = 2,
            DataPortCount = 2,
            ConnectionShape = ConnectionShape.PrimaryReplica,
            Parameters = [Password(required: true)],
            Highlights = ["replication", "read scale"],
        },
        new()
        {
            Id = TopologyId.C1,
            Code = "C1",
            DisplayName = "Sentinel high availability",
            Category = TopologyCategory.HighAvailability,
            Summary = "One primary, two replicas, three sentinels.",
            Detail = "The classic failover shape. The sentinels monitor the primary at the network " +
                "gateway address and the replicas announce themselves the same way, so every " +
                "address a sentinel hands a client is reachable from the host. Stop the primary and " +
                "watch a replica be promoted.",
            Image = Redis8Image,
            ContainerCount = 6,
            DataPortCount = 3,
            SentinelPortCount = 3,
            ConnectionShape = ConnectionShape.Sentinel,
            Parameters =
            [
                Password(required: true),
                new TopologyParameter
                {
                    Key = "serviceName",
                    Label = "Master name",
                    Kind = TopologyParameterKind.Text,
                    DefaultValue = "mymaster",
                    HelpText = "The name the sentinels know the primary by.",
                    IsRequired = true,
                },
            ],
            Highlights = ["failover", "sentinel", "quorum 2"],
        },
        new()
        {
            Id = TopologyId.D2,
            Code = "D2",
            DisplayName = "Cluster, three shards",
            Category = TopologyCategory.Cluster,
            Summary = "Three primaries and three replicas, 16384 slots.",
            Detail = "A real sharded cluster. Each node announces the network gateway address and " +
                "its own published ports, so a client on the host follows MOVED redirects correctly. " +
                "The cluster reports fail for a few seconds after creation and then ok - the " +
                "readiness wait polls rather than reading once.",
            Image = Redis8Image,
            ContainerCount = 6,
            DataPortCount = 6,
            NeedsBusPorts = true,
            ConnectionShape = ConnectionShape.Cluster,
            Parameters =
            [
                Password(required: false),
                new TopologyParameter
                {
                    Key = "nodeTimeoutMs",
                    Label = "Node timeout (ms)",
                    Kind = TopologyParameterKind.Integer,
                    DefaultValue = "5000",
                    Minimum = 1000,
                    Maximum = 60000,
                    HelpText = "How long a node may be unreachable before the cluster acts.",
                    IsRequired = true,
                },
            ],
            Highlights = ["sharding", "slots", "bus ports"],
        },
        new()
        {
            Id = TopologyId.E3,
            Code = "E3",
            DisplayName = "Redis 6.2",
            Category = TopologyCategory.VersionMatrix,
            Summary = "The compatibility floor, one node.",
            Detail = "A1's shape on Redis 6.2. No bundled modules, and the command set an older " +
                "client library expects. Use it to check that an application still works against " +
                "the oldest version worth supporting.",
            Image = Redis62Image,
            ContainerCount = 1,
            DataPortCount = 1,
            ConnectionShape = ConnectionShape.Standalone,
            Parameters = [Password(required: false)],
            Highlights = ["compatibility floor"],
        },
        new()
        {
            Id = TopologyId.E4,
            Code = "E4",
            DisplayName = "Valkey 8.1",
            Category = TopologyCategory.VersionMatrix,
            Summary = "The fork, one node.",
            Detail = "A1's shape on Valkey. The image ships both valkey-* and redis-* binaries, so " +
                "readiness checks, the shell probe and the console all work unchanged; only the " +
                "suggested command-line client differs.",
            Image = ValkeyImage,
            ContainerCount = 1,
            DataPortCount = 1,
            ConnectionShape = ConnectionShape.Standalone,
            Parameters = [Password(required: false)],
            Highlights = ["Valkey fork"],
        },
        new()
        {
            Id = TopologyId.F3,
            Code = "F3",
            DisplayName = "Redis 8 modules",
            Category = TopologyCategory.Features,
            Summary = "One node with the five bundled modules loaded.",
            Detail = "Redis 8 ships search, JSON, bloom, time series and vector sets, and its own " +
                "entrypoint is what loads them. This preset therefore passes no module flags at all " +
                "and, crucially, never overrides the entrypoint - doing so would silently drop four " +
                "of the five while the node still answered PING.",
            Image = Redis8Image,
            ContainerCount = 1,
            DataPortCount = 1,
            ConnectionShape = ConnectionShape.Standalone,
            Parameters = [Password(required: false)],
            Highlights = ["RediSearch", "RedisJSON", "RedisBloom", "RedisTimeSeries", "VectorSet"],
        },
        new()
        {
            Id = TopologyId.G1,
            Code = "G1",
            DisplayName = "Memory-capped container",
            Category = TopologyCategory.Operational,
            Summary = "One node inside a container with hard limits.",
            Detail = "A container memory limit with swap disabled, a CPU cap and a process limit, " +
                "with Redis's own maxmemory set below the container's. This is the preset that " +
                "makes the diagnostics and advisor tiers say something interesting: the limits " +
                "satisfy several advisor rules and leave the missing-healthcheck rule firing.",
            Image = Redis8Image,
            ContainerCount = 1,
            DataPortCount = 1,
            ConnectionShape = ConnectionShape.Standalone,
            Parameters =
            [
                new TopologyParameter
                {
                    Key = "containerMemoryMb",
                    Label = "Container memory (MB)",
                    Kind = TopologyParameterKind.Integer,
                    DefaultValue = "64",
                    Minimum = 16,
                    Maximum = 8192,
                    HelpText = "The container's hard memory limit. Swap is disabled by matching it.",
                    IsRequired = true,
                },
                new TopologyParameter
                {
                    Key = "maxmemoryMb",
                    Label = "Redis maxmemory (MB)",
                    Kind = TopologyParameterKind.Integer,
                    DefaultValue = "48",
                    Minimum = 8,
                    Maximum = 8192,
                    HelpText = "Keep this below the container limit so Redis evicts before the kernel kills.",
                    IsRequired = true,
                },
                new TopologyParameter
                {
                    Key = "policy",
                    Label = "Eviction policy",
                    Kind = TopologyParameterKind.Choice,
                    DefaultValue = "allkeys-lru",
                    Choices =
                    [
                        "allkeys-lru", "allkeys-lfu", "volatile-lru", "volatile-lfu",
                        "volatile-ttl", "volatile-random", "allkeys-random", "noeviction",
                    ],
                    HelpText = "What Redis drops when its own cap is reached.",
                    IsRequired = true,
                },
                new TopologyParameter
                {
                    Key = "cpus",
                    Label = "CPU cap",
                    Kind = TopologyParameterKind.Text,
                    DefaultValue = "0.5",
                    HelpText = "Cores, as a decimal. 0.5 means half a core.",
                    IsRequired = true,
                },
                new TopologyParameter
                {
                    Key = "pidsLimit",
                    Label = "Process limit",
                    Kind = TopologyParameterKind.Integer,
                    DefaultValue = "128",
                    Minimum = 16,
                    Maximum = 4096,
                    HelpText = "How many processes and threads the container may create.",
                    IsRequired = true,
                },
            ],
            Highlights = ["memory limit", "swap off", "CPU cap", "diagnostics"],
        },
        new()
        {
            Id = TopologyId.H1,
            Code = "H1",
            DisplayName = "Redlock quorum",
            Category = TopologyCategory.Locking,
            Summary = "Five independent masters, no replication.",
            Detail = "The shape the Redlock algorithm assumes: five masters that know nothing about " +
                "each other, each writing its append-only log on every command. A lock is held when " +
                "three of the five grant it. Composing this by hand from other presets is easy to " +
                "get subtly wrong, which is why it is a preset.",
            Image = Redis8Image,
            ContainerCount = 5,
            DataPortCount = 5,
            ConnectionShape = ConnectionShape.IndependentQuorum,
            Parameters = [Password(required: true)],
            Highlights = ["Redlock", "quorum 3 of 5", "appendfsync always"],
        },
    ];
}
