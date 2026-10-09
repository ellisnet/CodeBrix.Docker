using System;
using System.Formats.Tar;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Docker.Tests;

/// <summary>
/// Daemon-free tests for <see cref="ContainerArchiveExtractor"/>, fed archives built in memory. The
/// daemon never produces a hostile archive, so this is where the path-escape guard is fenced.
/// </summary>
public sealed class ContainerArchiveExtractorTests
{
    [Fact]
    public async Task ExtractAsync_WithAnEntryThatClimbsOutOfTheDestination_ThrowsNamingTheEntry()
    {
        //Arrange
        using var host = new TempDirectory();
        var destination = Path.Combine(host.Path, "out");
        using var archive = BuildArchive(("ok.txt", "fine"), ("../evil", "escaped"));

        //Act
        Func<Task> extract = () => ContainerArchiveExtractor.ExtractAsync(archive, "/data", destination, null,
            TestContext.Current.CancellationToken);

        //Assert
        var thrown = await extract.Should().ThrowAsync<DockerException>();
        thrown.Which.Message.Should().Contain("../evil");
        File.Exists(Path.Combine(host.Path, "evil")).Should().BeFalse();
        File.Exists(Path.Combine(destination, "ok.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task ExtractAsync_WithAnAbsoluteOrNestedClimbingEntry_Throws()
    {
        //Arrange
        using var host = new TempDirectory();
        var destination = Path.Combine(host.Path, "out");
        using var absolute = BuildArchive(("/etc/evil", "escaped"));
        using var nested = BuildArchive(("a/../../evil", "escaped"));

        //Act
        Func<Task> extractAbsolute = () => ContainerArchiveExtractor.ExtractAsync(absolute, "/data", destination,
            null, TestContext.Current.CancellationToken);
        Func<Task> extractNested = () => ContainerArchiveExtractor.ExtractAsync(nested, "/data", destination,
            null, TestContext.Current.CancellationToken);

        //Assert
        (await extractAbsolute.Should().ThrowAsync<DockerException>()).Which.Message.Should().Contain("/etc/evil");
        (await extractNested.Should().ThrowAsync<DockerException>()).Which.Message.Should().Contain("a/../../evil");
        File.Exists(Path.Combine(host.Path, "evil")).Should().BeFalse();
    }

    [Fact]
    public async Task ExtractAsync_ThroughAnExistingLinkThatLeadsOutside_Throws()
    {
        //Arrange
        using var host = new TempDirectory();
        var destination = Path.Combine(host.Path, "out");
        var outside = Path.Combine(host.Path, "outside");
        Directory.CreateDirectory(destination);
        Directory.CreateDirectory(outside);
        Directory.CreateSymbolicLink(Path.Combine(destination, "escape"), outside);
        using var archive = BuildArchive(("escape/evil", "escaped"));

        //Act
        Func<Task> extract = () => ContainerArchiveExtractor.ExtractAsync(archive, "/data", destination, null,
            TestContext.Current.CancellationToken);

        //Assert
        await extract.Should().ThrowAsync<DockerException>();
        File.Exists(Path.Combine(outside, "evil")).Should().BeFalse();
    }

    [Fact]
    public async Task ExtractAsync_WithLinksThatLeaveTheDestination_SkipsThemEvenWhenLinksAreAllowed()
    {
        //Arrange
        using var host = new TempDirectory();
        var destination = Path.Combine(host.Path, "out");
        using var archive = new MemoryStream();
        await using (var writer = new TarWriter(archive, TarEntryFormat.Pax, leaveOpen: true))
        {
            await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.SymbolicLink, "up") { LinkName = "../../etc" },
                TestContext.Current.CancellationToken);
            await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.SymbolicLink, "abs") { LinkName = "/etc/passwd" },
                TestContext.Current.CancellationToken);
        }

        archive.Position = 0;

        //Act
        var result = await ContainerArchiveExtractor.ExtractAsync(archive, "/data", destination,
            new CopyFromContainerOptions { SkipSymlinks = false }, TestContext.Current.CancellationToken);

