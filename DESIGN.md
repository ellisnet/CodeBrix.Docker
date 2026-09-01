# CodeBrix.Docker — Design Contract

**This document is the authoritative API and architecture contract.** Every agent working on
this codebase reads it first and conforms to it. If an agent must deviate (e.g., a Docker API
reality makes a signature impossible), it documents the deviation at the bottom of this file
under "Deviations".

## What this library is

A cross-platform (Windows/Linux/macOS), zero-dependency .NET library for managing, diagnosing,
and **optimizing** Docker containers and images.

Three tiers:
1. **Lifecycle** — containers, images, networks, volumes, system info/events, typed resource limits.
2. **Diagnostics** — CPU throttling detection, OOM-kill detection, memory breakdown (app memory
   vs. reclaimable page cache), health monitoring.
3. **Optimization** — an advisor rules engine encoding container best practices, plus image
   analysis by running Trivy / Dive / Hadolint / Slim *as containers* (no local tool installs).

## Hard constraints

- **TargetFramework: `net10.0` only.** LangVersion latest.
- **Nullable reference types are OFF and implicit usings are OFF** — the CodeBrix family convention
  for self-authored libraries. (Sibling CodeBrix libraries that enable NRT do so only as a
  documented situational exception, because they port large amounts of external code that depends
  on it; that does not apply here.) Consequences for all code in this repo:
  - Every file declares the `using` directives it needs explicitly — `using System;` first, then
    the rest alphabetically.
  - No `?` annotations on **reference** types, no null-forgiving `!` operators, no `#nullable`
    directives. `?` on **value** types (`int?`, `long?`, `TimeSpan?`, `DateTimeOffset?`, nullable
    enums) is `Nullable<T>` and is used freely — much of the diagnostics contract depends on it.
  - Nullability of reference types is expressed in XML doc comments and enforced by runtime
    guards, not by the compiler.
- **Zero NuGet dependencies** for the library project. System.Text.Json (in-box) for all JSON.
- **Async-only public API.** Every operation returns `Task`/`Task<T>`/`IAsyncEnumerable<T>` and takes
  `CancellationToken cancellationToken = default` as the last parameter. No sync wrappers.
- **Root namespace `CodeBrix.Docker`**, assembly `CodeBrix.Docker`, PackageId `CodeBrix.Docker.MitLicenseForever`.
- **NEVER run `git commit` or `git push`.** Leave all changes in the working tree.
- Code vendored/adapted from `C:\Temp\Docker.DotNet` (MIT) keeps a file header:
  `// Adapted from Docker.DotNet (https://github.com/dotnet/Docker.DotNet), MIT License, Copyright (c) .NET Foundation and Contributors.`
  Do NOT add a NuGet reference to Docker.DotNet. Prefer writing clean net10-idiomatic code with the
  clone open as reference; literally copy only where the logic is intricate (stream demux, converters).
- XML doc comments on all public types/members (`GenerateDocumentationFile` is on).

## Transport (validated by spike — see `Facts` below)

Endpoint resolution order:
1. Explicit `DockerClientOptions.Endpoint` if set.
2. `DOCKER_HOST` environment variable.
3. Default: `npipe://./pipe/docker_engine` on Windows, `unix:///var/run/docker.sock` otherwise.

Supported schemes: `npipe://` (NamedPipeClientStream), `unix://` (UnixDomainSocketEndPoint),
`tcp://`/`http://` (plain HTTP; TLS/`https` may throw `NotSupportedException` in v1).

Implementation: single `HttpClient` with `SocketsHttpHandler.ConnectCallback` returning the
connected pipe/socket stream. Base address `http://localhost/`. Unversioned API paths (e.g.
`containers/json`), which the daemon treats as latest. `HttpClient.Timeout` = infinite; per-call
timeouts come from cancellation tokens (streaming endpoints must not time out).

## Error model

- `DockerException : Exception` — base.
- `DockerApiException : DockerException` — non-2xx from the daemon. Properties: `HttpStatusCode StatusCode`,
  `string ResponseBody`. Daemon error JSON `{"message": "..."}` parsed into `Message` when present.
- `DockerContainerNotFoundException : DockerApiException` (404 on container endpoints);
  `DockerImageNotFoundException : DockerApiException` (404 on image endpoints).
- `DockerCliException : DockerException` — CLI shell-out failed. Properties: `int ExitCode`, `string StdErr`, `string Command`.

## Project layout (`src/CodeBrix.Docker/`)

