# CodeBrix.Docker

A cross-platform, zero-dependency .NET library for **managing, diagnosing, and optimizing**
Docker containers and images. It speaks the Docker Engine API directly over the daemon's own
transport - a Unix domain socket, a Windows named pipe, a TCP endpoint, or an SSH tunnel to a
remote host - and gives you a typed, async-only object model over the whole thing. It works
anywhere Docker runs: Linux (Debian and others), Windows, and macOS (Docker Desktop).
CodeBrix.Docker is provided as a .NET 10 library and associated `CodeBrix.Docker.MitLicenseForever`
NuGet package.

CodeBrix.Docker supports applications and assemblies that target Microsoft .NET version 10.0 and later.
Microsoft .NET version 10.0 is a Long-Term Supported (LTS) version of .NET, and was released on Nov 11, 2025; and will be actively supported by Microsoft until Nov 14, 2028.
Please update your C#/.NET code and projects to the latest LTS version of Microsoft .NET.

## Installation

```
dotnet add package CodeBrix.Docker.MitLicenseForever
```

Note that the NuGet package ID and the namespace are different - there is no package named plain `CodeBrix.Docker`:

* NuGet package ID: `CodeBrix.Docker.MitLicenseForever`
* Assembly and primary namespace: `CodeBrix.Docker` - i.e. `using CodeBrix.Docker;`

The `.MitLicenseForever` suffix belongs to the package ID only; it never appears in a namespace, a
using directive or a type name. The library declares exactly one namespace, and every public type
lives in it - the folders you see in the repository are file organization, not namespaces.

XML documentation (IntelliSense) ships alongside the assembly.

The package has no NuGet dependencies: its dependency group is empty and it pulls in nothing but
the .NET runtime. All JSON goes through the in-box `System.Text.Json`, and the `ssh://` transport
runs the operating system's own SSH client rather than referencing an SSH library.

## CodeBrix.Docker supports:

