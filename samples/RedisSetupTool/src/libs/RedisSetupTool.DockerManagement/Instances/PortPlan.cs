using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RedisSetupTool.DockerManagement.Instances;

/// <summary>The host ports one instance will publish.</summary>
public sealed class PortPlan
{
    /// <summary>Gets the data ports, one per data node; never null.</summary>
    public IReadOnlyList<int> DataPorts { get; init; } = [];

    /// <summary>Gets the sentinel ports; empty unless the topology runs sentinels.</summary>
    public IReadOnlyList<int> SentinelPorts { get; init; } = [];

    /// <summary>Gets the cluster-bus ports, parallel to <see cref="DataPorts"/>; empty unless D2.</summary>
    public IReadOnlyList<int> BusPorts { get; init; } = [];

    /// <summary>Renders the plan as one line, for the create form.</summary>
    /// <returns>A human-readable description.</returns>
    public string Describe()
    {
        var text = new StringBuilder();
        Append(text, DataPorts, "data");
        Append(text, SentinelPorts, "sentinel");
        Append(text, BusPorts, "bus");
        return text.Length == 0 ? "no ports" : text.ToString();
    }

    private static void Append(StringBuilder text, IReadOnlyList<int> ports, string label)
    {
        if (ports is null || ports.Count == 0)
        {
            return;
        }

        if (text.Length > 0)
        {
            text.Append(" - ");
        }

        text.Append(label).Append(' ');
        for (var index = 0; index < ports.Count; index++)
        {
            if (index > 0)
            {
                text.Append(", ");
            }

            text.Append(ports[index].ToString(CultureInfo.InvariantCulture));
        }
    }
}
