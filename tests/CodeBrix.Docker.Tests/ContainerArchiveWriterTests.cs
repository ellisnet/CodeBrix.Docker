using System.IO;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Docker.Tests;

/// <summary>
/// Daemon-free tests for the archive-building rules: exclusion matching and mode selection.
/// </summary>
public sealed class ContainerArchiveWriterTests
{
    private const UnixFileMode Mode600 = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private const UnixFileMode Mode700 = Mode600 | UnixFileMode.UserExecute;

    [Theory]
    [InlineData("target", "target", true, true)]
    [InlineData("target", "crates/core/target", true, true)]
    [InlineData("target", "targets", true, false)]
    [InlineData("target/", "target", false, false)]
    [InlineData("*.log", "logs/run.log", false, true)]
    [InlineData("build/output", "build/output", true, true)]
    [InlineData("build/output", "src/build/output", true, false)]
    [InlineData("docs/**/*.tmp", "docs/a/b/c.tmp", false, true)]
    [InlineData("docs/**/*.tmp", "docs/c.tmp", false, true)]
    [InlineData("node_?odules", "web/node_modules", true, true)]
    [InlineData(".git", ".github", true, false)]
    public void IsExcluded_MatchesGlobPatternsAgainstRelativePaths(string pattern, string path, bool isDirectory,
        bool expected)
    {
        //Arrange
        var matcher = new ExcludeMatcher([pattern]);

        //Act
        var excluded = matcher.IsExcluded(path, isDirectory);

        //Assert
        excluded.Should().Be(expected);
    }

    [Fact]
    public void ResolveFileMode_WithPreservedModes_UsesTheHostModeVerbatim()
    {
        //Arrange
        var writer = new ContainerArchiveWriter(new CopyToContainerOptions { PreserveUnixModes = true });

        //Act
        var mode = writer.ResolveFileMode(Mode600, "secret.txt");

        //Assert
        mode.Should().Be(Mode600);
    }

    [Fact]
    public void ResolveFileMode_WithoutPreservedModes_UsesTheDefaultsAndRecognisesExecutables()
    {
        //Arrange
        var writer = new ContainerArchiveWriter(new CopyToContainerOptions
        {
            PreserveUnixModes = false,
            ExecutableExtensions = ["sh", ".PS1"],
        });

        //Act
        var plain = writer.ResolveFileMode(Mode600, "notes.txt");
        var hostExecutable = writer.ResolveFileMode(Mode700, "tool");
        var byExtension = writer.ResolveFileMode(null, "build.sh");
        var byUpperCaseExtension = writer.ResolveFileMode(null, "setup.ps1");
        var windowsPlain = writer.ResolveFileMode(null, "readme.md");
        var directory = writer.ResolveDirectoryMode(Mode700);

        //Assert
        plain.Should().Be(CopyToContainerOptions.DefaultFileModeValue);
        hostExecutable.Should().Be(CopyToContainerOptions.DefaultDirectoryModeValue);
        byExtension.Should().Be(CopyToContainerOptions.DefaultDirectoryModeValue);
        byUpperCaseExtension.Should().Be(CopyToContainerOptions.DefaultDirectoryModeValue);
        windowsPlain.Should().Be(CopyToContainerOptions.DefaultFileModeValue);
        directory.Should().Be(CopyToContainerOptions.DefaultDirectoryModeValue);
    }

    [Fact]
    public void ResolveFileMode_WithNoExecutableMode_GivesExecutablesTheDefaultFileMode()
    {
        //Arrange
        var writer = new ContainerArchiveWriter(new CopyToContainerOptions
        {
            PreserveUnixModes = false,
            ExecutableFileMode = null,
        });

        //Act
        var mode = writer.ResolveFileMode(Mode700, "tool");

        //Assert
        mode.Should().Be(CopyToContainerOptions.DefaultFileModeValue);
    }
}