```
Client/        DockerClient, DockerClientOptions, internal DockerApiClient (HTTP plumbing)
Transport/     endpoint parsing + ConnectCallback factories
Common/        DockerJson (JsonSerializerOptions + converters), exceptions, QueryStringBuilder
Containers/    ContainerOperations + container DTOs (specs, inspect, stats, summaries), MultiplexedStreamReader
Images/        ImageOperations + image DTOs; ImageBuildOperations (CLI/BuildKit path)
Networks/      NetworkOperations + DTOs
Volumes/       VolumeOperations + DTOs
System/        SystemOperations + DTOs (version, info, disk usage, events)
Diagnostics/   DiagnosticsOperations + report types
Advisor/       AdvisorEngine, IAdvisorRule, AdvisorFinding, rules under Advisor/Rules/
Analysis/      TrivyScanner, DiveAnalyzer, HadolintLinter, SlimOptimizer + result DTOs
Cli/           DockerCliRunner (Process-based shell-out to `docker`)
```

## Public API surface

### Entry point

```csharp
namespace CodeBrix.Docker;

public sealed class DockerClient : IDisposable
{
    public static DockerClient Create(DockerClientOptions? options = null);
    public ContainerOperations Containers { get; }
    public ImageOperations Images { get; }
    public NetworkOperations Networks { get; }
    public VolumeOperations Volumes { get; }
    public SystemOperations System { get; }
    public DiagnosticsOperations Diagnostics { get; }
    public AdvisorEngine Advisor { get; }
    public AnalysisOperations Analysis { get; }
}

public sealed class DockerClientOptions
{
    public string? Endpoint { get; set; }           // null => resolve per Transport section
    public string DockerCliPath { get; set; } = "docker";
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(100); // applied per non-streaming call
}
```

Internal `DockerApiClient` exposes (internal, used by all operation classes):
`Task<T> GetAsync<T>(string path, CancellationToken ct)`,
`Task<string> GetStringAsync(...)`, `Task<T> PostAsync<T>(string path, object? body, ...)`,
`Task PostAsync(...)`, `Task DeleteAsync(...)`,
`Task<Stream> GetStreamAsync(string path, CancellationToken ct)` (for logs/stats-stream/events/exec),
`IAsyncEnumerable<T> GetJsonLinesAsync<T>(string path, CancellationToken ct)` (progress/events streams).
All JSON via `DockerJson.Options` (case-insensitive, ignore-null-on-write, tolerant of unknown fields).

### Containers

```csharp
public sealed class ContainerOperations
{
    Task<IReadOnlyList<ContainerSummary>> ListAsync(bool all = false, IDictionary<string,string>? labelFilters = null, CancellationToken ct = default);
    Task<ContainerInspectResult> InspectAsync(string idOrName, CancellationToken ct = default);
    Task<string> CreateAsync(ContainerSpec spec, CancellationToken ct = default);       // returns container id
    Task StartAsync(string idOrName, CancellationToken ct = default);
    Task<string> RunAsync(ContainerSpec spec, CancellationToken ct = default);          // create + start
    Task StopAsync(string idOrName, int timeoutSeconds = 10, CancellationToken ct = default);
    Task RestartAsync(string idOrName, int timeoutSeconds = 10, CancellationToken ct = default);
    Task KillAsync(string idOrName, string signal = "SIGKILL", CancellationToken ct = default);
    Task RemoveAsync(string idOrName, bool force = false, bool removeVolumes = false, CancellationToken ct = default);
    Task UpdateResourcesAsync(string idOrName, ResourceLimits limits, CancellationToken ct = default);  // POST /containers/{id}/update — live retune
    Task<long> WaitForExitAsync(string idOrName, CancellationToken ct = default);       // returns exit code
    Task<ContainerStats> GetStatsAsync(string idOrName, CancellationToken ct = default);          // one-shot (?stream=false)
    IAsyncEnumerable<ContainerStats> StreamStatsAsync(string idOrName, CancellationToken ct = default);
    Task<ContainerLogs> GetLogsAsync(string idOrName, int? tail = null, bool timestamps = false, CancellationToken ct = default); // demuxed Stdout/Stderr strings
    Task<ExecResult> ExecAsync(string idOrName, IReadOnlyList<string> command, string? user = null, string? workingDir = null, IReadOnlyList<string>? env = null, CancellationToken ct = default);
    Task PruneAsync(IDictionary<string,string>? labelFilters = null, CancellationToken ct = default);
}

public sealed record ExecResult(string Stdout, string Stderr, long ExitCode);
```

`ContainerSpec` (builder-style init-properties, maps to `/containers/create`):
`Image` (required), `Name?`, `Command` (string[]?), `Entrypoint?`, `Env` (list "K=V"), `Labels`
(dict), `User?`, `WorkingDir?`, `HostName?`, `ExposedPorts`/`PortBindings` (list of
`PortBinding(int ContainerPort, int? HostPort, string Protocol = "tcp")`), `Mounts` (list of
`MountSpec` — factory methods `MountSpec.Volume(name, containerPath, readOnly)`,
`MountSpec.Bind(hostPath, containerPath, readOnly)`, `MountSpec.Tmpfs(containerPath, sizeBytes?)`),
`NetworkName?`, `NetworkAliases` (list), `RestartPolicy?` (enum No/Always/OnFailure/UnlessStopped + MaxRetries),
`AutoRemove` (bool), `Privileged` (bool), `Healthcheck?` (`HealthcheckSpec`: Test string[], Interval,
Timeout, StartPeriod, Retries), `LogDriver?` + `LogOptions` (dict), `Limits` (`ResourceLimits?`).

