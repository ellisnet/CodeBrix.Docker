using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Docker;

namespace RedisSetupTool.DockerManagement.Topologies.Builders;

/// <summary>
/// Every single-container topology. The command line is the only thing that varies, plus a readiness
/// check that proves the point of the preset rather than merely proving the node answers.
/// </summary>
internal sealed class StandaloneTopologyBuilder : ITopologyBuilder
{
    /// <summary>The five modules Redis 8 loads through its own entrypoint.</summary>
    internal static readonly string[] ExpectedModules =
        ["search", "bf", "vectorset", "timeseries", "ReJSON"];

    /// <inheritdoc />
    public IReadOnlyList<TopologyId> Supported =>
    [
        TopologyId.A1, TopologyId.A2, TopologyId.A3, TopologyId.A5, TopologyId.A6,
        TopologyId.E3, TopologyId.E4, TopologyId.F3, TopologyId.G1,
    ];

    /// <inheritdoc />
    public int StepCount(TopologyDescriptor descriptor) => 4;

    /// <inheritdoc />
    public async Task<TopologyBuildResult> BuildAsync(TopologyBuildContext context,
        CancellationToken cancellationToken)
    {
        await context.CreateNetworkAsync(cancellationToken).ConfigureAwait(false);

        var node = await context.StartNodeAsync(new NodePlan
        {
            RoleName = "primary",
            RoleLabel = "primary",
            Role = NodeRole.Primary,
            NodeIndex = 1,
            ContainerPort = 6379,
            HostPort = context.Ports.DataPorts[0],
            Command = BuildCommand(context),
            Limits = BuildLimits(context),
        }, cancellationToken).ConfigureAwait(false);

        await context.WaitForPongAsync(node, TimeSpan.FromSeconds(30), cancellationToken)
            .ConfigureAwait(false);
        await VerifyAsync(context, node, cancellationToken).ConfigureAwait(false);

        var endpoint = new RedisEndpoint
        {
            Host = "127.0.0.1",
            Port = node.HostPort,
            Role = NodeRole.Primary,
            NodeIndex = 1,
        };

        var users = ParseUsers(context.Parameter("users"));
        var cli = context.Descriptor.Id == TopologyId.E4
            ? ConnectionInfoFactory.ValkeyCli
            : ConnectionInfoFactory.RedisCli;

        return new TopologyBuildResult
        {
            Nodes = [node],
            Connection = ConnectionInfoFactory.Build(ConnectionShape.Standalone, [endpoint],
                context.Password, users: users, notes: BuildNotes(context), cliExecutable: cli),
        };
    }

    /// <summary>Builds the redis-server arguments for a single-node topology.</summary>
    /// <param name="context">The build context.</param>
    /// <returns>The command, whose first element always starts with a dash so the image's own
    /// entrypoint prepends <c>redis-server</c> and keeps loading its bundled modules.</returns>
    internal static IReadOnlyList<string> BuildCommand(TopologyBuildContext context)
    {
        var command = new List<string> { "--port", "6379" };
        var password = context.Password;

        switch (context.Descriptor.Id)
        {
            case TopologyId.A3:
                Authenticate(command, password);
                foreach (var user in ParseUsers(context.Parameter("users")))
                {
                    command.Add("--user");
                    command.Add(user.Username);
                    command.Add("on");
                    command.Add(">" + user.Password);
                    foreach (var token in user.Permissions.Split(' ',
                                 StringSplitOptions.RemoveEmptyEntries))
                    {
                        command.Add(token);
                    }
                }

                break;

            case TopologyId.A5:
                command.Add("--dir");
                command.Add("/data");
                command.Add("--save");
                command.Add(context.Parameter("savePolicy"));
                command.Add("--appendonly");
                command.Add("yes");
                command.Add("--appendfsync");
                command.Add(context.Parameter("appendfsync"));
                Authenticate(command, password);
                break;

            case TopologyId.A6:
                command.Add("--maxmemory");
                command.Add(context.Parameter("maxmemory"));
                command.Add("--maxmemory-policy");
                command.Add(context.Parameter("policy"));
                NoPersistence(command);
                Authenticate(command, password);
                break;

            case TopologyId.G1:
                command.Add("--maxmemory");
                command.Add(context.ParameterInt("maxmemoryMb", 48)
                    .ToString(CultureInfo.InvariantCulture) + "mb");
                command.Add("--maxmemory-policy");
                command.Add(context.Parameter("policy"));
                NoPersistence(command);
                Authenticate(command, password);
                break;

            case TopologyId.F3:
                //Nothing about modules: the image's entrypoint loads every module in
                //  /usr/local/lib/redis/modules, and only when the resolved command is redis-server.
                Authenticate(command, password);
                break;

            default:
                NoPersistence(command);
                Authenticate(command, password);
                break;
        }

        return command;
    }

    /// <summary>Parses the multi-line ACL user parameter.</summary>
    /// <param name="text">The raw parameter value.</param>
    /// <returns>The users; empty when the parameter is blank.</returns>
    internal static IReadOnlyList<RedisUser> ParseUsers(string text)
    {
        var users = new List<RedisUser>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return users;
        }

