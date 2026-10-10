using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Docker.Tests;

/// <summary>
/// Daemon-free tests for the CLI-backed build-cache prune on <see cref="ImageOperations"/>: the argument
/// list each option produces, and how the CLI's result becomes a <see cref="BuildCachePruneResult"/> or a
/// <see cref="DockerCliException"/>, driven through <see cref="DockerCliRunner"/>'s process-launch seam.
/// </summary>
/// <remarks>
/// No test here starts a docker process: an unfiltered <c>docker builder prune</c> would wipe the
/// engine-wide BuildKit cache that other projects on the developer's machine rely on. The one live call is
/// ImageTests.PruneBuildCacheAsync_WithAHugeAgeFilter_PrunesNothing, which is filtered to prune nothing.
/// </remarks>
public sealed class ImageOperationsTests
{
    // ---------------------------------------------------------------------------------------
    // Argument lists
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void BuildCachePruneArguments_WithoutOptions_IsTheForcedPruneOnly()
    {
        //Arrange

        //Act
        var args = ImageOperations.BuildCachePruneArguments(null, ImageOperations.KeepStorageFlag);

        //Assert
        args.Should().Equal("builder", "prune", "--force");
    }

    [Fact]
    public void BuildCachePruneArguments_WithDefaultOptions_IsTheForcedPruneOnly()
    {
        //Arrange
        var options = new BuildCachePruneOptions();

        //Act
        var args = ImageOperations.BuildCachePruneArguments(options, ImageOperations.KeepStorageFlag);

        //Assert
        args.Should().Equal("builder", "prune", "--force");
    }

