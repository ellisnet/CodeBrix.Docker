using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Docker.Tests;

/// <summary>
/// Daemon-free tests for <see cref="DockerCliRunner"/>: the predicate that recognises the docker CLI failing
/// during its own start-up because another process had one of its files open, and the retry loop built on
/// it, driven through the runner's process-launch seam.
/// </summary>
/// <remarks>
/// The failure being retried was observed on Windows 11 with Docker Desktop 29.8.2, while a test suite ran
/// many docker CLI processes in parallel. It cannot be provoked on demand, and never on Linux or macOS, so
/// the loop is exercised with a scripted attempt instead of a real process. The predicate matches message
/// text only, which is why these tests run unchanged on every operating system.
/// </remarks>
public sealed class DockerCliRunnerTests
{
    /// <summary>The standard error of the failed <c>docker build</c> exactly as it was observed.</summary>
    private const string ObservedSharingViolation =
        "Failed to initialize: unable to resolve docker endpoint: context \"desktop-linux\": open " +
        @"C:\Users\jerem\.docker\contexts\meta\" +
        @"fe9c6bd7a66301f49ca9b6a70b217107cd1284598bfc254700c989b916da791e\meta.json: " +
        "The process cannot access the file because it is being used by another process.";

    private static readonly string[] BuildArguments = ["build", "-t", "codebrix-test/retry:latest", "."];

    // ---------------------------------------------------------------------------------------
    // The predicate
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void IsTransientInitializationFailure_ForTheObservedDockerDesktopSharingViolation_ReturnsTrue()
    {
        //Arrange
        var standardError = ObservedSharingViolation + Environment.NewLine;

        //Act
        var transient = DockerCliRunner.IsTransientInitializationFailure(1, standardError);

        //Assert
        transient.Should().BeTrue();
    }

    [Theory]
    [InlineData(1, "FAILED TO INITIALIZE: unable to resolve docker endpoint: context \"desktop-linux\": open " +
                   @"C:\Users\someone\.docker\contexts\meta\abc\meta.json: THE PROCESS CANNOT ACCESS THE FILE " +
                   "BECAUSE IT IS BEING USED BY ANOTHER PROCESS.")]
    [InlineData(1, "failed to initialize: unable to resolve docker endpoint: context \"desktop-linux\": open " +
                   "/home/someone/.docker/contexts/meta/abc/meta.json: the process cannot access the file " +
                   "because it is being used by another process.")]
    [InlineData(1, "WARNING: a line the CLI wrote first\nFailed to initialize: open " +
                   @"C:\Users\someone\.docker\contexts\tls\abc\docker\ca.pem: The process cannot access the file " +
                   "because it is being used by another process.\n")]
    [InlineData(125, ObservedSharingViolation)]
    public void IsTransientInitializationFailure_ForVariantsOfTheSharingViolationDuringStartUp_ReturnsTrue(
        int exitCode, string standardError)
    {
        //Arrange
        // Case does not matter, neither does the shape of the path or the file named, nor other lines around
        // the message; what matters is that the CLI stopped while starting up, over a file another process
        // had open.

        //Act
        var transient = DockerCliRunner.IsTransientInitializationFailure(exitCode, standardError);

        //Assert
        transient.Should().BeTrue();
    }

    [Theory]
    [InlineData("Cannot connect to the Docker daemon at unix:///var/run/docker.sock. Is the docker daemon running?")]
    [InlineData("permission denied while trying to connect to the Docker daemon socket at " +
                "unix:///var/run/docker.sock: Get \"http://%2Fvar%2Frun%2Fdocker.sock/v1.51/version\": dial unix " +
                "/var/run/docker.sock: connect: permission denied")]
    [InlineData("ERROR: failed to solve: process \"/bin/sh -c exit 1\" did not complete successfully: exit code: 1")]
    [InlineData("Error response from daemon: No such container: x")]
    [InlineData("Failed to initialize: unable to resolve docker endpoint: context \"nope\": context not found: " +
                "open /home/someone/.docker/contexts/meta/abc/meta.json: no such file or directory")]
    [InlineData("unable to resolve docker endpoint: context \"desktop-linux\": open " +
                @"C:\Users\someone\.docker\contexts\meta\abc\meta.json: The process cannot access the file because " +
                "it is being used by another process.")]
    [InlineData("ERROR: failed to solve: failed to read dockerfile: open Dockerfile: The process cannot access the " +
                "file because it is being used by another process.")]
    [InlineData("Failed to initialize: open " + @"C:\Users\someone\.docker\contexts\meta\abc\meta.json: " +
                "The process cannot access the file because another process has locked a portion of the file.")]
    [InlineData("")]
    [InlineData(null)]
    public void IsTransientInitializationFailure_ForAnyOtherFailure_ReturnsFalse(string standardError)
    {
        //Arrange
        // Ordinary failures, a start-up failure that is not a sharing violation (a context that does not
        // exist), a sharing violation outside start-up (it can come from a step that did reach the engine),
        // the endpoint wording without "Failed to initialize", and nothing at all.

        //Act
        var transient = DockerCliRunner.IsTransientInitializationFailure(1, standardError);

        //Assert
        transient.Should().BeFalse();
    }

