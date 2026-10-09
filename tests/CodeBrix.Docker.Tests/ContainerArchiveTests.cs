using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Docker.Tests;

/// <summary>
/// Exercises copying into and out of containers against a live daemon: whole trees with exclusions,
/// modes and symbolic links, single files, path inspection, the raw archive stream, round trips, a
/// large transfer, error mapping and cancellation.
/// </summary>
[Collection(DockerTestCollection.Name)]
public sealed class ContainerArchiveTests(DockerTestFixture fixture)
{
    private const UnixFileMode Mode644 = UnixFileMode.UserRead | UnixFileMode.UserWrite
                                         | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    private const UnixFileMode Mode755 = Mode644 | UnixFileMode.UserExecute | UnixFileMode.GroupExecute
                                         | UnixFileMode.OtherExecute;

    private DockerClient Client => fixture.Client;

    [Fact]
    public async Task CopyDirectoryToContainerAsync_IntoACreatedContainer_CopiesTheTreeWithModesAndWithoutExclusions()
    {
        //Arrange
        using var cancellation = Timeout(TimeSpan.FromMinutes(3));
        using var host = new TempDirectory();
        var tree = SourceTree.Create(host.Path);
        var progress = new RecordingProgress<CopyProgress>();
        string id = null;

        try
        {
            id = await Client.Containers.CreateAsync(fixture.Spec("cp-created", "alpine:latest", "sleep", "300"),
                cancellation.Token);

            //Act
            await Client.Containers.CopyDirectoryToContainerAsync(id, tree.Root, "/tmp", new CopyToContainerOptions
            {
                IncludeRootFolder = true,
                ExcludePatterns = ["target"],
                ExecutableExtensions = [".sh"],
                Progress = progress,
            }, cancellation.Token);

            await Client.Containers.StartAsync(id, cancellation.Token);
            var hashes = await ExecOkAsync(id, cancellation.Token, "sh", "-c",
                "cd /tmp/proj && sha256sum README.txt run.sh data/blob.bin data/nested/deep/note.txt");
            var modes = await ExecOkAsync(id, cancellation.Token, "stat", "-c", "%a %n",
                "/tmp/proj/run.sh", "/tmp/proj/README.txt");
            var excluded = await Client.Containers.ExecAsync(id, ["test", "-e", "/tmp/proj/target"],
                cancellationToken: cancellation.Token);
            var link = tree.HasSymlink
                ? await ExecOkAsync(id, cancellation.Token, "readlink", "/tmp/proj/docs-link")
                : "README.txt";
            var runs = await ExecOkAsync(id, cancellation.Token, "/tmp/proj/run.sh");

            //Assert
            ParseHashes(hashes).Should().Equal(tree.Hashes);
            modes.Should().Contain("755 /tmp/proj/run.sh");
            modes.Should().Contain("644 /tmp/proj/README.txt");
            excluded.ExitCode.Should().NotBe(0);
            link.Trim().Should().Be("README.txt");
            runs.Trim().Should().Be("script-ran");
            progress.Values.Count.Should().BeGreaterThan(0);
            progress.Values[^1].BytesCopied.Should().Be(tree.TotalBytes);
            progress.Values.Should().NotContain(report => report.CurrentPath.Contains("target/"));
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task CopyDirectoryToContainerAsync_IntoARunningContainer_PlacesTheContentsInTheDestination()
    {
        //Arrange
        using var cancellation = Timeout(TimeSpan.FromMinutes(3));
        using var host = new TempDirectory();
        var tree = SourceTree.Create(host.Path);
        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(fixture.Spec("cp-running", "alpine:latest", "sleep", "300"),
                cancellation.Token);
            await ExecOkAsync(id, cancellation.Token, "mkdir", "-p", "/work");

            //Act
            await Client.Containers.CopyDirectoryToContainerAsync(id, tree.Root, "/work", new CopyToContainerOptions
            {
                ExcludePatterns = ["target/", "*.nothing"],
            }, cancellation.Token);
            await Client.Containers.CopyFileToContainerAsync(id, tree.Readme, "/srv",
                cancellationToken: cancellation.Token);

            var hashes = await ExecOkAsync(id, cancellation.Token, "sh", "-c",
                "cd /work && sha256sum README.txt run.sh data/blob.bin data/nested/deep/note.txt");
            var listing = await ExecOkAsync(id, cancellation.Token, "ls", "-la", "/work");
            var single = await ExecOkAsync(id, cancellation.Token, "sha256sum", "/srv/README.txt");

            //Assert
            ParseHashes(hashes).Should().Equal(tree.Hashes);
            listing.Should().NotContain("target");
            listing.Should().Contain("run.sh");
            single.Should().StartWith(tree.Hashes["README.txt"]);
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task CopyDirectoryToContainerAsync_WithoutPreservedModes_AppliesTheConfiguredModes()
    {
        //Arrange
        using var cancellation = Timeout(TimeSpan.FromMinutes(2));
        using var host = new TempDirectory();
        var tree = SourceTree.Create(host.Path);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(tree.Readme, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(fixture.Spec("cp-modes", "alpine:latest", "sleep", "300"),
                cancellation.Token);

            //Act
            await Client.Containers.CopyDirectoryToContainerAsync(id, tree.Root, "/srv", new CopyToContainerOptions
            {
                PreserveUnixModes = false,
                ExcludePatterns = ["target", "data"],
                ExecutableExtensions = [".bin"],
            }, cancellation.Token);
            var modes = await ExecOkAsync(id, cancellation.Token, "stat", "-c", "%a %n",
                "/srv/README.txt", "/srv/run.sh");

            //Assert
            modes.Should().Contain("644 /srv/README.txt");
            modes.Should().Contain("755 /srv/run.sh");
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task CopyToContainerAsync_WithBadDestinations_SaysWhichThingIsWrong()
    {
        //Arrange
        using var cancellation = Timeout(TimeSpan.FromMinutes(2));
        using var host = new TempDirectory();
        var tree = SourceTree.Create(host.Path);
        string id = null;

        try
        {
            id = await Client.Containers.CreateAsync(fixture.Spec("cp-errors", "alpine:latest", "sleep", "300"),
                cancellation.Token);

            //Act
            Func<Task> missingPath = () => Client.Containers.CopyDirectoryToContainerAsync(id, tree.Root,
                "/does/not/exist", cancellationToken: cancellation.Token);
            Func<Task> ontoAFile = () => Client.Containers.CopyFileToContainerAsync(id, tree.Readme,
                "/etc/passwd", cancellationToken: cancellation.Token);
            Func<Task> missingContainer = () => Client.Containers.CopyFileToContainerAsync(
                fixture.NewName("absent"), tree.Readme, "/tmp", cancellationToken: cancellation.Token);
            Func<Task> missingHostFolder = () => Client.Containers.CopyDirectoryToContainerAsync(id,
                Path.Combine(host.Path, "nowhere"), "/tmp", cancellationToken: cancellation.Token);
            using var emptyTar = new MemoryStream();
            Func<Task> rawMissingPath = () => Client.Containers.CopyToContainerAsync(id, emptyTar, "/nope",
                cancellationToken: cancellation.Token);

            //Assert
            var notFound = await missingPath.Should().ThrowAsync<DockerApiException>();
            notFound.Which.Should().NotBeOfType<DockerContainerNotFoundException>();
            notFound.Which.StatusCode.Should().Be(HttpStatusCode.NotFound);
            notFound.Which.Message.Should().Contain("/does/not/exist");

            var badRequest = await ontoAFile.Should().ThrowAsync<DockerApiException>();
            badRequest.Which.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            badRequest.Which.Message.Should().Contain("not a directory");

            await missingContainer.Should().ThrowAsync<DockerContainerNotFoundException>();
            await missingHostFolder.Should().ThrowAsync<DirectoryNotFoundException>();

            var raw = await rawMissingPath.Should().ThrowAsync<DockerApiException>();
            raw.Which.Should().NotBeOfType<DockerContainerNotFoundException>();
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task CopyDirectoryToContainerAsync_OntoAReadOnlyMount_SaysTheMountIsReadOnly()
    {
        //Arrange
        using var cancellation = Timeout(TimeSpan.FromMinutes(2));
        using var host = new TempDirectory();
        var tree = SourceTree.Create(Path.Combine(host.Path, "source"));
        var mounted = Path.Combine(host.Path, "mounted");
        Directory.CreateDirectory(mounted);
        var spec = fixture.Spec("cp-readonly", "alpine:latest", "sleep", "300");
        spec.Mounts.Add(MountSpec.Bind(mounted, "/data", readOnly: true));
        string id = null;

        try
        {
            id = await Client.Containers.CreateAsync(spec, cancellation.Token);

            //Act
            Func<Task> wholeTree = () => Client.Containers.CopyDirectoryToContainerAsync(id, tree.Root, "/data",
                cancellationToken: cancellation.Token);
            Func<Task> singleFile = () => Client.Containers.CopyFileToContainerAsync(id, tree.Readme, "/data",
                cancellationToken: cancellation.Token);

            //Assert
            var treeFailure = await wholeTree.Should().ThrowAsync<DockerApiException>();
            treeFailure.Which.Message.Should().Contain("read-only");
            var fileFailure = await singleFile.Should().ThrowAsync<DockerApiException>();
            fileFailure.Which.Message.Should().Contain("read-only");
            Directory.GetFileSystemEntries(mounted).Should().BeEmpty();
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task StatPathAsync_DescribesFilesDirectoriesLinksAndMissingPaths()
    {
        //Arrange
        using var cancellation = Timeout(TimeSpan.FromMinutes(2));
        string id = null;

        try
        {
            id = await Client.Containers.CreateAsync(fixture.Spec("cp-stat", "alpine:latest", "sleep", "300"),
                cancellation.Token);

            //Act
            var file = await Client.Containers.StatPathAsync(id, "/etc/passwd", cancellation.Token);
            var directory = await Client.Containers.StatPathAsync(id, "/etc", cancellation.Token);
            var link = await Client.Containers.StatPathAsync(id, "/bin/sh", cancellation.Token);
            Func<Task> missingPath = () => Client.Containers.StatPathAsync(id, "/no/such/path", cancellation.Token);
            Func<Task> missingContainer = () => Client.Containers.StatPathAsync(fixture.NewName("absent"), "/etc",
                cancellation.Token);

            //Assert
            file.Name.Should().Be("passwd");
            file.IsRegularFile.Should().BeTrue();
            file.IsDirectory.Should().BeFalse();
            file.Size.Should().BeGreaterThan(0);
            file.PermissionBits.Should().Be(Mode644);
            file.Modified.Year.Should().BeGreaterThan(2000);

            directory.IsDirectory.Should().BeTrue();
            directory.IsRegularFile.Should().BeFalse();
            directory.PermissionBits.Should().Be(Mode755);

            link.IsSymlink.Should().BeTrue();
            link.IsRegularFile.Should().BeFalse();
            link.LinkTarget.Should().Be("/bin/busybox");

            var notFound = await missingPath.Should().ThrowAsync<DockerApiException>();
            notFound.Which.Should().NotBeOfType<DockerContainerNotFoundException>();
            notFound.Which.StatusCode.Should().Be(HttpStatusCode.NotFound);
            notFound.Which.Message.Should().Contain("/no/such/path");
            await missingContainer.Should().ThrowAsync<DockerContainerNotFoundException>();
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task CopyFromContainerAsync_ReturnsATarStreamRootedAtTheBaseName()
    {
        //Arrange
        using var cancellation = Timeout(TimeSpan.FromMinutes(2));
        string id = null;

        try
        {
            id = await Client.Containers.CreateAsync(fixture.Spec("cp-raw", "alpine:latest", "sleep", "300"),
                cancellation.Token);

            //Act
            var names = new List<string>();
            string passwd = null;
            await using (var archive = await Client.Containers.CopyFromContainerAsync(id, "/etc", cancellation.Token))
            {
                await using var reader = new TarReader(archive);
                while (await reader.GetNextEntryAsync(copyData: false, cancellation.Token) is { } entry)
                {
                    names.Add(entry.Name);
                    if (entry.Name == "etc/passwd" && entry.DataStream is not null)
                    {
                        using var text = new StreamReader(entry.DataStream);
                        passwd = await text.ReadToEndAsync(cancellation.Token);
                    }
                }
            }

            Func<Task> missingPath = () => Client.Containers.CopyFromContainerAsync(id, "/no/such/path",
                cancellation.Token);

            //Assert
            names[0].TrimEnd('/').Should().Be("etc");
            names.Should().Contain("etc/passwd");
            names.Should().OnlyContain(name => name.StartsWith("etc", StringComparison.Ordinal));
            passwd.Should().Contain("root:x:0:0");
            var notFound = await missingPath.Should().ThrowAsync<DockerApiException>();
            notFound.Which.Should().NotBeOfType<DockerContainerNotFoundException>();
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task CopyFromContainerToDirectoryAsync_RoundTripsTheTreeBothWithAndWithoutTheRootFolder()
    {
        //Arrange
        using var cancellation = Timeout(TimeSpan.FromMinutes(3));
        using var host = new TempDirectory();
        var tree = SourceTree.Create(Path.Combine(host.Path, "source"));
        var keptRoot = Path.Combine(host.Path, "kept");
        var stripped = Path.Combine(host.Path, "stripped");
        var progress = new RecordingProgress<CopyProgress>();
        string id = null;

        try
        {
            id = await Client.Containers.CreateAsync(fixture.Spec("cp-roundtrip", "alpine:latest", "sleep", "300"),
                cancellation.Token);
            await Client.Containers.CopyDirectoryToContainerAsync(id, tree.Root, "/srv", new CopyToContainerOptions
            {
                IncludeRootFolder = true,
                ExcludePatterns = ["target"],
                ExecutableExtensions = [".sh"],
            }, cancellation.Token);

            //Act
            var kept = await Client.Containers.CopyFromContainerToDirectoryAsync(id, "/srv/proj", keptRoot,
                new CopyFromContainerOptions { Progress = progress }, cancellation.Token);
            var flat = await Client.Containers.CopyFromContainerToDirectoryAsync(id, "/srv/proj", stripped,
                new CopyFromContainerOptions { StripRootFolder = true, SkipSymlinks = false }, cancellation.Token);

            //Assert
            HashTree(Path.Combine(keptRoot, "proj"), tree.Hashes.Keys).Should().Equal(tree.Hashes);
            HashTree(stripped, tree.Hashes.Keys).Should().Equal(tree.Hashes);
            Directory.Exists(Path.Combine(stripped, "proj")).Should().BeFalse();
            Directory.Exists(Path.Combine(keptRoot, "proj", "target")).Should().BeFalse();

            if (!OperatingSystem.IsWindows())
            {
                File.GetUnixFileMode(Path.Combine(keptRoot, "proj", "run.sh")).Should().Be(Mode755);
                File.GetUnixFileMode(Path.Combine(keptRoot, "proj", "README.txt")).Should().Be(Mode644);
            }

            kept.FileCount.Should().Be(tree.Hashes.Count);
            kept.TotalBytes.Should().Be(tree.TotalBytes);
            kept.DirectoryCount.Should().BeGreaterThan(3);
            File.Exists(Path.Combine(keptRoot, "proj", "docs-link")).Should().BeFalse();

            var script = kept.Entries.Single(entry => entry.HostPath.EndsWith("run.sh", StringComparison.Ordinal));
            script.ContainerPath.Should().Be("/srv/proj/run.sh");
            script.Mode.Should().Be(Mode755);
            script.IsExecutable.Should().BeTrue();
            script.IsDirectory.Should().BeFalse();

            if (tree.HasSymlink)
            {
                kept.SkippedEntries.Should().Contain("proj/docs-link");
                flat.SkippedEntries.Should().BeEmpty();
                new FileInfo(Path.Combine(stripped, "docs-link")).LinkTarget.Should().Be("README.txt");
            }

            progress.Values.Count.Should().Be(tree.Hashes.Count);
            progress.Values[^1].BytesCopied.Should().Be(tree.TotalBytes);
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task CopyFileToContainerAsync_WithFiftyMegabytes_StreamsBothWaysIntact()
    {
        //Arrange
        using var cancellation = Timeout(TimeSpan.FromMinutes(4));
        using var host = new TempDirectory();
        var large = Path.Combine(host.Path, "large.bin");
        WriteRandomFile(large, 50 * 1024 * 1024);
        var expected = Sha256Hex(large);
        var output = Path.Combine(host.Path, "out");
        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(fixture.Spec("cp-large", "alpine:latest", "sleep", "300"),
                cancellation.Token);

            //Act
            await Client.Containers.CopyFileToContainerAsync(id, large, "/tmp", cancellationToken: cancellation.Token);
            var inside = await ExecOkAsync(id, cancellation.Token, "sha256sum", "/tmp/large.bin");
            var result = await Client.Containers.CopyFromContainerToDirectoryAsync(id, "/tmp/large.bin", output,
                cancellationToken: cancellation.Token);

            //Assert
            inside.Should().StartWith(expected);
            result.FileCount.Should().Be(1);
            result.TotalBytes.Should().Be(50 * 1024 * 1024);
            Sha256Hex(Path.Combine(output, "large.bin")).Should().Be(expected);
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task CopyToContainerAsync_WhenCancelledMidStream_IsCancelled()
    {
        //Arrange
        using var cancellation = Timeout(TimeSpan.FromMinutes(2));
        using var copyCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(fixture.Spec("cp-cancel-in", "alpine:latest", "sleep", "300"),
                cancellation.Token);

            using var archive = new MemoryStream();
            await using (var writer = new TarWriter(archive, TarEntryFormat.Pax, leaveOpen: true))
            {
                var payload = new byte[16 * 1024 * 1024];
                Random.Shared.NextBytes(payload);
                await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.RegularFile, "partial.bin")
                {
                    DataStream = new MemoryStream(payload),
                }, cancellation.Token);
            }

            archive.Position = 0;
            using var tripwire = new CancelAfterBytesStream(archive, 2 * 1024 * 1024, copyCancellation);

            //Act
            Func<Task> copy = () => Client.Containers.CopyToContainerAsync(id, tripwire, "/tmp",
                cancellationToken: copyCancellation.Token);

            //Assert
            await copy.Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task CopyFromContainerToDirectoryAsync_WhenCancelledBetweenFiles_KeepsOnlyFinishedFiles()
    {
        //Arrange
        using var cancellation = Timeout(TimeSpan.FromMinutes(2));
        using var copyCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
        using var host = new TempDirectory();
        var output = Path.Combine(host.Path, "out");
        var cancelOnFirst = new CallbackProgress<CopyProgress>(_ => copyCancellation.Cancel());
        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(fixture.Spec("cp-cancel-out", "alpine:latest", "sleep", "300"),
                cancellation.Token);
            await ExecOkAsync(id, cancellation.Token, "sh", "-c",
                "mkdir /bulk && for i in 1 2 3 4 5; do head -c 4194304 /dev/urandom > /bulk/f$i.bin; done");

            //Act
            Func<Task> copy = () => Client.Containers.CopyFromContainerToDirectoryAsync(id, "/bulk", output,
                new CopyFromContainerOptions { Progress = cancelOnFirst }, copyCancellation.Token);

            //Assert
            await copy.Should().ThrowAsync<OperationCanceledException>();
            var written = Directory.GetFiles(output, "*", SearchOption.AllDirectories);
            written.Length.Should().Be(1);
            new FileInfo(written[0]).Length.Should().Be(4194304);
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------

    private static CancellationTokenSource Timeout(TimeSpan timeout)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        source.CancelAfter(timeout);
        return source;
    }

    private async Task<string> ExecOkAsync(string id, CancellationToken cancellationToken, params string[] command)
    {
        var result = await Client.Containers.ExecAsync(id, command, cancellationToken: cancellationToken);
        result.ExitCode.Should().Be(0, $"'{string.Join(' ', command)}' failed: {result.Stderr}");
        return result.Stdout;
    }

    private static Dictionary<string, string> ParseHashes(string sha256sumOutput) =>
        sha256sumOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split("  ", 2))
            .ToDictionary(parts => parts[1].Trim(), parts => parts[0], StringComparer.Ordinal);

    private static Dictionary<string, string> HashTree(string root, IEnumerable<string> relativePaths) =>
        relativePaths.ToDictionary(path => path, path => Sha256Hex(Path.Combine(root, path)), StringComparer.Ordinal);

    private static string Sha256Hex(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static void WriteRandomFile(string path, int length)
    {
        var buffer = new byte[1024 * 1024];
        using var stream = File.Create(path);
        for (var written = 0; written < length; written += buffer.Length)
        {
            Random.Shared.NextBytes(buffer);
            stream.Write(buffer, 0, Math.Min(buffer.Length, length - written));
        }
    }

    /// <summary>The tree every copy test moves around, with the hashes of the files that must survive.</summary>
    private sealed record SourceTree(string Root, string Readme, Dictionary<string, string> Hashes, long TotalBytes,
        bool HasSymlink)
    {
        public static SourceTree Create(string parent)
        {
            var root = Path.Combine(parent, "proj");
            Directory.CreateDirectory(Path.Combine(root, "data", "nested", "deep"));
            Directory.CreateDirectory(Path.Combine(root, "target", "debug"));

            var readme = Path.Combine(root, "README.txt");
            File.WriteAllText(readme, "A tree for the copy tests.\n");

            var script = Path.Combine(root, "run.sh");
            File.WriteAllText(script, "#!/bin/sh\necho script-ran\n");

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(readme, Mode644);
                File.SetUnixFileMode(script, Mode755);
            }

            WriteRandomFile(Path.Combine(root, "data", "blob.bin"), 1024 * 1024);
            File.WriteAllText(Path.Combine(root, "data", "nested", "deep", "note.txt"), "deep note\n");
            WriteRandomFile(Path.Combine(root, "target", "debug", "out.bin"), 64 * 1024);
            var hasSymlink = true;
            try
            {
                File.CreateSymbolicLink(Path.Combine(root, "docs-link"), "README.txt");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A Windows host without the symbolic-link privilege.
                hasSymlink = false;
            }

            string[] kept = ["README.txt", "run.sh", "data/blob.bin", "data/nested/deep/note.txt"];
            var hashes = kept.ToDictionary(path => path, path => Sha256Hex(Path.Combine(root, path)),
                StringComparer.Ordinal);
            var total = kept.Sum(path => new FileInfo(Path.Combine(root, path)).Length);
            return new SourceTree(root, readme, hashes, total, hasSymlink);
        }
    }

    /// <summary>A progress sink that records every report synchronously.</summary>
    private sealed class RecordingProgress<T> : IProgress<T>
    {
        private readonly List<T> _values = [];

        public IReadOnlyList<T> Values
        {
            get
            {
                lock (_values)
                {
                    return _values.ToArray();
                }
            }
        }

        public void Report(T value)
        {
            lock (_values)
            {
                _values.Add(value);
            }
        }
    }

    /// <summary>A progress sink that runs a callback synchronously.</summary>
    private sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}
