using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CodeBrix.Docker.Tests;

[Collection(DockerTestCollection.Name)]
public sealed class AnalysisTests(DockerTestFixture fixture)
{
    private const string ScanTarget = "alpine:3.19";

    private const string ProblemDockerfile = """
        FROM python:3.11
        RUN apt-get update
        RUN apt-get install -y vim curl
        RUN pip install flask
        """;

    private DockerClient Client => fixture.Client;

    [Fact]
    public async Task LintDockerfileAsync_ReportsTheRulesABadDockerfileBreaks()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        using var context = new TempDirectory();
        var dockerfile = context.WriteFile("Dockerfile", ProblemDockerfile);

        var result = await Client.Analysis.LintDockerfileAsync(dockerfile, cancellation.Token);

        Assert.Equal(Path.GetFullPath(dockerfile), result.DockerfilePath);
        Assert.True(result.Total > 0, "A deliberately sloppy Dockerfile should produce findings.");

        var codes = result.Findings.Select(finding => finding.Code).ToArray();
        Assert.Contains("DL3008", codes);
        Assert.Contains("DL3013", codes);

        Assert.All(result.Findings, finding =>
        {
            Assert.False(string.IsNullOrWhiteSpace(finding.Code));
            Assert.False(string.IsNullOrWhiteSpace(finding.Level));
            Assert.False(string.IsNullOrWhiteSpace(finding.Message));
            Assert.True(finding.Line > 0, $"{finding.Code} should carry a line number.");
        });

        Assert.NotEmpty(result.CountByLevel);
        Assert.Equal(result.Total, result.CountByLevel.Values.Sum());

        await AssertNoToolContainersRemainAsync(cancellation.Token);
    }

    [Fact]
    public async Task LintDockerfileAsync_ForAMissingFile_ThrowsFileNotFound()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var missing = Path.Combine(Path.GetTempPath(),
            $"{DockerTestFixture.NamePrefix}absent-{Guid.NewGuid():N}", "Dockerfile");

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => Client.Analysis.LintDockerfileAsync(missing, cancellation.Token));
    }

    [Fact]
    public async Task ScanImageAsync_ReportsVulnerabilitiesAndReusesItsDatabaseCache()
    {
        // The first scan may have to download the vulnerability database.
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(20));

        var first = await Client.Analysis.ScanImageAsync(ScanTarget, cancellationToken: cancellation.Token);

        Assert.Equal(ScanTarget, first.ImageReference);
        Assert.True(first.Total > 0, $"Expected findings for {ScanTarget}.");
        Assert.NotEmpty(first.CountBySeverity);
        Assert.Equal(first.Total, first.CountBySeverity.Values.Sum());
        Assert.All(first.Vulnerabilities, vulnerability =>
        {
            Assert.False(string.IsNullOrWhiteSpace(vulnerability.Id));
            Assert.False(string.IsNullOrWhiteSpace(vulnerability.Severity));
            Assert.False(string.IsNullOrWhiteSpace(vulnerability.PkgName));
        });
        Assert.Contains(first.CountBySeverity.Keys,
            severity => severity is "LOW" or "MEDIUM" or "HIGH" or "CRITICAL");
        Assert.Equal(0, first.ExitCode);

        // The cache volume is what keeps a repeat scan cheap.
        var cache = await Client.Volumes.InspectAsync(AnalysisOperations.DefaultTrivyCacheVolumeName,
            cancellation.Token);
        Assert.Equal(AnalysisOperations.ToolLabelValue, cache.Labels?[AnalysisOperations.ToolLabelName]);

        var second = await Client.Analysis.ScanImageAsync(ScanTarget, cancellationToken: cancellation.Token);
        Assert.Equal(first.Total, second.Total);

        await AssertNoToolContainersRemainAsync(cancellation.Token);
    }

    [Fact]
    public async Task ScanImageAsync_WithASeverityFilter_NarrowsTheResults()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(20));

        var all = await Client.Analysis.ScanImageAsync(ScanTarget, cancellationToken: cancellation.Token);
        var high = await Client.Analysis.ScanImageAsync(ScanTarget,
            new TrivyScanOptions { Severities = { "HIGH", "CRITICAL" } }, cancellation.Token);

        Assert.True(high.Total <= all.Total);
        Assert.All(high.Vulnerabilities,
            vulnerability => Assert.Contains(vulnerability.Severity, new[] { "HIGH", "CRITICAL" }));
        Assert.Equal(high.Total, high.CountOf("high") + high.CountOf("critical"));

        await AssertNoToolContainersRemainAsync(cancellation.Token);
    }

    [Fact]
    public async Task AnalyzeImageEfficiencyAsync_ScoresASingleLayerImage()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(15));

        var result = await Client.Analysis.AnalyzeImageEfficiencyAsync(ScanTarget, cancellation.Token);

        Assert.Equal(ScanTarget, result.ImageReference);
        Assert.InRange(result.EfficiencyScore, 0.000001d, 1d);
        Assert.Equal(0, result.WastedBytes);
        Assert.Equal(0d, result.WastedPercent);
        Assert.Single(result.Layers);
        Assert.True(result.Layers[0].SizeBytes > 0);
        Assert.True(result.TotalSizeBytes > 0);

        await AssertNoToolContainersRemainAsync(cancellation.Token);
    }

    private async Task AssertNoToolContainersRemainAsync(CancellationToken cancellationToken)
    {
        var containers = await Client.Containers.ListAsync(all: true, DockerTestFixture.ToolLabelFilter,
            cancellationToken);

        Assert.Empty(containers);
    }
}