```csharp
public sealed class ResourceLimits
{
    public double? Cpus { get; set; }              // -> NanoCPUs = (long)(Cpus * 1e9)
    public string? CpusetCpus { get; set; }        // "0" or "0,1" or "0-3"
    public long? CpuShares { get; set; }           // relative weight, default 1024
    public long? MemoryBytes { get; set; }
    public long? MemoryReservationBytes { get; set; }
    public long? MemorySwapBytes { get; set; }     // set == MemoryBytes to disable swap
    public long? PidsLimit { get; set; }
    public static long Megabytes(int mb) => mb * 1024L * 1024L;
}
```

`ContainerStats` DTO must surface (nullable — **empty objects come back for exited containers**):
`CpuStats.ThrottlingData` (`Periods`, `ThrottledPeriods`, `ThrottledTime`), `CpuStats.CpuUsage.TotalUsage`,
`PreCpuStats` (for % computation), `MemoryStats.Usage`, `MemoryStats.Limit`, `MemoryStats.Stats`
(`Dictionary<string, long>?` — cgroup v2 keys: anon, file, kernel, slab, pgfault, shmem...),
`PidsStats.Current`, `Networks` dict, `BlkioStats`. Provide computed helpers:
`double? CpuPercent()` (standard delta formula), `double? MemoryPercent()`.

`ContainerInspectResult` must surface: `Id`, `Name`, `State` (`Status`, `Running`, `OOMKilled`,
`ExitCode`, `StartedAt`, `FinishedAt`, `Health?` (`Status`, `FailingStreak`, `Log`)), `Config`
(`Image`, `User`, `Env`, `Labels`, `Healthcheck?`), `HostConfig` (`NanoCpus`, `CpusetCpus`,
`CpuShares`, `Memory`, `MemoryReservation`, `MemorySwap`, `PidsLimit`, `Privileged`, `RestartPolicy`,
`LogConfig` (`Type`, `Config` dict), `NetworkMode`, `Binds`), `RestartCount`, `NetworkSettings.Networks`.

**Log/exec stream demux:** non-TTY logs and exec output use the Docker stdcopy framing —
8-byte header: byte0 = stream (0 stdin, 1 stdout, 2 stderr), bytes 4-7 = big-endian payload length.
Implement `MultiplexedStreamReader` (adapt from Docker.DotNet `MultiplexedStream.cs`).
Exec: `POST /containers/{id}/exec` (AttachStdout/Stderr true, Tty false) then `POST /exec/{id}/start`
with `{"Detach":false,"Tty":false}` — the response body is the multiplexed stream; then
`GET /exec/{id}/json` for `ExitCode`. No stdin/TTY support in v1.

### Images

```csharp
public sealed class ImageOperations
{
    Task<IReadOnlyList<ImageSummary>> ListAsync(bool all = false, CancellationToken ct = default);
    Task<ImageInspectResult> InspectAsync(string reference, CancellationToken ct = default);
    Task PullAsync(string reference, IProgress<string>? progress = null, CancellationToken ct = default); // POST /images/create (anonymous); on 401/denied, fall back to `docker pull` CLI (credential helpers)
    Task RemoveAsync(string reference, bool force = false, CancellationToken ct = default);
    Task TagAsync(string sourceReference, string targetReference, CancellationToken ct = default);
    Task<IReadOnlyList<ImageHistoryEntry>> GetHistoryAsync(string reference, CancellationToken ct = default);
    Task PruneAsync(bool dangling = true, CancellationToken ct = default);
    Task<ImageBuildResult> BuildAsync(ImageBuildSpec spec, CancellationToken ct = default);  // CLI shell-out (BuildKit)
}
```

`ImageBuildSpec`: `ContextDirectory` (required), `DockerfilePath?` (default `<context>/Dockerfile`),
`Tags` (list, required non-empty), `BuildArgs` (dict), `Target?` (multi-stage stage name), `Pull` (bool),
`NoCache` (bool), `Labels` (dict). `ImageBuildResult`: `ImageId`, `Tags`, `Output` (combined build log).
Build runs `docker build` via `DockerCliRunner` — the Engine API `/build` endpoint is the legacy
builder; BuildKit needs the CLI. After build, resolve `ImageId` via inspect of first tag.
`ImageInspectResult` surfaces `Id`, `RepoTags`, `Size`, `Architecture`, `Os`, `Config` (User, Env,
ExposedPorts, Healthcheck?, Labels), `RootFS.Layers` count.