    [Fact]
    public void IsTransientInitializationFailure_WhenTheAttemptSucceeded_ReturnsFalse()
    {
        //Arrange
        // A command that exits 0 has nothing to retry, whatever it printed on the way.

        //Act
        var transient = DockerCliRunner.IsTransientInitializationFailure(0, ObservedSharingViolation);

        //Assert
        transient.Should().BeFalse();
    }

    [Fact]
    public void TransientFailureRetryDelays_AllowsThreeRetriesWithGrowingPauses()
    {
        //Arrange
        var delays = DockerCliRunner.TransientFailureRetryDelays;

        //Act
        var milliseconds = delays.Select(delay => delay.TotalMilliseconds).ToArray();

        //Assert
        milliseconds.Should().Equal(100, 250, 500);
    }

    // ---------------------------------------------------------------------------------------
    // The retry loop
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_AfterTwoTransientInitializationFailures_SucceedsOnTheThirdAttempt()
    {
        //Arrange
        var cli = new ScriptedCli(attempt => attempt <= 2
            ? new CliResult(1, string.Empty, ObservedSharingViolation)
            : new CliResult(0, "built", string.Empty));
        var runner = cli.CreateRunner(ZeroDelays());
        var progress = new CollectingProgress();

        //Act
        var result = await runner.RunAsync(BuildArguments, "/context", progress, TestContext.Current.CancellationToken);

        //Assert
        cli.Attempts.Should().Be(3);
        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Be("built");
        result.Stderr.Should().BeEmpty();
        cli.Arguments.Should().AllSatisfy(arguments => arguments.Should().Equal(BuildArguments));
        cli.WorkingDirectories.Should().Equal("/context", "/context", "/context");
        progress.Lines.Should().HaveCount(5);
        progress.Lines[0].Should().Be(ObservedSharingViolation);
        progress.Lines[1].Should().Contain("retrying").And.Contain("attempt 2 of 4");
        progress.Lines[2].Should().Be(ObservedSharingViolation);
        progress.Lines[3].Should().Contain("retrying").And.Contain("attempt 3 of 4");
        progress.Lines[4].Should().Be("built");
    }

