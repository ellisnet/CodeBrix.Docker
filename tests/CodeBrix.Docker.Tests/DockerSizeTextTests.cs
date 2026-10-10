using SilverAssertions;
using Xunit;

namespace CodeBrix.Docker.Tests;

/// <summary>
/// Daemon-free tests for <see cref="DockerSizeText"/>: the parser for the human-readable sizes the docker
/// CLI prints, and the lookup of the <c>Total:</c> line <c>docker builder prune</c> ends with.
/// </summary>
public sealed class DockerSizeTextTests
{
    [Theory]
    [InlineData("0B", 0L)]
    [InlineData("512B", 512L)]
    [InlineData("1.5kB", 1500L)]
    [InlineData("14.56GB", 14_560_000_000L)]
    [InlineData("3MB", 3_000_000L)]
    [InlineData("2TB", 2_000_000_000_000L)]
    [InlineData("2GiB", 2_147_483_648L)]
    [InlineData("1KiB", 1024L)]
    [InlineData("1.5MiB", 1_572_864L)]
    [InlineData(" 7 kB ", 7000L)]
    public void ParseBytes_ForADockerSize_ReturnsDecimalOrBinaryBytes(string text, long expected)
    {
        //Arrange
        // kB/MB/GB/TB are 1000-based, KiB/MiB/GiB are 1024-based, B is bytes.

        //Act
        var bytes = DockerSizeText.ParseBytes(text);

        //Assert
        bytes.Should().Be(expected);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("GB")]
    [InlineData("12")]
    [InlineData("12XB")]
    [InlineData("1.2.3GB")]
    [InlineData("-5MB")]
    [InlineData("")]
    [InlineData(null)]
    public void ParseBytes_ForTextThatIsNotASize_ReturnsNull(string text)
    {
        //Arrange
        // A missing unit, an unknown unit, a malformed number or nothing at all.

        //Act
        var bytes = DockerSizeText.ParseBytes(text);

        //Assert
        bytes.Should().BeNull();
    }

    [Fact]
    public void FindTotal_ForBuilderPruneOutput_ReturnsTheLastTotalValue()
    {
        //Arrange
        const string output = "Flag --keep-storage has been deprecated\n" +
                              "ID\t\t\t\t\t\tRECLAIMABLE\tSIZE\t\tLAST ACCESSED\n" +
                              "abc123\t\t\t\t\t\ttrue \t\t1.2GB\t\t3 days ago\n" +
                              "Total:\t14.56GB\n";

        //Act
        var total = DockerSizeText.FindTotal(output);

        //Assert
        total.Should().Be("14.56GB");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("ERROR: something else entirely\n")]
    public void FindTotal_WhenThereIsNoTotalLine_ReturnsNullAndTheBytesAreNullToo(string output)
    {
        //Arrange

        //Act
        var total = DockerSizeText.FindTotal(output);
        var bytes = DockerSizeText.ParseBytes(total);

        //Assert
        total.Should().BeNull();
        bytes.Should().BeNull();
    }
}