### Networks / Volumes

```csharp
public sealed class NetworkOperations
{
    Task<string> CreateAsync(string name, string driver = "bridge", IDictionary<string,string>? labels = null, CancellationToken ct = default);
    Task<IReadOnlyList<NetworkSummary>> ListAsync(CancellationToken ct = default);
    Task<NetworkInspectResult> InspectAsync(string idOrName, CancellationToken ct = default);
    Task RemoveAsync(string idOrName, CancellationToken ct = default);
    Task ConnectAsync(string network, string container, IReadOnlyList<string>? aliases = null, CancellationToken ct = default);
    Task DisconnectAsync(string network, string container, bool force = false, CancellationToken ct = default);
    Task PruneAsync(CancellationToken ct = default);
}

public sealed class VolumeOperations
{
    Task<string> CreateAsync(string? name = null, IDictionary<string,string>? labels = null, CancellationToken ct = default);
    Task<IReadOnlyList<VolumeSummary>> ListAsync(CancellationToken ct = default);
    Task<VolumeInspectResult> InspectAsync(string name, CancellationToken ct = default);
    Task RemoveAsync(string name, bool force = false, CancellationToken ct = default);
    Task PruneAsync(CancellationToken ct = default);
}
```

### System

```csharp
public sealed class SystemOperations
{
    Task<bool> PingAsync(CancellationToken ct = default);
    Task<DockerVersionInfo> GetVersionAsync(CancellationToken ct = default);   // Version, ApiVersion, Os, Arch, KernelVersion
    Task<DockerSystemInfo> GetInfoAsync(CancellationToken ct = default);       // OSType, ServerVersion, CgroupVersion, CgroupDriver, NCPU, MemTotal, Name
    Task<DiskUsageInfo> GetDiskUsageAsync(CancellationToken ct = default);     // images/containers/volumes size totals
    IAsyncEnumerable<DockerEvent> StreamEventsAsync(CancellationToken ct = default);  // GET /events, JSON-lines
    Task EnsureLinuxDaemonAsync(CancellationToken ct = default);               // throws DockerException if OSType != "linux"
}
```

### Diagnostics (Tier 2 — the differentiator)

```csharp
public sealed class DiagnosticsOperations
{
    Task<CpuThrottlingReport> GetCpuThrottlingAsync(string idOrName, CancellationToken ct = default);
    Task<MemoryBreakdownReport> GetMemoryBreakdownAsync(string idOrName, CancellationToken ct = default);
    Task<OomReport> CheckOomAsync(string idOrName, CancellationToken ct = default);
    Task<HealthReport> GetHealthAsync(string idOrName, CancellationToken ct = default);
    Task WaitForHealthyAsync(string idOrName, TimeSpan timeout, CancellationToken ct = default); // polls inspect; throws TimeoutException
    Task<ContainerDiagnosticsReport> DiagnoseAsync(string idOrName, CancellationToken ct = default); // aggregate of the above
}
```

