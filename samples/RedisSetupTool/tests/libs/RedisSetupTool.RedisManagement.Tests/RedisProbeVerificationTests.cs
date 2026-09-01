using System.Threading.Tasks;
using RedisSetupTool.RedisManagement.Tests.Fakes;
using SilverAssertions;
using Xunit;

namespace RedisSetupTool.RedisManagement.Tests;

/// <summary>Covers the per-shape verification logic against canned replies.</summary>
public class RedisProbeVerificationTests
{
    /// <summary>A standalone node proves its role, its configuration and its modules.</summary>
    [Fact]
    public async Task VerifyAsync_ForStandalone_ChecksRoleConfigUsersAndModules()
    {
        //Arrange
        var node = new FakeRedisServer("127.0.0.1:6401");
        node.Replies["INFO"] = "# Server\nredis_version:8.10.1\nredis_mode:standalone\n# Replication\nrole:master\n";
        node.Replies["CONFIG GET appendonly"] = "appendonly\nyes";
        node.Replies["ACL LIST"] = "user default on nopass ~* &* +@all\nuser app on #abc ~app:* +@read";
        node.Replies["MODULE LIST"] = "name\nsearch\nver\n21005\nname\nReJSON\nver\n20805";
        var factory = new FakeRedisConnectionFactory().With("127.0.0.1:6401", node);
        var probe = new RedisProbe(factory);

        var descriptor = new RedisConnectionDescriptor
        {
            Shape = RedisConnectionShape.Standalone,
            Endpoints = [new RedisHostPort { Host = "127.0.0.1", Port = 6401 }],
            ExpectedConfig = new System.Collections.Generic.Dictionary<string, string>
            {
                ["appendonly"] = "yes",
            },
            ExpectedUsers = ["app"],
            ExpectedModules = ["search", "ReJSON"],
            ExpectedVersionPrefix = "8.",
        };

        //Act
        var verification = await probe.VerifyAsync(descriptor, TestContext.Current.CancellationToken);

        //Assert
        verification.Succeeded.Should().Be(true);
        verification.Checks.Count.Should().Be(9);
        verification.Summary.Should().Contain("All 9 checks passed");
    }

    /// <summary>A missing module fails exactly one check.</summary>
    [Fact]
    public async Task VerifyAsync_WhenAModuleIsMissing_FailsExactlyOneCheck()
    {
        //Arrange
        var node = new FakeRedisServer("127.0.0.1:6401");
        node.Replies["INFO"] = "redis_version:8.10.1\nrole:master\n";
        node.Replies["MODULE LIST"] = "name\nsearch\nver\n21005";
        var factory = new FakeRedisConnectionFactory().With("127.0.0.1:6401", node);
        var probe = new RedisProbe(factory);

        var descriptor = new RedisConnectionDescriptor
        {
            Shape = RedisConnectionShape.Standalone,
            Endpoints = [new RedisHostPort { Host = "127.0.0.1", Port = 6401 }],
            ExpectedModules = ["search", "ReJSON"],
        };

        //Act
        var verification = await probe.VerifyAsync(descriptor, TestContext.Current.CancellationToken);

        //Assert
        verification.Succeeded.Should().Be(false);
        verification.Summary.Should().Contain("module ReJSON loaded");
    }