        foreach (var line in text.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (TryParseUser(line, out var user))
            {
                users.Add(user);
            }
        }

        return users;
    }

    /// <summary>Parses one <c>name:password:permissions</c> user line.</summary>
    /// <param name="line">The line.</param>
    /// <param name="user">The parsed user.</param>
    /// <returns>True when the line has all three parts.</returns>
    internal static bool TryParseUser(string line, out RedisUser user)
    {
        user = null;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var parts = line.Trim().Split(':', 3);
        if (parts.Length != 3 || parts[0].Length == 0 || parts[1].Length == 0
            || parts[2].Trim().Length == 0)
        {
            return false;
        }

        user = new RedisUser
        {
            Username = parts[0].Trim(),
            Password = parts[1].Trim(),
            Permissions = parts[2].Trim(),
        };

        return true;
    }

    private static ResourceLimits BuildLimits(TopologyBuildContext context)
    {
        if (context.Descriptor.Id != TopologyId.G1)
        {
            return null;
        }

        var memoryMb = context.ParameterInt("containerMemoryMb", 64);
        var cpuText = context.Parameter("cpus");
        double? cpus = double.TryParse(cpuText, NumberStyles.Float, CultureInfo.InvariantCulture,
            out var parsed) ? parsed : null;

        return new ResourceLimits
        {
            //Equal memory and swap limits are what actually disables swap.
            MemoryBytes = ResourceLimits.Megabytes(memoryMb),
            MemorySwapBytes = ResourceLimits.Megabytes(memoryMb),
            MemoryReservationBytes = ResourceLimits.Megabytes(Math.Max(1, memoryMb / 2)),
            Cpus = cpus,
            PidsLimit = context.ParameterInt("pidsLimit", 128),
        };
    }

    private static async Task VerifyAsync(TopologyBuildContext context, TopologyNode node,
        CancellationToken cancellationToken)
    {
        switch (context.Descriptor.Id)
        {
            case TopologyId.A3:
                await context.WaitAsync("the ACL users to exist", TimeSpan.FromSeconds(20), async () =>
                {
                    var acl = await context.ExecAsync(node.ContainerName,
                        context.RedisCli(node.ContainerPort, "ACL", "LIST"), cancellationToken)
                        .ConfigureAwait(false);
                    foreach (var user in ParseUsers(context.Parameter("users")))
                    {
                        if (!acl.Stdout.Contains("user " + user.Username, StringComparison.Ordinal))
                        {
                            return false;
                        }
                    }

                    return acl.Succeeded;
                }, cancellationToken).ConfigureAwait(false);
                break;

            case TopologyId.A5:
                await ExpectConfigAsync(context, node, "appendonly", "yes", cancellationToken)
                    .ConfigureAwait(false);
                break;

            case TopologyId.A6:
                await ExpectConfigAsync(context, node, "maxmemory-policy",
                    context.Parameter("policy"), cancellationToken).ConfigureAwait(false);
                break;

            case TopologyId.F3:
                await context.WaitAsync("the bundled modules to load", TimeSpan.FromSeconds(30),
                    async () =>
                    {
                        var modules = await context.ExecAsync(node.ContainerName,
                            context.RedisCli(node.ContainerPort, "MODULE", "LIST"), cancellationToken)
                            .ConfigureAwait(false);
                        foreach (var module in ExpectedModules)
                        {
                            if (!modules.Stdout.Contains(module, StringComparison.OrdinalIgnoreCase))
                            {
                                return false;
                            }
                        }

                        return true;
                    }, cancellationToken).ConfigureAwait(false);
                break;

            default:
                break;
        }
    }

    private static Task ExpectConfigAsync(TopologyBuildContext context, TopologyNode node,
        string setting, string expected, CancellationToken cancellationToken) =>
        context.WaitAsync($"{setting} to be {expected}", TimeSpan.FromSeconds(20), async () =>
        {
            var result = await context.ExecAsync(node.ContainerName,
                context.RedisCli(node.ContainerPort, "CONFIG", "GET", setting), cancellationToken)
                .ConfigureAwait(false);
            return result.Succeeded
                && result.Stdout.Contains(expected, StringComparison.OrdinalIgnoreCase);
        }, cancellationToken);

    private static IReadOnlyList<string> BuildNotes(TopologyBuildContext context) =>
        context.Descriptor.Id switch
        {
            TopologyId.F3 => ["Modules are loaded by the image entrypoint: "
                + string.Join(", ", ExpectedModules)],
            TopologyId.G1 => ["The container memory limit is hard and swap is disabled, "
                + "so Redis must evict before the kernel intervenes."],
            TopologyId.E4 => ["Valkey ships both valkey-* and redis-* binaries."],
            _ => [],
        };

    private static void NoPersistence(List<string> command)
    {
        command.Add("--save");
        command.Add(string.Empty);
        command.Add("--appendonly");
        command.Add("no");
    }

    private static void Authenticate(List<string> command, string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return;
        }

        command.Add("--requirepass");
        command.Add(password);
        command.Add("--masterauth");
        command.Add(password);
    }
}