- `CpuThrottlingReport`: `Periods`, `ThrottledPeriods`, `ThrottledTimeNanos`, `ThrottleRatio`
  (0..1; 0 when Periods==0), `Severity` (None <5%, Moderate 5–25%, High 25–75%, Critical >75%),
  `Interpretation` (human sentence, e.g. "Container was throttled in 99% of scheduling periods —
  the CPU limit is too restrictive for this workload").
- `MemoryBreakdownReport`: `UsageBytes`, `LimitBytes?`, `AnonBytes?` (application memory),
  `FileBytes?` (page cache, reclaimable), `KernelBytes?`, `UsagePercent?`,
  `EffectiveUsagePercent?` (anon/limit), `Interpretation` (flags when usage is dominated by
  reclaimable page cache).
- `OomReport`: `WasOomKilled`, `ExitCode`, `RestartCount`, `FinishedAt?`, `Interpretation`
  (exit 137 + OOMKilled=true → "killed by the kernel OOM killer; raise the memory limit or fix the leak").
- `HealthReport`: `HasHealthcheck`, `Status?` (starting/healthy/unhealthy), `FailingStreak`, last log entries.

### Advisor (Tier 3)

```csharp
public sealed class AdvisorEngine
{
    Task<IReadOnlyList<AdvisorFinding>> AnalyzeContainerAsync(string idOrName, CancellationToken ct = default);
    Task<IReadOnlyList<AdvisorFinding>> AnalyzeAllContainersAsync(CancellationToken ct = default);
}

public sealed record AdvisorFinding(string RuleId, AdvisorSeverity Severity, string ContainerName,
    string Title, string Detail, string Recommendation);
public enum AdvisorSeverity { Info, Warning, Critical }
```

Rules are internal `IAdvisorRule` implementations evaluated against a context of
(`ContainerInspectResult`, `ContainerStats?` — stats only when running). Ship these rules
(IDs stable; each Recommendation cites the concrete flag/spec property to change):

| Id  | Severity | Trigger |
|-----|----------|---------|
| CB001 | Warning | No memory limit set (`HostConfig.Memory == 0`) — noisy-neighbor / host OOM risk |
| CB002 | Warning | Memory limit set but swap not disabled (`MemorySwap != Memory`) — unpredictable perf |
| CB003 | Warning | No PID limit — fork-bomb exposure |
| CB004 | Info | No CPU limit (`NanoCpus == 0` and `CpuShares` in {0,1024}) |
| CB005 | Warning/Critical | Running: throttle ratio > 25% (Warning) / > 75% (Critical) — limit too restrictive |
| CB006 | Warning | Running: anon memory > 90% of limit — OOM-kill imminent |
| CB007 | Warning | No HEALTHCHECK defined (image or container) |
| CB008 | Warning | Running as root (`Config.User` empty or "root"/"0") |
| CB009 | Info | Memory limit set but no reservation; recommend reservation ≈ 70–80% of limit |
| CB010 | Critical | `Privileged == true` |
| CB011 | Warning | json-file log driver without `max-size` — unbounded log growth |
| CB012 | Warning | Container previously OOM-killed (`State.OOMKilled` or exit 137 while exited) |
| CB013 | Info | Running: memory usage dominated by page cache (file > 2×anon and > 50% of usage) — usage number is misleadingly high |
| CB014 | Info | Image referenced by `:latest` or untagged — non-reproducible deploys |

### Analysis (Tier 3 — containerized tools)

```csharp
public sealed class AnalysisOperations
{
    Task<TrivyScanResult> ScanImageAsync(string imageReference, TrivyScanOptions? options = null, CancellationToken ct = default);
    Task<DiveAnalysisResult> AnalyzeImageEfficiencyAsync(string imageReference, CancellationToken ct = default);
    Task<HadolintResult> LintDockerfileAsync(string dockerfilePath, CancellationToken ct = default);
    Task<SlimResult> OptimizeImageAsync(string imageReference, SlimOptions? options = null, CancellationToken ct = default); // EXPERIMENTAL
}
```

All four run their tool as a container via this library's own `ContainerOperations`, mounting the
Docker socket (`/var/run/docker.sock:/var/run/docker.sock` bind — works on Docker Desktop too).
Default images (overridable via options/properties): `aquasec/trivy:latest`, `wagoodman/dive:latest`,
`hadolint/hadolint:latest`, `dslim/slim:latest`. Tool containers get label
`codebrix.docker.tool=true` and are always removed in a `finally`. Capture output via container
logs after `WaitForExitAsync`; non-zero tool exit codes that indicate findings (e.g. trivy
`--exit-code`) are not errors.

- **Trivy**: `image --format json --quiet <ref>`; mount a named cache volume
  `codebrix-docker-trivy-cache` at `/root/.cache/` (labeled) so repeat scans skip the DB download.
  Result: `Vulnerabilities` list (Id, PkgName, InstalledVersion, FixedVersion, Severity, Title) +
  `CountBySeverity` dict + `Total`.
- **Dive**: run with `--json /out/dive.json` writing into a bind-mounted temp directory
  (`Path.GetTempPath()` subdir); also needs env `CI=true`. Parse `EfficiencyScore` (0..1),
  `WastedBytes`, `Layers` (index, size, command). Dive also needs the socket mount.
- **Hadolint**: bind-mount the Dockerfile read-only to `/Dockerfile`, run
  `hadolint --format json --no-fail /Dockerfile` (no socket needed). Result: list of
  (`Code` e.g. DL3008, `Level`, `Line`, `Message`).
- **Slim**: `build --target <ref> --tag <ref>.slim --http-probe=false ...` with socket mount;
  options for HttpProbePaths (when set, enable probing with `--http-probe-cmd`). Mark `[Experimental]`
  in XML docs; long timeout (10 min default).

### CLI runner

```csharp
internal sealed class DockerCliRunner   // Cli/
{
    Task<CliResult> RunAsync(IReadOnlyList<string> args, string? workingDir = null, IProgress<string>? output = null, CancellationToken ct = default);
}
internal sealed record CliResult(int ExitCode, string Stdout, string Stderr);
```
`ProcessStartInfo` with argument list (no shell string concat), UTF-8, both streams captured
concurrently. Used by: `Images.BuildAsync`, `Images.PullAsync` auth fallback. Throw
`DockerCliException` on unexpected non-zero exit.

## Facts validated by spike (2026-08-31, Docker Desktop 29.7.2 / WSL2 / cgroup v2 / API 1.55)

- Named-pipe transport via `SocketsHttpHandler.ConnectCallback` + `NamedPipeClientStream(".", "docker_engine")` works.
- `/containers/{id}/stats?stream=false` returns `cpu_stats.throttling_data` and `memory_stats.stats`
  with cgroup-v2 keys (`anon`, `file`, `slab`, `pgfault`).
- **Stats for an exited container returns EMPTY `cpu_stats`/`memory_stats` objects** — all stats DTO
  fields must be nullable/defaulted; never `GetProperty` unguarded semantics.
- `POST /containers/{id}/update` with `{"NanoCpus": ...}` works live; response `{"Warnings":null}`.
- OOM kill reports correctly: `State.OOMKilled=true`, `ExitCode=137`.
- `docker exec <c> cat /sys/fs/cgroup/cpu.stat` works (cgroup namespaces) — exec fallback viable.

## Testing strategy (`tests/CodeBrix.Docker.Tests/`)

xUnit integration tests against the local daemon, exercising realistic operational scenarios. Rules:

- **Every resource a test creates carries the label `codebrix.docker.tests=true`** (containers,
  networks, volumes, built images via build `--label`).
- A shared `DockerTestFixture` (collection fixture) provides the `DockerClient` and, in
  `DisposeAsync`, force-removes **all** containers/networks/volumes/images carrying that label —
  cleanup is guaranteed even when tests fail. Individual tests still clean up eagerly.
- Test images: prefer tiny public images — `alpine:latest`, `nginx:alpine`, `redis:alpine`. Busy-loop
  CPU load: `sh -c 'while :; do :; done'`. OOM trigger: `tail /dev/zero` with 64 MB limit +
  swap disabled. Page-cache growth: `dd if=/dev/zero of=/tmp/f bs=1M count=50`.
- Slim test gated behind env var `CODEBRIX_DOCKER_TEST_SLIM=1` (skipped otherwise — slow/experimental).
- Tests must pass with `dotnet test` on this machine (Windows + Docker Desktop, Linux daemon).

Scenario coverage map:
resource management → resource limits, throttling report, live update, OOM, memory breakdown,
PID limit; image building → `BuildAsync` with `Target` (multi-stage), image size comparison,
history; image analysis → Trivy/Dive/Hadolint results on a deliberately bad Dockerfile;
runtime tuning → tmpfs/volume mounts, healthchecks + `WaitForHealthyAsync`, log options,
network aliases + exec `getent hosts`; troubleshooting → exit-code diagnosis, logs retrieval;
advisor → findings on a badly-configured container vs. clean run on a well-configured one.

## Deviations

(record any contract deviations here, with reason)

### Core library (Common / Transport / Client / Cli / System / Containers)

- **Namespace is flat: every public and internal type lives in `CodeBrix.Docker`**, with the folders in
  "Project layout" used purely for file organization. Reason: the "Public API surface" section declares
  `namespace CodeBrix.Docker;` once and lists every operation class under it, and a `CodeBrix.Docker.System`
  namespace would shadow the global `System` namespace inside the assembly. **Later agents: do not add
  folder-scoped namespaces.**
- **Fact correction — stats for an exited container.** The Facts section says `cpu_stats`/`memory_stats`
  come back as empty objects. Verified again on Docker 29.7.2 / API 1.55: `memory_stats` is `{}`, but
  `cpu_stats` and `precpu_stats` are *present with all counters zero* and `system_cpu_usage` /
  `online_cpus` absent, and `read`/`preread` are the Go zero time. The DTO consequence is unchanged
  (everything nullable), but "field is non-null" is **not** a valid liveness test — use
  `ContainerStats.HasLiveData`, and note `ThrottlingData.ThrottleRatio()` returns `0` (not `null`) when
  `Periods == 0`, per the CB005 rule's definition.
- **Additions beyond the contract** (no signatures changed): `DockerCliRunner.TryRunAsync(...)` — same
  parameters as `RunAsync` but returns the `CliResult` instead of throwing on a non-zero exit, for tools
  whose exit code encodes findings; `SystemOperations.StreamEventsAsync(type, containerIdOrName, ct)`
  overload; `DockerClient.Endpoint`.
- **`Task PruneAsync(...)` on containers** discards the daemon's `ContainersDeleted`/`SpaceReclaimed`
  report, as the contract's return type requires.

### Diagnostics / Advisor

- **`PidsStats.Limit` needed a tolerant converter (fix in `Containers/PidsStats.cs`).** The daemon
  serializes `pids_stats.limit` as an unsigned 64-bit value and uses `ulong.MaxValue`
  (`18446744073709551615`, the cgroup v2 `pids.max` value `max`) to mean "no limit", which overflows
  `long` and made **every** `GET /containers/{id}/stats` call throw for any container without a PID
  limit. `Limit` keeps its `long?` type and now uses a nested `UnlimitedAsNullInt64Converter` that maps
  out-of-range numbers to `null`. This is the only change made outside `Diagnostics/` and `Advisor/`.
- **Additions beyond the contract** (no contract signatures changed): every report carries
  `ContainerName` (the friendly name, no leading slash); `CpuThrottlingReport.HasLiveData` /
  `MemoryBreakdownReport.HasLiveData` distinguish "no throttling" from "container not running", and
  `CpuThrottlingReport.ThrottledTime` exposes the nanoseconds as a `TimeSpan`;
  `MemoryBreakdownReport.IsPageCacheDominated` surfaces the CB013 condition; `OomReport` adds
  `IsRunning` and `MemoryLimitBytes`; `HealthReport` adds `Interpretation` and `IsHealthy`;
  `ContainerDiagnosticsReport` adds `ContainerId`, `Status`, `IsRunning` and a `Summary` that leads with
  the worst finding; `AdvisorEngine.RuleIds` lists the shipped rule ids.
- **`MemoryBreakdownReport.LimitBytes` is the container's *configured* limit** (`HostConfig.Memory`),
  `null` when none is set — not the cgroup limit the stats endpoint reports, which is the host's total
  memory for an unlimited container. `UsagePercent` and `EffectiveUsagePercent` are therefore `null`
  for a container with no memory limit, rather than a meaningless percentage of host RAM.
- **`WaitForHealthyAsync` fails fast on a dead container.** Besides `TimeoutException` on expiry and
  `DockerException` when the container has no healthcheck, it throws `DockerException` if the container
  is neither running nor restarting, since it can never become healthy.
- **Rules that need live counters are skipped, not failed, for stopped containers** (CB005, CB006,
  CB013). CB013 additionally requires at least 4 MB of page cache to avoid firing on idle containers
  whose few hundred KB of cache technically dominate their usage.

### Analysis (Trivy / Dive / Hadolint / Slim)

- **Dive's report is retrieved with `docker cp`, not a bind-mounted output directory.** The contract says
  to run Dive with `--json /out/dive.json` writing into a bind-mounted subdirectory of
  `Path.GetTempPath()`. Bind-mounting a Windows host path into a Linux container depends on Docker
  Desktop file sharing being configured for that drive, which is not portable. Instead Dive writes to
  `/tmp/dive.json` inside its own container, the container is created **without** `AutoRemove`, and the
  file is copied out with `docker cp <container>:/tmp/dive.json <temp file>` after the container exits;
  the container and the local temp file are then removed. Same for Hadolint, in the other direction: the
  container is created but not started, the Dockerfile is copied in with `docker cp <file>
  <container>:/Dockerfile`, and only then is it started — so no read-only bind mount of the Dockerfile.
  Consequence: `AnalyzeImageEfficiencyAsync` and `LintDockerfileAsync` need the `docker` CLI on `PATH`
  (`DockerClientOptions.DockerCliPath`), like `Images.BuildAsync` already does.
- **Analysis pulls images itself** through `POST /images/create` (drained JSON-lines progress stream)
  rather than calling `ImageOperations`, keeping the class dependent only on `DockerApiClient` and
  `ContainerOperations` per its internal constructor. `AnalyzeImageEfficiencyAsync` and
  `OptimizeImageAsync` also pull the image *being analyzed* when it is not present locally, because Dive
  and Slim read it from the daemon; `ScanImageAsync` does not, because Trivy resolves references itself.
- **Trivy's cache volume is created directly** (`POST /volumes/create` with label
  `codebrix.docker.tool=true`) rather than through `VolumeOperations`, for the same reason.