    /// <summary>Replication is proved from both ends plus a real write.</summary>
    [Fact]
    public async Task VerifyAsync_ForPrimaryReplica_ChecksBothEndsAndAWrite()
    {
        //Arrange
        var primary = new FakeRedisServer("127.0.0.1:6401");
        primary.Replies["INFO replication"] =
            "role:master\nconnected_slaves:1\nslave0:ip=172.18.0.1,port=6402,state=online,offset=99,lag=0\n";
        var replica = new FakeRedisServer("127.0.0.1:6402");
        replica.Replies["INFO replication"] =
            "role:slave\nmaster_host:172.18.0.1\nmaster_port:6401\nmaster_link_status:up\n";

        var factory = new FakeRedisConnectionFactory()
            .With("127.0.0.1:6401", primary)
            .With("127.0.0.1:6402", replica);

        //The fakes do not replicate, so mirror the write the probe makes.
        var probe = new RedisProbe(new MirroringFactory(factory, primary, replica));

        var descriptor = new RedisConnectionDescriptor
        {
            Shape = RedisConnectionShape.PrimaryReplica,
            Endpoints =
            [
                new RedisHostPort { Host = "127.0.0.1", Port = 6401 },
                new RedisHostPort { Host = "127.0.0.1", Port = 6402 },
            ],
        };

        //Act
        var verification = await probe.VerifyAsync(descriptor, TestContext.Current.CancellationToken);

        //Assert
        verification.Succeeded.Should().Be(true);
        verification.Checks.Count.Should().Be(5);
    }

    /// <summary>A cluster in the fail state fails the state check and says so.</summary>
    [Fact]
    public async Task VerifyAsync_WhenTheClusterIsNotOk_FailsTheStateCheck()
    {
        //Arrange
        var node = new FakeRedisServer("172.18.0.1:7401");
        node.Replies["CLUSTER INFO"] =
            "cluster_state:fail\ncluster_slots_assigned:16384\ncluster_slots_ok:16384\n"
            + "cluster_known_nodes:2\ncluster_size:1\n";
        node.Replies["CLUSTER NODES"] =
            "aaa 172.18.0.1:7401@17401 myself,master - 0 0 1 connected 0-16383\n"
            + "bbb 172.18.0.1:7402@17402 slave aaa 0 0 2 connected\n";

        var factory = new FakeRedisConnectionFactory { Default = node };
        var probe = new RedisProbe(factory);

        var descriptor = new RedisConnectionDescriptor
        {
            Shape = RedisConnectionShape.Cluster,
            Endpoints =
            [
                new RedisHostPort { Host = "172.18.0.1", Port = 7401 },
                new RedisHostPort { Host = "172.18.0.1", Port = 7402 },
            ],
        };

        //Act
        var verification = await probe.VerifyAsync(descriptor, TestContext.Current.CancellationToken);

        //Assert
        verification.Succeeded.Should().Be(false);
        verification.Summary.Should().Contain("cluster_state is ok");
    }

    /// <summary>Every sentinel is asked, and they must agree.</summary>
    [Fact]
    public async Task VerifyAsync_ForSentinel_AsksEverySentinel()
    {
        //Arrange
        var sentinel = new FakeRedisServer("sentinel");
        sentinel.Replies["SENTINEL get-master-addr-by-name mymaster"] = "172.18.0.1\n6401";
        sentinel.Replies["SENTINEL sentinels mymaster"] = "runid\na\nrunid\nb";
        sentinel.Replies["SENTINEL replicas mymaster"] = "runid\nc\nrunid\nd";

        var master = new FakeRedisServer("master");
        var factory = new FakeRedisConnectionFactory { Default = master, SentinelSelector = _ => sentinel };
        var probe = new RedisProbe(factory);

        var descriptor = new RedisConnectionDescriptor
        {
            Shape = RedisConnectionShape.Sentinel,
            ServiceName = "mymaster",
            Endpoints =
            [
                new RedisHostPort { Host = "127.0.0.1", Port = 26401, IsSentinel = true },
                new RedisHostPort { Host = "127.0.0.1", Port = 26402, IsSentinel = true },
                new RedisHostPort { Host = "127.0.0.1", Port = 26403, IsSentinel = true },
            ],
        };

        //Act
        var verification = await probe.VerifyAsync(descriptor, TestContext.Current.CancellationToken);

        //Assert
        verification.Succeeded.Should().Be(true);
        //Four checks per sentinel, plus the master's own ping.
        verification.Checks.Count.Should().Be(13);
    }