    [Fact]
    public void BuildCachePruneArguments_WithAll_AddsTheAllFlag()
    {
        //Arrange
        var options = new BuildCachePruneOptions { All = true };

        //Act
        var args = ImageOperations.BuildCachePruneArguments(options, ImageOperations.KeepStorageFlag);

        //Assert
        args.Should().Equal("builder", "prune", "--force", "--all");
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void BuildCachePruneArguments_WithNoPositiveKeepStorage_AddsNothing(long keepStorageBytes)
    {
        //Arrange
        var options = new BuildCachePruneOptions { KeepStorageBytes = keepStorageBytes };

        //Act
        var args = ImageOperations.BuildCachePruneArguments(options, ImageOperations.KeepStorageFlag);

        //Assert
        args.Should().Equal("builder", "prune", "--force");
    }

    [Fact]
    public void BuildCachePruneArguments_WithKeepStorage_PassesTheByteCount()
    {
        //Arrange
        var options = new BuildCachePruneOptions { KeepStorageBytes = 5_000_000_000L };

        //Act
        var args = ImageOperations.BuildCachePruneArguments(options, ImageOperations.KeepStorageFlag);

        //Assert
        args.Should().Equal("builder", "prune", "--force", "--keep-storage", "5000000000");
    }

    [Theory]
    [InlineData(48.0, "until=48h")]
    [InlineData(1.5, "until=2h")]
    [InlineData(0.01, "until=1h")]
    [InlineData(0.0, "until=1h")]
    [InlineData(-3.0, "until=1h")]
    [InlineData(876000.0, "until=876000h")]
    public void BuildCachePruneArguments_WithOlderThan_FiltersOnWholeHoursRoundedUp(double hours,
        string expectedFilter)
    {
        //Arrange
        var options = new BuildCachePruneOptions { OlderThan = TimeSpan.FromHours(hours) };

        //Act
        var args = ImageOperations.BuildCachePruneArguments(options, ImageOperations.KeepStorageFlag);

        //Assert
        args.Should().Equal("builder", "prune", "--force", "--filter", expectedFilter);
    }

    [Fact]
    public void BuildCachePruneArguments_WithEveryOption_PassesThemAllInOrder()
    {
        //Arrange
        var options = new BuildCachePruneOptions
        {
            All = true,
            KeepStorageBytes = 1024L,
            OlderThan = TimeSpan.FromDays(2),
        };

        //Act
        var args = ImageOperations.BuildCachePruneArguments(options, ImageOperations.KeepStorageFlag);

        //Assert
        args.Should().Equal("builder", "prune", "--force", "--all", "--keep-storage", "1024", "--filter",
            "until=48h");
    }

    // ---------------------------------------------------------------------------------------
    // Running the prune
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task PruneBuildCacheAsync_WhenTheCliSucceeds_ReportsTheTotalAndTheOutput()
    {
        //Arrange
        var cli = new FakeCli(new CliResult(0, "abc123\ttrue\t14.56GB\nTotal:\t14.56GB\n",
            "Flag --keep-storage has been deprecated\n"));
        var images = cli.CreateImages();
        var options = new BuildCachePruneOptions { KeepStorageBytes = 10, OlderThan = TimeSpan.FromHours(1) };

        //Act
        var result = await images.PruneBuildCacheAsync(options, TestContext.Current.CancellationToken);

        //Assert
        cli.Arguments.Should().Equal("builder", "prune", "--force", "--keep-storage", "10", "--filter",
            "until=1h");
        result.ReclaimedSpaceText.Should().Be("14.56GB");
        result.ReclaimedBytes.Should().Be(14_560_000_000L);
        result.Output.Should().Contain("Total:\t14.56GB").And.Contain("deprecated");
    }

    [Fact]
    public async Task PruneBuildCacheAsync_WhenTheOutputHasNoTotalLine_ReturnsNullTextAndBytes()
    {
        //Arrange
        var cli = new FakeCli(new CliResult(0, "nothing recognisable\n", string.Empty));
        var images = cli.CreateImages();

        //Act
        var result = await images.PruneBuildCacheAsync(cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        cli.Arguments.Should().Equal("builder", "prune", "--force");
        result.ReclaimedSpaceText.Should().BeNull();
        result.ReclaimedBytes.Should().BeNull();
        result.Output.Should().Contain("nothing recognisable");
    }

    [Fact]
    public async Task PruneBuildCacheAsync_WhenTheCliExitsNonZero_ThrowsDockerCliException()
    {
        //Arrange
        var cli = new FakeCli(new CliResult(1, string.Empty, "ERROR: no builder \"default\" found"));
        var images = cli.CreateImages();

        //Act
        Func<Task> prune = () => images.PruneBuildCacheAsync(new BuildCachePruneOptions { All = true },
            TestContext.Current.CancellationToken);

        //Assert
        var thrown = await prune.Should().ThrowAsync<DockerCliException>();
        thrown.Which.ExitCode.Should().Be(1);
        thrown.Which.StdErr.Should().Contain("no builder");
        thrown.Which.Command.Should().Be("docker builder prune --force --all");
    }

    [Fact]
    public async Task PruneBuildCacheAsync_WhenCancelled_PropagatesTheCancellation()
    {
        //Arrange
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var cli = new FakeCli(new CliResult(0, "Total:\t0B\n", string.Empty));
        var images = cli.CreateImages();

        //Act
        Func<Task> prune = () => images.PruneBuildCacheAsync(cancellationToken: cancellation.Token);

        //Assert
        await prune.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void BuildCachePruneArguments_WithKeepStorageAndTheReservedSpaceFlag_PassesThatFlag()
    {
        //Arrange
        var options = new BuildCachePruneOptions { KeepStorageBytes = 5_000_000_000L };

        //Act
        var args = ImageOperations.BuildCachePruneArguments(options, ImageOperations.ReservedSpaceFlag);

        //Assert
        args.Should().Equal("builder", "prune", "--force", "--reserved-space", "5000000000");
    }

    [Theory]
    [InlineData(null, "--keep-storage")]
    [InlineData("0.18.9", "--keep-storage")]
    [InlineData("0.19.0", "--reserved-space")]
    [InlineData("0.37.1", "--reserved-space")]
    [InlineData("1.0.0", "--reserved-space")]
    public void StorageFlagFor_FollowsTheBuildxVersion(string buildxVersion, string expectedFlag)
    {
        //Arrange
        var version = buildxVersion is null ? null : Version.Parse(buildxVersion);

        //Act
        var flag = ImageOperations.StorageFlagFor(version);

        //Assert
        flag.Should().Be(expectedFlag);
    }

    [Theory]
    [InlineData("github.com/docker/buildx v0.37.1 0b265a9f62db554fa9aba6dd19e1bd5704bc7d8a\n", "0.37.1")]
    [InlineData("github.com/docker/buildx v0.19.0-rc1 abcdef\n", "0.19.0")]
    [InlineData("buildx 0.18.2", "0.18.2")]
    [InlineData("", null)]
    [InlineData("no version here", null)]
    [InlineData("docker: 'buildx' is not a docker command.", null)]
    public void ParseBuildxVersion_ReadsTheVersionOutOfTheCliLine(string output, string expected)
    {
        //Act
        var version = ImageOperations.ParseBuildxVersion(output);

        //Assert
        (version?.ToString()).Should().Be(expected);
    }

    [Fact]
    public async Task PruneBuildCacheAsync_WithKeepStorageOnBuildx019OrLater_UsesReservedSpace()
    {
        //Arrange
        var cli = new FakeCli(args => args[0] == "buildx"
            ? new CliResult(0, "github.com/docker/buildx v0.37.1 0b265a9f62db554fa9aba6dd19e1bd5704bc7d8a\n", string.Empty)
            : new CliResult(0, "Total:\t1.5GB\n", string.Empty));
        var images = cli.CreateImages();

        //Act
        var result = await images.PruneBuildCacheAsync(new BuildCachePruneOptions { KeepStorageBytes = 2_000_000_000L },
            TestContext.Current.CancellationToken);

        //Assert
        cli.Calls.Should().HaveCount(2);
        cli.Calls[0].Should().Equal("buildx", "version");
        cli.Calls[1].Should().Equal("builder", "prune", "--force", "--reserved-space", "2000000000");
        result.StorageFlag.Should().Be("--reserved-space");
        result.ReclaimedBytes.Should().Be(1_500_000_000L);
    }

    [Fact]
    public async Task PruneBuildCacheAsync_WithKeepStorageOnAnOlderBuildx_UsesKeepStorage()
    {
        //Arrange
        var cli = new FakeCli(args => args[0] == "buildx"
            ? new CliResult(0, "github.com/docker/buildx v0.18.0 1234567\n", string.Empty)
            : new CliResult(0, "Total:\t0B\n", string.Empty));
        var images = cli.CreateImages();

        //Act
        var result = await images.PruneBuildCacheAsync(new BuildCachePruneOptions { KeepStorageBytes = 1 },
            TestContext.Current.CancellationToken);

        //Assert
        cli.Calls[1].Should().Equal("builder", "prune", "--force", "--keep-storage", "1");
        result.StorageFlag.Should().Be("--keep-storage");
    }

    [Fact]
    public async Task PruneBuildCacheAsync_WithKeepStorageAndNoBuildxPlugin_UsesKeepStorage()
    {
        //Arrange
        var cli = new FakeCli(args => args[0] == "buildx"
            ? new CliResult(1, string.Empty, "docker: 'buildx' is not a docker command.\n")
            : new CliResult(0, "Total:\t0B\n", string.Empty));
        var images = cli.CreateImages();

        //Act
        var result = await images.PruneBuildCacheAsync(new BuildCachePruneOptions { KeepStorageBytes = 1 },
            TestContext.Current.CancellationToken);

        //Assert
        cli.Calls[1].Should().Equal("builder", "prune", "--force", "--keep-storage", "1");
        result.StorageFlag.Should().Be("--keep-storage");
    }

    [Fact]
    public async Task PruneBuildCacheAsync_ProbesTheBuildxVersionOncePerInstance()
    {
        //Arrange
        var cli = new FakeCli(args => args[0] == "buildx"
            ? new CliResult(0, "github.com/docker/buildx v0.37.1 abc\n", string.Empty)
            : new CliResult(0, "Total:\t0B\n", string.Empty));
        var images = cli.CreateImages();
        var options = new BuildCachePruneOptions { KeepStorageBytes = 1 };

        //Act
        await images.PruneBuildCacheAsync(options, TestContext.Current.CancellationToken);
        await images.PruneBuildCacheAsync(options, TestContext.Current.CancellationToken);

        //Assert
        cli.Calls.Count(c => c[0] == "buildx").Should().Be(1);
        cli.Calls.Count(c => c[0] == "builder").Should().Be(2);
    }

    [Fact]
    public async Task PruneBuildCacheAsync_WithoutKeepStorage_NeverProbesTheBuildxVersion()
    {
        //Arrange
        var cli = new FakeCli(new CliResult(0, "Total:\t0B\n", string.Empty));
        var images = cli.CreateImages();

        //Act
        var result = await images.PruneBuildCacheAsync(new BuildCachePruneOptions { All = true },
            TestContext.Current.CancellationToken);

        //Assert
        cli.Calls.Should().HaveCount(1);
        cli.Calls[0].Should().Equal("builder", "prune", "--force", "--all");
        result.StorageFlag.Should().BeNull();
    }

    // ---------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Stands in for the docker process: records the argument list and returns one fixed result, after
    /// reporting its lines to the output receiver the way the real runner does. A cancelled token is
    /// honoured the way the real process wait honours it.
    /// </summary>
    private sealed class FakeCli(Func<IReadOnlyList<string>, CliResult> respond)
    {
        public FakeCli(CliResult result)
            : this(_ => result)
        {
        }

        /// <summary>Gets the argument list of the last attempt.</summary>
        public IReadOnlyList<string> Arguments { get; private set; } = [];

        /// <summary>Gets the argument lists of every attempt, in order.</summary>
        public List<IReadOnlyList<string>> Calls { get; } = [];

        /// <summary>Creates an operation group whose CLI attempts are this fake. The API client is unused.</summary>
        public ImageOperations CreateImages() =>
            new(api: null, new DockerCliRunner(new DockerClientOptions(), RunAttemptAsync, retryDelays: []));

        private Task<CliResult> RunAttemptAsync(IReadOnlyList<string> args, string workingDir,
            IProgress<string> output, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Arguments = args.ToArray();
            Calls.Add(Arguments);
            var result = respond(args);

            foreach (var line in Lines(result.Stdout).Concat(Lines(result.Stderr)))
            {
                output?.Report(line);
            }

            return Task.FromResult(result);
        }

        private static IEnumerable<string> Lines(string text) =>
            string.IsNullOrEmpty(text) ? [] : text.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');
    }
}