- **Slim's default output tag is literally `<reference>.slim`** (so `alpine:3.19` becomes
  `alpine:3.19.slim`), overridable with `SlimOptions.OutputTag`.
- **Additions beyond the contract** (no contract signatures changed): tool image overrides as properties
  `AnalysisOperations.TrivyImage`/`DiveImage`/`HadolintImage`/`SlimImage`, plus per-call `ToolImage` on
  `TrivyScanOptions`/`SlimOptions`; public constants `ToolLabelName`, `ToolLabelValue`,
  `ContainerNamePrefix`, `DefaultTrivyCacheVolumeName` (useful for test cleanup);
  `TrivyScanOptions.Severities`/`IgnoreUnfixed`/`CacheVolumeName`/`Timeout`;
  `SlimOptions.ContinueAfterSeconds`/`Timeout`/`HttpProbePaths`; `ExitCode` on every result type, plus
  `TrivyScanResult.ArtifactName`/`CountOf(severity)`, `DiveAnalysisResult.TotalSizeBytes`/`WastedPercent`,
  `HadolintResult.CountByLevel`, and `SlimResult.OriginalSizeBytes`/`OptimizedSizeBytes`/`SizeReduction`.
- **Facts validated (Docker Desktop 29.7.2 / API 1.55, 2026-08-31):** `--mount type=bind` of
  `/var/run/docker.sock` works and Docker Desktop rewrites it to its proxied socket; `aquasec/trivy`
  entrypoint is `trivy` and `wagoodman/dive` is `/usr/local/bin/dive`, so both take bare arguments as
  `ContainerSpec.Command`, while `hadolint/hadolint` has **no** entrypoint (`Cmd` is
  `["/bin/hadolint","-"]`) and so needs `/bin/hadolint` as the first command element. Trivy exits `0`
  with findings when `--exit-code` is not passed, and Dive in `CI=true` mode exits `0` for an image that
  passes its built-in rules; `docker cp` out of an exited, non-auto-removed container works.

