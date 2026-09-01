using SilverAssertions;
using Xunit;

namespace CodeBrix.Docker.Tests;

/// <summary>
/// Wire-level tests for <see cref="PidsStats"/>, and in particular for the converter that turns the
/// daemon's "no PID limit" sentinel into <see langword="null"/>.
/// </summary>
/// <remarks>
/// These do not need a daemon, and deliberately so. Whether a live container reports the sentinel at
/// all depends on the daemon's cgroup driver: the systemd driver runs each container as a systemd
/// scope that inherits a concrete <c>TasksMax</c>, so on such a host no live container ever produces
/// the unlimited value and the converter would otherwise go untested. Feeding the payload in directly
/// keeps the behaviour fenced on every machine.
/// </remarks>
public sealed class PidsStatsTests
{
    [Fact]
    public void Deserialize_WithTheUnsignedUnlimitedSentinel_ReportsNoLimit()
    {
        //Arrange
        // ulong.MaxValue is what the daemon sends for cgroup v2's pids.max value "max". It does not
        // fit in a long, so the naive mapping would throw or overflow.
        const string json = """{"current":1,"limit":18446744073709551615}""";

        //Act
        var stats = DockerJson.Deserialize<PidsStats>(json);

        //Assert
        stats.Should().NotBeNull();
        stats.Current.Should().Be(1);
        stats.Limit.Should().BeNull();
    }

    [Fact]
    public void Deserialize_WithAConfiguredLimit_ReportsThatLimit()
    {
        //Arrange
        const string json = """{"current":3,"limit":64}""";

        //Act
        var stats = DockerJson.Deserialize<PidsStats>(json);

        //Assert
        stats.Current.Should().Be(3);
        stats.Limit.Should().Be(64);
    }

    [Fact]
    public void Deserialize_WithTheHostWideCeilingFromTheSystemdDriver_ReportsThatCeiling()
    {
        //Arrange
        // The systemd cgroup driver gives an otherwise unconfigured container the scope's inherited
        // TasksMax, which is a large but perfectly ordinary number rather than the sentinel.
        const string json = """{"current":1,"limit":76464}""";

        //Act
        var stats = DockerJson.Deserialize<PidsStats>(json);

        //Assert
        stats.Limit.Should().Be(76464);
    }

    [Fact]
    public void Deserialize_WithAMissingLimit_ReportsNoLimit()
    {
        //Arrange
        const string json = """{"current":1}""";

        //Act
        var stats = DockerJson.Deserialize<PidsStats>(json);

        //Assert
        stats.Limit.Should().BeNull();
    }

    [Fact]
    public void Deserialize_WithANullLimit_ReportsNoLimit()
    {
        //Arrange
        const string json = """{"current":1,"limit":null}""";

        //Act
        var stats = DockerJson.Deserialize<PidsStats>(json);

        //Assert
        stats.Limit.Should().BeNull();
    }
}
