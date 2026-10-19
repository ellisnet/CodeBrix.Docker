using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using Xunit;

namespace CodeBrix.Docker.Tests;

/// <summary>
/// A fact for the foreign-platform tests: skipped, with a reason naming what is missing, when no Docker
/// engine answers or when the host has no qemu-user binfmt handler for aarch64
/// (<c>/proc/sys/fs/binfmt_misc/qemu-aarch64</c>).
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class ForeignPlatformFactAttribute : FactAttribute
{
    /// <summary>The binfmt handler an arm64 container needs on an x86-64 Linux host.</summary>
    public const string Aarch64BinfmtHandler = "/proc/sys/fs/binfmt_misc/qemu-aarch64";

    private static readonly Lazy<string> SkipReason = new(FindSkipReason, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Creates the attribute, setting <see cref="FactAttribute.Skip"/> when a prerequisite is missing.</summary>
    /// <param name="sourceFilePath"></param>
    /// <param name="sourceLineNumber"></param>
    public ForeignPlatformFactAttribute(
        [CallerFilePath] string sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (SkipReason.Value is { } reason)
        {
            Skip = reason;
        }
    }

    private static string FindSkipReason()
    {
        try
        {
            using var client = DockerClient.Create();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            if (!client.System.PingAsync(cancellation.Token).GetAwaiter().GetResult())
            {
                return $"No Docker engine answered at {client.Endpoint}.";
            }
        }
        catch (Exception ex)
        {
            return $"No Docker engine is reachable ({ex.GetType().Name}: {ex.Message}).";
        }

        if (!File.Exists(Aarch64BinfmtHandler))
        {
            return $"{Aarch64BinfmtHandler} is absent: the host has no qemu-user binfmt handler for aarch64.";
        }

        return null;
    }
}