### Images / Networks / Volumes

- **All contract signatures are implemented exactly as written.** The additions below are extra
  overloads and members only; nothing in the "Public API surface" section changed.
- **Label-filtered overloads added** so callers (and the test fixture) can clean up or query only what
  they created, instead of sweeping the whole machine: `Images.ListAsync(bool all,
  IDictionary<string,string>? labelFilters, ct)`, `Images.PruneAsync(bool dangling,
  IDictionary<string,string>? labelFilters, ct)`, `Networks.ListAsync(IDictionary<string,string>?
  labelFilters, ct)`, `Networks.PruneAsync(IDictionary<string,string>? labelFilters, ct)`,
  `Volumes.ListAsync(IDictionary<string,string>? labelFilters, ct)` and
  `Volumes.PruneAsync(IDictionary<string,string>? labelFilters, ct)`.
- **`Volumes.PruneAsync()` (no filters) prunes anonymous volumes only.** Since API 1.42 the daemon
  requires the `all=true` filter before it will consider *named* volumes, and an unfiltered sweep over
  named volumes destroys user data. The label-filtered overload sends `all=true` alongside the label
  filters, so it does reclaim named volumes — but only ones carrying the caller's labels. Calling the
  label-filtered overload with no filters falls back to the anonymous-only behaviour.
