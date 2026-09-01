# CodeBrix.Docker

A cross-platform, zero-dependency .NET library for **managing, diagnosing, and optimizing**
Docker containers and images. Works anywhere Docker runs: Linux (Debian and others), Windows,
and macOS (Docker Desktop), talking to the Docker Engine API over the platform's native
transport — named pipe on Windows, Unix socket elsewhere.

Published on NuGet as **`CodeBrix.Docker.MitLicenseForever`** — MIT licensed, forever.

## Features

**Lifecycle** — create/run/stop/remove containers with typed `ResourceLimits` (CPUs, cpuset,
shares, memory, reservation, swap, PID limits), live resource retuning via `docker update`
semantics, exec, logs, stats, image pull/build (BuildKit via CLI)/tag/history/prune, networks
(incl. aliases), volumes, system info, and event streaming.

**Diagnostics** — the signals that matter for right-sizing containers:
- **CPU throttling reports** (`nr_throttled / nr_periods` ratio with severity and interpretation)
- **OOM-kill detection** (OOMKilled flag, exit code 137, restart counts)
- **Memory breakdown** — application memory (`anon`) vs. reclaimable page cache (`file`), so a
  "high memory" container isn't misdiagnosed
- Health-check monitoring and `WaitForHealthyAsync`

**Optimization advisor** — a rules engine encoding container best practices: missing memory/PID
limits, swap not disabled, throttling too high, running as root, missing health checks,
unbounded log growth, `:latest` tags, privileged mode, and more.

**Image analysis** — runs [Trivy](https://github.com/aquasecurity/trivy) (CVE scanning),
[Dive](https://github.com/wagoodman/dive) (layer efficiency), and
[Hadolint](https://github.com/hadolint/hadolint) (Dockerfile linting) *as containers* — no local
tool installation required. Experimental [Slim](https://github.com/slimtoolkit/slim) integration
for automatic image minification.

## Quick start

```csharp
using CodeBrix.Docker;

using var docker = DockerClient.Create();

// Run a container with resource limits
var id = await docker.Containers.RunAsync(new ContainerSpec
{
    Image = "nginx:alpine",
    Name = "web",
    Limits = new ResourceLimits
    {
        Cpus = 0.5,
        MemoryBytes = ResourceLimits.Megabytes(256),
        MemorySwapBytes = ResourceLimits.Megabytes(256), // disable swap
        PidsLimit = 200,
    },
});

// Diagnose it
var report = await docker.Diagnostics.DiagnoseAsync(id);
Console.WriteLine(report.CpuThrottling.Interpretation);

// Get optimization advice
foreach (var finding in await docker.Advisor.AnalyzeContainerAsync(id))
    Console.WriteLine($"[{finding.Severity}] {finding.Title}: {finding.Recommendation}");

// Scan the image for vulnerabilities
var scan = await docker.Analysis.ScanImageAsync("nginx:alpine");
Console.WriteLine($"{scan.Total} vulnerabilities ({scan.CountBySeverity})");
```

## Requirements

- .NET 10.0+
- A reachable Docker daemon (Linux containers mode). Default endpoints:
  `npipe://./pipe/docker_engine` (Windows), `unix:///var/run/docker.sock` (Linux/macOS);
  `DOCKER_HOST` is honored.
- The `docker` CLI on PATH for image builds (BuildKit) and authenticated pulls.

## License

MIT — see [LICENSE](LICENSE). Contains code adapted from
[Docker.DotNet](https://github.com/dotnet/Docker.DotNet) (MIT, .NET Foundation and
Contributors) — see [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
