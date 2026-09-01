using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CodeBrix.Docker.Tests;

[Collection(DockerTestCollection.Name)]
public sealed class AdvisorTests(DockerTestFixture fixture)
{
    private DockerClient Client => fixture.Client;

    [Fact]
    public async Task AnalyzeContainerAsync_ForAnUnconfiguredContainer_ReportsTheExpectedRules()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));

        // A CPU quota and nothing else: no memory limit, no PID limit, no healthcheck, root user and a
        // mutable image tag. The quota is what lets the kernel record throttling for CB005.
        var spec = fixture.Spec("advisorbad", "busybox:latest", "sh", "-c", StatsTests.BusyLoop);
        spec.Limits = new ResourceLimits { Cpus = 0.1 };
        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(spec, cancellation.Token);
            var containerId = id;

            var findings = await Poll.UntilAsync(
                token => Client.Advisor.AnalyzeContainerAsync(containerId, token),
                result => result.Any(finding => finding.RuleId == "CB005"),
                TimeSpan.FromSeconds(90), "the throttling rule to observe the capped busy loop",
                TimeSpan.FromSeconds(2), cancellation.Token);

            var ruleIds = findings.Select(finding => finding.RuleId).ToArray();

            Assert.Contains("CB001", ruleIds);
            Assert.Contains("CB003", ruleIds);
            Assert.Contains("CB005", ruleIds);
            Assert.Contains("CB007", ruleIds);
            Assert.Contains("CB008", ruleIds);
            Assert.Contains("CB014", ruleIds);

            // A CPU quota is set, so the "no CPU limit" rule must stay quiet.
            Assert.DoesNotContain("CB004", ruleIds);
            // Swap and reservation rules only apply once a memory limit exists.
            Assert.DoesNotContain("CB002", ruleIds);
            Assert.DoesNotContain("CB009", ruleIds);
            Assert.DoesNotContain("CB010", ruleIds);
            Assert.DoesNotContain("CB012", ruleIds);

            Assert.All(findings, finding =>
            {
                Assert.Equal(spec.Name, finding.ContainerName);
                Assert.DoesNotContain('/', finding.ContainerName);
                Assert.NotEmpty(finding.Title);
                Assert.NotEmpty(finding.Detail);
                Assert.NotEmpty(finding.Recommendation);
            });

            for (var i = 1; i < findings.Count; i++)
            {
                var previous = findings[i - 1];
                var current = findings[i];
                Assert.True(previous.Severity > current.Severity
                            || (previous.Severity == current.Severity
                                && string.CompareOrdinal(previous.RuleId, current.RuleId) < 0),
                    $"Findings must be sorted by descending severity then rule id, but {previous.RuleId} " +
                    $"({previous.Severity}) preceded {current.RuleId} ({current.Severity}).");
            }

            var throttling = findings.Single(finding => finding.RuleId == "CB005");
            Assert.Equal(AdvisorSeverity.Critical, throttling.Severity);
            Assert.Contains("CB005", AdvisorEngine.RuleIds);
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task AnalyzeContainerAsync_ForAPrivilegedContainer_ReportsACriticalFinding()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var spec = fixture.Spec("advisorpriv", "busybox:latest", "sleep", "300");
        spec.Privileged = true;
        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(spec, cancellation.Token);

            var findings = await Client.Advisor.AnalyzeContainerAsync(id, cancellation.Token);

            var privileged = Assert.Single(findings, finding => finding.RuleId == "CB010");
            Assert.Equal(AdvisorSeverity.Critical, privileged.Severity);
            Assert.Equal(spec.Name, privileged.ContainerName);
            Assert.Contains("Privileged", privileged.Detail, StringComparison.OrdinalIgnoreCase);

            // Critical findings sort first.
            Assert.Equal("CB010", findings[0].RuleId);
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task AnalyzeContainerAsync_ForJsonFileLogsWithoutASizeCap_ReportsUnboundedLogs()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var uncapped = fixture.Spec("advisorlogs", "busybox:latest", "sleep", "300");
        uncapped.LogDriver = "json-file";
        var capped = fixture.Spec("advisorlogscap", "busybox:latest", "sleep", "300");
        capped.LogDriver = "json-file";
        capped.LogOptions["max-size"] = "10m";
        capped.LogOptions["max-file"] = "3";
        string uncappedId = null;
        string cappedId = null;

        try
        {
            uncappedId = await Client.Containers.RunAsync(uncapped, cancellation.Token);
            cappedId = await Client.Containers.RunAsync(capped, cancellation.Token);

            var uncappedFindings = await Client.Advisor.AnalyzeContainerAsync(uncappedId, cancellation.Token);
            var cappedFindings = await Client.Advisor.AnalyzeContainerAsync(cappedId, cancellation.Token);

            var finding = Assert.Single(uncappedFindings, item => item.RuleId == "CB011");
            Assert.Equal(AdvisorSeverity.Warning, finding.Severity);
            Assert.Contains("max-size", finding.Recommendation, StringComparison.Ordinal);

            Assert.DoesNotContain("CB011", cappedFindings.Select(item => item.RuleId));
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(uncappedId);
            await fixture.RemoveContainerQuietlyAsync(cappedId);
        }
    }

    [Fact]
    public async Task AnalyzeContainerAsync_ForAnOomKilledContainer_ReportsThePreviousKill()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var spec = OomSpecs.MemoryHog(fixture);
        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(spec, cancellation.Token);
            await Client.Containers.WaitForExitAsync(id, cancellation.Token);

            var findings = await Client.Advisor.AnalyzeContainerAsync(id, cancellation.Token);

            var oom = Assert.Single(findings, finding => finding.RuleId == "CB012");
            Assert.Equal(AdvisorSeverity.Warning, oom.Severity);
            Assert.Equal(spec.Name, oom.ContainerName);
            Assert.Contains("OOM", oom.Title, StringComparison.OrdinalIgnoreCase);

            // Rules that need live counters are skipped, not failed, for a stopped container.
            var ruleIds = findings.Select(finding => finding.RuleId).ToArray();
            Assert.DoesNotContain("CB005", ruleIds);
            Assert.DoesNotContain("CB006", ruleIds);
            Assert.DoesNotContain("CB013", ruleIds);
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task AnalyzeContainerAsync_ForAWellConfiguredContainer_ReportsNothing()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var spec = fixture.Spec("advisorgood", "alpine:3.19", "sleep", "300");
        spec.User = "nobody";
        spec.LogDriver = "json-file";
        spec.LogOptions["max-size"] = "10m";
        spec.LogOptions["max-file"] = "3";
        spec.Healthcheck = new HealthcheckSpec
        {
            Test = ["CMD", "true"],
            Interval = TimeSpan.FromSeconds(30),
            Timeout = TimeSpan.FromSeconds(5),
            Retries = 3,
        };
        spec.Limits = new ResourceLimits
        {
            Cpus = 0.5,
            MemoryBytes = ResourceLimits.Megabytes(128),
            MemorySwapBytes = ResourceLimits.Megabytes(128),
            MemoryReservationBytes = ResourceLimits.Megabytes(96),
            PidsLimit = 128,
        };
        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(spec, cancellation.Token);

            var findings = await Client.Advisor.AnalyzeContainerAsync(id, cancellation.Token);

            Assert.Empty(findings);
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task AnalyzeAllContainersAsync_CoversStoppedContainersToo()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var spec = fixture.Spec("advisorall", "busybox:latest", "sh", "-c", "exit 0");
        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(spec, cancellation.Token);
            await Client.Containers.WaitForExitAsync(id, cancellation.Token);

            var findings = await Client.Advisor.AnalyzeAllContainersAsync(cancellation.Token);

            var mine = findings.Where(finding => finding.ContainerName == spec.Name).ToArray();
            Assert.NotEmpty(mine);
            Assert.Contains("CB001", mine.Select(finding => finding.RuleId));
            Assert.All(findings, finding => Assert.DoesNotContain('/', finding.ContainerName));
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }
}