- **`ImageBuildSpec.Output` (`IProgress<string>?`) added** so build logs can be observed live. The
  contract's `BuildAsync(spec, ct)` signature is unchanged; the same text is also returned in
  `ImageBuildResult.Output` (stdout and stderr interleaved in arrival order — BuildKit writes its
  progress to stderr, so a stdout-only capture would be empty).
- **`ImageBuildResult.ImageId` is resolved by inspecting the first tag**, per the contract. If that
  inspect fails (a builder that does not load its result into the local image store), the id is
  recovered by scanning the build log for a 64-hex-digit `sha256:` value; if that also fails the
  property is empty rather than throwing.
- **404 mapping was not extended** for networks and volumes: `DockerApiClient.CreateApiException` still
  returns a plain `DockerApiException` for `networks/…` and `volumes/…`, matching the error model,
  which names container and image not-found types only.
- **Image references are escaped leniently.** `Uri.EscapeDataString` is applied and then `%2F` and
  `%3A` are restored, because the daemon's image routes match the rest of the path and a reference such
  as `ghcr.io/owner/name:tag` must keep its separators literal.
- **`PullAsync` reads the progress stream itself** (`PostForStreamAsync` + line loop) rather than using
  `GetJsonLinesAsync<T>`, which is GET-only. An `{"error": …}` line in an otherwise-200 response is
  raised as a `DockerException`; when its text looks like an authentication refusal the pull is retried
  through `docker pull`, per the contract.

### Test suite

- **Fact correction — the OOM trigger.** The "Testing strategy" section prescribes `tail /dev/zero`
  with a 64 MB limit and swap disabled. Verified on Docker 29.7.2 / API 1.55: BusyBox `tail` (both
  `alpine:latest` and `busybox:latest`) caps its own read-ahead buffer, so the container exits with
  code **1** and `State.OOMKilled=false` — no OOM kill at all. The suite instead fills a tmpfs that is
  larger than the container's memory limit (`MountSpec.Tmpfs("/hog", 512 MB)` plus
  `dd if=/dev/zero of=/hog/fill bs=1M count=200`, memory and memory-swap both 64 MB): tmpfs pages are
  charged to the container's memory cgroup and cannot be reclaimed with swap disabled, which reproduces
  `State.OOMKilled=true` with exit code 137 every time (`OomSpecs.MemoryHog` in the test project).
- **BusyBox `nslookup` exit codes are not a reliable resolution test.** On a user-defined network the
  embedded resolver forwards misses upstream, and an upstream wildcard answer makes `nslookup` exit `0`
  for a name that is not on the network. The network tests therefore assert on the resolved address —
  the endpoint IP that `ContainerInspectResult.NetworkSettings.Networks[network].IpAddress` reports —
  rather than on the exit code.