    /// <summary>Each master must be independent and durable.</summary>
    [Fact]
    public async Task VerifyAsync_ForQuorum_ChecksIndependenceAndDurability()
    {
        //Arrange
        var factory = new FakeRedisConnectionFactory();
        var endpoints = new System.Collections.Generic.List<RedisHostPort>();
        for (var index = 0; index < 5; index++)
        {
            var endpoint = new RedisHostPort { Host = "127.0.0.1", Port = 6401 + index };
            var node = new FakeRedisServer(endpoint.ToString());
            node.Replies["CONFIG GET appendfsync"] = "appendfsync\nalways";
            node.Replies["INFO replication"] = "role:master\nconnected_slaves:0\n";
            factory.With(endpoint.ToString(), node);
            endpoints.Add(endpoint);
        }

        var probe = new RedisProbe(factory);
        var descriptor = new RedisConnectionDescriptor
        {
            Shape = RedisConnectionShape.IndependentQuorum,
            Endpoints = endpoints,
        };

        //Act
        var verification = await probe.VerifyAsync(descriptor, TestContext.Current.CancellationToken);

        //Assert
        verification.Succeeded.Should().Be(true);
        verification.Checks.Count.Should().Be(15);
    }

    /// <summary>A factory that copies a write on the primary onto the replica.</summary>
    private sealed class MirroringFactory : IRedisConnectionFactory
    {
        private readonly FakeRedisConnectionFactory _inner;
        private readonly FakeRedisServer _primary;
        private readonly FakeRedisServer _replica;

        internal MirroringFactory(FakeRedisConnectionFactory inner, FakeRedisServer primary,
            FakeRedisServer replica)
        {
            _inner = inner;
            _primary = primary;
            _replica = replica;
        }

        public Task<IRedisConnection> ConnectAsync(RedisConnectionDescriptor descriptor,
            System.Threading.CancellationToken cancellationToken = default) =>
            _inner.ConnectAsync(descriptor, cancellationToken);

        public async Task<IRedisConnection> ConnectSingleAsync(RedisHostPort endpoint,
            RedisCredentials credentials,
            System.Threading.CancellationToken cancellationToken = default)
        {
            var connection = await _inner.ConnectSingleAsync(endpoint, credentials, cancellationToken);
            return ReferenceEquals(connection, _primary)
                ? new MirroringConnection(_primary, _replica)
                : connection;
        }

        public Task<IRedisConnection> ConnectSentinelsAsync(RedisConnectionDescriptor descriptor,
            System.Threading.CancellationToken cancellationToken = default) =>
            _inner.ConnectSentinelsAsync(descriptor, cancellationToken);
    }

    /// <summary>Forwards to the primary and mirrors writes onto the replica.</summary>
    private sealed class MirroringConnection : IRedisConnection
    {
        private readonly FakeRedisServer _primary;
        private readonly FakeRedisServer _replica;

        internal MirroringConnection(FakeRedisServer primary, FakeRedisServer replica)
        {
            _primary = primary;
            _replica = replica;
        }

        public string Description => _primary.Description;

        public Task<System.TimeSpan> PingAsync(
            System.Threading.CancellationToken cancellationToken = default) =>
            _primary.PingAsync(cancellationToken);

        public Task<string> ExecuteAsync(string command, params object[] args) =>
            _primary.ExecuteAsync(command, args);

        public async Task<bool> StringSetAsync(string key, string value,
            System.TimeSpan? expiry = null, bool onlyIfAbsent = false)
        {
            var stored = await _primary.StringSetAsync(key, value, expiry, onlyIfAbsent);
            await _replica.StringSetAsync(key, value, expiry);
            return stored;
        }

        public Task<string> StringGetAsync(string key) => _primary.StringGetAsync(key);

        public async Task<bool> KeyDeleteAsync(string key)
        {
            await _replica.KeyDeleteAsync(key);
            return await _primary.KeyDeleteAsync(key);
        }

        public Task<long> ScriptEvaluateLongAsync(string script, string[] keys, object[] values) =>
            _primary.ScriptEvaluateLongAsync(script, keys, values);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