        //Assert
        result.SkippedEntries.Should().BeEquivalentTo(["up", "abs"]);
        Directory.GetFileSystemEntries(destination).Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractAsync_WithStripRootFolder_PlacesTheContentsDirectlyInTheDestination()
    {
        //Arrange
        using var host = new TempDirectory();
        var destination = Path.Combine(host.Path, "out");
        using var archive = BuildArchive(("app/", null), ("app/a.txt", "alpha"), ("app/sub/", null),
            ("app/sub/b.txt", "beta"));

        //Act
        var result = await ContainerArchiveExtractor.ExtractAsync(archive, "/srv/app", destination,
            new CopyFromContainerOptions { StripRootFolder = true }, TestContext.Current.CancellationToken);

        //Assert
        File.ReadAllText(Path.Combine(destination, "a.txt")).Should().Be("alpha");
        File.ReadAllText(Path.Combine(destination, "sub", "b.txt")).Should().Be("beta");
        Directory.Exists(Path.Combine(destination, "app")).Should().BeFalse();
        result.FileCount.Should().Be(2);
        result.DirectoryCount.Should().Be(1);
        result.Entries.Select(entry => entry.ContainerPath).Should()
            .BeEquivalentTo(["/srv/app/a.txt", "/srv/app/sub", "/srv/app/sub/b.txt"]);
    }

    [Fact]
    public async Task ExtractAsync_WithOverwriteOff_LeavesExistingFilesAndReportsThem()
    {
        //Arrange
        using var host = new TempDirectory();
        var destination = Path.Combine(host.Path, "out");
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, "a.txt"), "original");
        using var archive = BuildArchive(("a.txt", "replacement"), ("b.txt", "new"));

        //Act
        var result = await ContainerArchiveExtractor.ExtractAsync(archive, "/data", destination,
            new CopyFromContainerOptions { Overwrite = false }, TestContext.Current.CancellationToken);

        //Assert
        File.ReadAllText(Path.Combine(destination, "a.txt")).Should().Be("original");
        File.ReadAllText(Path.Combine(destination, "b.txt")).Should().Be("new");
        result.SkippedEntries.Should().Equal(["a.txt"]);
    }

    [Fact]
    public async Task ExtractAsync_WhenCancelledInsideAFile_DeletesThatFileAndKeepsFinishedOnes()
    {
        //Arrange
        using var host = new TempDirectory();
        var destination = Path.Combine(host.Path, "out");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var archive = new MemoryStream();
        await using (var writer = new TarWriter(archive, TarEntryFormat.Pax, leaveOpen: true))
        {
            await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.RegularFile, "first.bin")
            {
                DataStream = new MemoryStream(new byte[4096]),
            }, TestContext.Current.CancellationToken);
            await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.RegularFile, "second.bin")
            {
                DataStream = new MemoryStream(new byte[8 * 1024 * 1024]),
            }, TestContext.Current.CancellationToken);
        }

        archive.Position = 0;
        using var tripwire = new CancelAfterBytesStream(archive, 1024 * 1024, cancellation);

        //Act
        Func<Task> extract = () => ContainerArchiveExtractor.ExtractAsync(tripwire, "/data", destination, null,
            cancellation.Token);

        //Assert
        await extract.Should().ThrowAsync<OperationCanceledException>();
        File.Exists(Path.Combine(destination, "first.bin")).Should().BeTrue();
        File.Exists(Path.Combine(destination, "second.bin")).Should().BeFalse();
    }

    private static MemoryStream BuildArchive(params (string Name, string Content)[] entries)
    {
        var archive = new MemoryStream();
        using (var writer = new TarWriter(archive, TarEntryFormat.Pax, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = content is null
                    ? new PaxTarEntry(TarEntryType.Directory, name)
                    : new PaxTarEntry(TarEntryType.RegularFile, name)
                    {
                        DataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content)),
                    };
                writer.WriteEntry(entry);
            }
        }

        archive.Position = 0;
        return archive;
    }
}