* Container lifecycle - create, run, start, stop, restart, remove, wait, list and inspect
* Typed `ResourceLimits` - CPUs, cpuset, CPU shares, memory, memory reservation, swap and PID limits - set at creation and retuned while the container runs
* Command execution inside a running container: one-shot exec, or a live interactive session with standard input, a pseudo-terminal and resize
* Log retrieval with the Docker stream framing already decoded, and live statistics as a single sample or a stream
* Image pull with progress, build (BuildKit via the CLI), tag, inspect, history, list, remove and prune
* Networks (including aliases), volumes, daemon information and disk usage, and the daemon's event stream
* Local and remote daemons: `unix://`, `npipe://`, `tcp://`/`http://` and `ssh://` endpoints, with `DOCKER_HOST` honored
* CPU throttling reports - the `nr_throttled / nr_periods` ratio with a severity band and a plain-English interpretation
* OOM-kill detection - the OOMKilled flag, exit code 137, and restart counts
* Memory breakdown - application memory (`anon`) against reclaimable page cache (`file`), so a "high memory" container is not misdiagnosed
* Health-check monitoring, including `WaitForHealthyAsync`
* An optimization advisor: a rules engine encoding container best practices - missing memory and PID limits, swap not disabled, throttling too high, running as root, missing health checks, unbounded log growth, unpinned image references, privileged mode, and more
* Image analysis that runs [Trivy](https://github.com/aquasecurity/trivy) (CVE scanning), [Dive](https://github.com/wagoodman/dive) (layer efficiency) and [Hadolint](https://github.com/hadolint/hadolint) (Dockerfile linting) *as containers* - no local tool installation required
* Experimental image minification through the containerized [mint](https://github.com/mintoolkit/mint) optimizer
* An async-only API: every public operation returns `Task`, `Task<T>` or `IAsyncEnumerable<T>` and takes a `CancellationToken` as its last parameter

## Requirements

* A reachable Docker daemon, in Linux-containers mode. The endpoint is resolved from
  `DockerClientOptions.Endpoint`, then `DOCKER_HOST`, then the platform default -
  `npipe://./pipe/docker_engine` on Windows and `unix:///var/run/docker.sock` elsewhere.
  TLS-secured `https://` endpoints are not supported; use `ssh://` to reach a remote daemon.
* The `docker` command-line tool on PATH, but only for four operations: `Images.BuildAsync`
  (BuildKit builds go through the CLI), `Images.PullAsync` *when* an anonymous pull is refused
  and a credential helper is needed, and the `docker cp` steps inside
  `Analysis.AnalyzeImageEfficiencyAsync` and `Analysis.LintDockerfileAsync`. Everything else is
  pure Engine API and needs no CLI at all. Point `DockerClientOptions.DockerCliPath` at a
  different executable when `docker` is not on PATH.
* For an `ssh://` endpoint only: an SSH client on PATH, key-based authentication, the remote host
  key already in a `known_hosts` file, and the `docker` CLI installed on the remote host.
* For the image-analysis operations only: outbound network access the first time, because the
  tool images are pulled on demand.

## Sample Code

### Connect to the Daemon and Report What It Is

```csharp
using CodeBrix.Docker;

using var client = DockerClient.Create();

if (!await client.System.PingAsync())
{
    Console.WriteLine("The Docker daemon is not reachable.");
    return;
}

var version = await client.System.GetVersionAsync();
var info = await client.System.GetInfoAsync();

Console.WriteLine($"Docker {version.Version} (API {version.ApiVersion}) on {version.Os}/{version.Arch}");
Console.WriteLine($"Host {info.Name}: {info.NCpu} CPUs, {info.MemTotal / (1024 * 1024)} MB");
Console.WriteLine($"Containers: {info.ContainersRunning} running of {info.Containers}; images: {info.Images}");
```

### Run a Container with Typed Resource Limits

```csharp
using CodeBrix.Docker;

using var client = DockerClient.Create();

// CreateAsync and RunAsync do not pull a missing image; pull it first.
await client.Images.PullAsync("nginx:alpine");

var id = await client.Containers.RunAsync(new ContainerSpec
{
    Image = "nginx:alpine",
    Name = "web",
    Limits = new ResourceLimits
    {
        Cpus = 0.5,
        MemoryBytes = ResourceLimits.Megabytes(256),
        MemorySwapBytes = ResourceLimits.Megabytes(256), // == MemoryBytes disables swap
        PidsLimit = 200,
    },
});

// Retune the limits while it runs.
await client.Containers.UpdateResourcesAsync(id, new ResourceLimits { Cpus = 1.0 });
```

### Diagnose a Container

```csharp
using CodeBrix.Docker;

using var client = DockerClient.Create();

// 'id' is a container id or name - for example the one RunAsync returned above.
var report = await client.Diagnostics.DiagnoseAsync(id);

Console.WriteLine(report.Summary);
Console.WriteLine(report.CpuThrottling.Interpretation);
Console.WriteLine(report.Memory.Interpretation);
Console.WriteLine(report.Oom.Interpretation);
Console.WriteLine(report.Health.Interpretation);
```

Every report carries the raw counters *and* a plain-English `Interpretation` sentence, so a
container that looks memory-hungry because of reclaimable page cache is described as exactly that.

### Get Optimization Advice

```csharp
using System.Linq;
using CodeBrix.Docker;

using var client = DockerClient.Create();

var findings = await client.Advisor.AnalyzeContainerAsync(id);

foreach (var f in findings.OrderByDescending(x => x.Severity))
{
    Console.WriteLine($"[{f.Severity}] {f.RuleId} {f.Title}");
    Console.WriteLine("        " + f.Recommendation);
}

Console.WriteLine("rules shipped: " + string.Join(", ", AdvisorEngine.RuleIds));
```

### Scan an Image for Vulnerabilities

```csharp
using System.Linq;
using CodeBrix.Docker;

using var client = DockerClient.Create();

var scan = await client.Analysis.ScanImageAsync("nginx:alpine", new TrivyScanOptions
{
    Severities = { "HIGH", "CRITICAL" },
    IgnoreUnfixed = false,
});

Console.WriteLine($"{scan.ImageReference}: {scan.Total} vulnerabilit(ies), " +
                  $"{scan.CountOf("CRITICAL")} critical, {scan.CountOf("HIGH")} high");

foreach (var v in scan.Vulnerabilities.Take(3))
{
    Console.WriteLine($"  {v.Severity,-8} {v.Id} in {v.PkgName} {v.InstalledVersion}" +
                      (v.HasFix ? $" -> fixed in {v.FixedVersion}" : " (no fix yet)"));
}
```

`TrivyScanResult.CountBySeverity` is a dictionary keyed by Trivy's uppercase severity names; use
`CountOf(severity)` to read one of them without worrying about absent keys.

### Reach a Remote Daemon over SSH

```csharp
using CodeBrix.Docker;

using var remote = DockerClient.Create(new DockerClientOptions
{
    Endpoint = "ssh://root@build-01:2222",
    SshArguments =
    {
        "-i", "/keys/deploy",
        "-o", "IdentitiesOnly=yes",
        "-o", "UserKnownHostsFile=/etc/docker/known_hosts",
    },
});

Console.WriteLine("ping: " + await remote.System.PingAsync());

var containers = await remote.Containers.ListAsync();
Console.WriteLine($"running: {containers.Count} container(s) on the remote daemon");
```

Everything works over `ssh://` exactly as it does locally, including interactive exec, standard
input and its half-close. The same thing with no options at all is
`DOCKER_HOST=ssh://root@build-01:2222` plus a plain `DockerClient.Create()`.

## Documentation

The NuGet package includes `AGENT-README.txt`, a complete API reference and usage guide written for AI coding agents - point your agent at that file when it is writing code against this library.

Additional sample code and usage examples are available in the `CodeBrix.Docker.Tests` project:
https://github.com/ellisnet/CodeBrix.Docker/tree/main/tests/CodeBrix.Docker.Tests

## License

CodeBrix.Docker is licensed under the MIT License - see the
[LICENSE](https://github.com/ellisnet/CodeBrix.Docker/blob/main/LICENSE) file.

For licensing and provenance information about the open source code included in
this package, see [THIRD-PARTY-NOTICES.txt](https://github.com/ellisnet/CodeBrix.Docker/blob/main/THIRD-PARTY-NOTICES.txt).
