using System;
using System.Diagnostics;
using System.Text;

namespace RedisSetupTool.DockerManagement.Tests;

/// <summary>
/// Shells out to the Docker CLI to make scratch containers for the facade to be tested against.
/// The facade deliberately has no container-create operation - topologies create containers - so the
/// tests build their fixtures a different way rather than widening the public surface.
/// </summary>
public static class DockerCli
{
    /// <summary>Runs a Docker command and returns its standard output.</summary>
    /// <param name="arguments">The arguments, already split.</param>
    /// <returns>Standard output, trimmed.</returns>
    public static string Run(params string[] arguments)
    {
        var (exitCode, stdout, stderr) = TryRun(arguments);
        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                "docker " + string.Join(' ', arguments) + " failed with " + exitCode + ": " + stderr);
        }

        return stdout.Trim();
    }

    /// <summary>Runs a Docker command and reports the outcome without throwing.</summary>
    /// <param name="arguments">The arguments, already split.</param>
    /// <returns>The exit code and both streams.</returns>
    public static (int ExitCode, string Stdout, string Stderr) TryRun(params string[] arguments)
    {
        var info = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info);
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        stdout.Append(process.StandardOutput.ReadToEnd());
        stderr.Append(process.StandardError.ReadToEnd());
        process.WaitForExit(120000);
        return (process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    /// <summary>Removes a container, ignoring failures.</summary>
    /// <param name="name">The container name.</param>
    public static void RemoveQuietly(string name)
    {
        if (!string.IsNullOrEmpty(name))
        {
            TryRun("rm", "-f", name);
        }
    }
}