    [Fact]
    public async Task RunAsync_WhenEveryAttemptFailsToInitialize_ThrowsDescribingTheLastAttempt()
    {
        //Arrange
        var cli = new ScriptedCli(attempt =>
            new CliResult(1, string.Empty, $"{ObservedSharingViolation} (attempt {attempt})"));
        var runner = cli.CreateRunner(ZeroDelays());

        //Act
        Func<Task> run = () => runner.RunAsync(BuildArguments,
            cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        var thrown = await run.Should().ThrowAsync<DockerCliException>();
        cli.Attempts.Should().Be(DockerCliRunner.TransientFailureRetryDelays.Count + 1);
        thrown.Which.ExitCode.Should().Be(1);
        thrown.Which.StdErr.Should().Be($"{ObservedSharingViolation} (attempt 4)");
        thrown.Which.Command.Should().Be("docker build -t codebrix-test/retry:latest .");
        thrown.Which.Message.Should().Contain("(attempt 4)");
    }

    [Fact]
    public async Task TryRunAsync_WhenEveryAttemptFailsToInitialize_ReturnsTheLastAttemptWithoutThrowing()
    {
        //Arrange
        var cli = new ScriptedCli(attempt =>
            new CliResult(1, $"out {attempt}", $"{ObservedSharingViolation} (attempt {attempt})"));
        var runner = cli.CreateRunner(ZeroDelays());

        //Act
        var result = await runner.TryRunAsync(BuildArguments, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        cli.Attempts.Should().Be(4);
        result.ExitCode.Should().Be(1);
        result.Stdout.Should().Be("out 4");
        result.Stderr.Should().Be($"{ObservedSharingViolation} (attempt 4)");
    }

    [Fact]
    public async Task RunAsync_WithTheDefaultSchedule_PausesBetweenFourAttemptsInAll()
    {
        //Arrange
        // The one test that takes the real pauses: 100 + 250 + 500 ms.
        var cli = new ScriptedCli(_ => new CliResult(1, string.Empty, ObservedSharingViolation));
        var runner = cli.CreateRunner(retryDelays: null);
        var stopwatch = Stopwatch.StartNew();

        //Act
        var result = await runner.TryRunAsync(BuildArguments, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        stopwatch.Stop();
        cli.Attempts.Should().Be(4);
        result.ExitCode.Should().Be(1);
        stopwatch.Elapsed.Should().BeGreaterThan(TimeSpan.FromMilliseconds(700));
    }

    [Theory]
    [InlineData("Cannot connect to the Docker daemon at unix:///var/run/docker.sock. Is the docker daemon running?")]
    [InlineData("permission denied while trying to connect to the Docker daemon socket at unix:///var/run/docker.sock")]
    [InlineData("ERROR: failed to solve: process \"/bin/sh -c exit 1\" did not complete successfully: exit code: 1")]
    [InlineData("Error response from daemon: No such container: x")]
    public async Task RunAsync_ForAnyOtherFailure_RunsExactlyOnceAndThrowsAsBefore(string standardError)
    {
        //Arrange
        var cli = new ScriptedCli(_ => new CliResult(1, string.Empty, standardError));
        var runner = cli.CreateRunner(ZeroDelays());
        var progress = new CollectingProgress();

        //Act
        Func<Task> run = () => runner.RunAsync(BuildArguments, output: progress,
            cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        var thrown = await run.Should().ThrowAsync<DockerCliException>();
        cli.Attempts.Should().Be(1);
        thrown.Which.ExitCode.Should().Be(1);
        thrown.Which.StdErr.Should().Be(standardError);
        progress.Lines.Should().Equal(standardError);
    }

    [Fact]
    public async Task RunAsync_WhenTheAttemptCannotStartTheProcess_DoesNotRetry()
    {
        //Arrange
        var cli = new ScriptedCli(_ => throw new DockerCliException("docker build", -1, "Could not start 'docker'."));
        var runner = cli.CreateRunner(ZeroDelays());

        //Act
        Func<Task> run = () => runner.RunAsync(BuildArguments,
            cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        var thrown = await run.Should().ThrowAsync<DockerCliException>();
        cli.Attempts.Should().Be(1);
        thrown.Which.ExitCode.Should().Be(-1);
    }

    [Fact]
    public async Task RunAsync_WhenCancelledDuringTheRetryPause_ThrowsOperationCanceledAndStartsNoFurtherAttempt()
    {
        //Arrange
        // The pause is long enough that only the token can end it.
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var cli = new ScriptedCli(_ =>
        {
            cancellation.Cancel();
            return new CliResult(1, string.Empty, ObservedSharingViolation);
        });
        var runner = cli.CreateRunner([TimeSpan.FromMinutes(10)]);

        //Act
        Func<Task> run = () => runner.RunAsync(BuildArguments, cancellationToken: cancellation.Token);

        //Assert
        await run.Should().ThrowWithinAsync<OperationCanceledException>(TimeSpan.FromSeconds(30));
        cli.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_WhenTheDockerExecutableDoesNotExist_ThrowsCouldNotStartWithoutRetrying()
    {
        //Arrange
        // The real process path, with no Docker anywhere: a start failure is raised, never retried.
        var runner = new DockerCliRunner(new DockerClientOptions
        {
            DockerCliPath = "codebrix-docker-no-such-cli-" + Guid.NewGuid().ToString("N")[..8],
        });

        //Act
        Func<Task> run = () => runner.RunAsync(["version"], cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        var thrown = await run.Should().ThrowAsync<DockerCliException>();
        thrown.Which.ExitCode.Should().Be(-1);
        thrown.Which.StdErr.Should().Contain("Could not start 'codebrix-docker-no-such-cli-");
    }

    // ---------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------

    /// <summary>The default number of retries, with no pause between them.</summary>
    private static TimeSpan[] ZeroDelays() =>
        Enumerable.Repeat(TimeSpan.Zero, DockerCliRunner.TransientFailureRetryDelays.Count).ToArray();

    /// <summary>
    /// Stands in for the docker process: each attempt returns the scripted result for its 1-based number,
    /// after reporting that result's lines to the output receiver the way the real runner does.
    /// </summary>
    private sealed class ScriptedCli(Func<int, CliResult> script)
    {
        private readonly List<IReadOnlyList<string>> _arguments = [];
        private readonly List<string> _workingDirectories = [];

        /// <summary>Gets how many attempts have been made.</summary>
        public int Attempts { get; private set; }

        /// <summary>Gets the argument list each attempt was given.</summary>
        public IReadOnlyList<IReadOnlyList<string>> Arguments => _arguments;

        /// <summary>Gets the working directory each attempt was given.</summary>
        public IReadOnlyList<string> WorkingDirectories => _workingDirectories;

        /// <summary>Creates a runner whose attempts are this script.</summary>
        public DockerCliRunner CreateRunner(IReadOnlyList<TimeSpan> retryDelays) =>
            new(new DockerClientOptions(), RunAttemptAsync, retryDelays);

        private Task<CliResult> RunAttemptAsync(IReadOnlyList<string> args, string workingDir,
            IProgress<string> output, CancellationToken cancellationToken)
        {
            Attempts++;
            _arguments.Add(args.ToArray());
            _workingDirectories.Add(workingDir);

            var result = script(Attempts);
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
