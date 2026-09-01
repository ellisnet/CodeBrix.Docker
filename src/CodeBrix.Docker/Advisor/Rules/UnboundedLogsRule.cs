using System;
using System.Collections.Generic;

namespace CodeBrix.Docker;

/// <summary>
/// CB011 — the container logs to <c>json-file</c> without a size cap, so its log file grows until the
/// host's disk is full.
/// </summary>
internal sealed class UnboundedLogsRule : IAdvisorRule
{
    public string RuleId => "CB011";

    public AdvisorFinding Evaluate(AdvisorContext context)
    {
        var logConfig = context.HostConfig?.LogConfig;
        var driver = logConfig?.Type;
        if (!string.Equals(driver, "json-file", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (HasOption(logConfig?.Config, "max-size"))
        {
            return null;
        }

        return new AdvisorFinding(
            RuleId,
            AdvisorSeverity.Warning,
            context.ContainerName,
            "Log driver has no size limit",
            $"Container '{context.ContainerName}' uses the json-file log driver with no max-size option, so " +
            "everything it writes to stdout/stderr accumulates in a single file on the host until the disk fills " +
            "and every container on that host starts failing.",
            "Set ContainerSpec.LogOptions[\"max-size\"] (docker run --log-opt max-size=10m) together with " +
            "\"max-file\", for example 3, or switch ContainerSpec.LogDriver to \"local\", which rotates by default.");
    }

    private static bool HasOption(IDictionary<string, string> options, string key)
    {
        if (options is null)
        {
            return false;
        }

        foreach (var pair in options)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(pair.Value))
            {
                return true;
            }
        }

        return false;
    }
}
