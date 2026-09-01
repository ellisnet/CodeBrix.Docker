using System.Collections.Generic;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace RedisSetupTool.DockerManagement.Tests;

/// <summary>Diagnostics and advisor operations against the live daemon.</summary>
[Collection(RedisSetupToolCollection.Name)]
public class DockerManagerDiagnosticsTests
{
    private readonly RedisSetupToolFixture _fixture;

    /// <summary>Creates the test class.</summary>
    /// <param name="fixture">The shared fixture.</param>
    public DockerManagerDiagnosticsTests(RedisSetupToolFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>A diagnose call returns all four sub-reports with their interpretations.</summary>
    [Fact]
    public async Task DiagnoseAsync_ReturnsAllFourSubReports()
    {
        //Arrange
        var name = RedisSetupToolFixture.NewName("diag");
        var token = TestContext.Current.CancellationToken;
        DockerCli.Run("run", "-d", "--name", name,
            "--label", RedisSetupToolFixture.TestLabelName + "="
                + RedisSetupToolFixture.TestLabelValue,
            "--memory", "64m", "--memory-swap", "64m", "--cpus", "0.5", "--pids-limit", "128",
            "alpine:latest", "sleep", "300");

        try
        {
            //Act
            var report = await _fixture.Docker.DiagnoseAsync(name, token);
            var cpu = await _fixture.Docker.GetCpuThrottlingAsync(name, token);
            var memory = await _fixture.Docker.GetMemoryBreakdownAsync(name, token);
            var oom = await _fixture.Docker.CheckOomAsync(name, token);
            var health = await _fixture.Docker.GetHealthAsync(name, token);

            //Assert
            report.IsRunning.Should().Be(true);
            report.Summary.Should().NotBeNullOrEmpty();
            report.Cpu.Should().NotBeNull();
            report.Memory.Should().NotBeNull();
            report.Oom.Should().NotBeNull();
            report.Health.Should().NotBeNull();

            cpu.Interpretation.Should().NotBeNullOrEmpty();
            memory.LimitBytes.Should().Be(64L * 1024 * 1024);
            oom.WasOomKilled.Should().Be(false);
            health.HasHealthcheck.Should().Be(false);
        }
        finally
        {
            DockerCli.RemoveQuietly(name);
        }
    }

    /// <summary>Every finding names a rule the engine advertises.</summary>
    [Fact]
    public async Task AdviseContainerAsync_ReturnsFindingsFromKnownRules()
    {
        //Arrange
        var name = RedisSetupToolFixture.NewName("advise");
        var token = TestContext.Current.CancellationToken;
        DockerCli.Run("run", "-d", "--name", name,
            "--label", RedisSetupToolFixture.TestLabelName + "="
                + RedisSetupToolFixture.TestLabelValue,
            "alpine:latest", "sleep", "300");

        try
        {
            //Act
            var findings = await _fixture.Docker.AdviseContainerAsync(name, token);

            //Assert
            findings.Count.Should().BeGreaterThan(0);
            var ruleIds = new List<string>(_fixture.Docker.AdvisorRuleIds);
            foreach (var finding in findings)
            {
                ruleIds.Should().Contain(finding.RuleId);
                finding.Recommendation.Should().NotBeNullOrEmpty();
            }

            //An unconstrained container has no healthcheck, which is rule CB007.
            var codes = new List<string>();
            foreach (var finding in findings)
            {
                codes.Add(finding.RuleId);
            }

            codes.Should().Contain("CB007");
        }
        finally
        {
            DockerCli.RemoveQuietly(name);
        }
    }
}
