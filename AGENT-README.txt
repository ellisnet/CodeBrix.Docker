================================================================================
AGENT-README: CodeBrix.Docker
A Guide for AI Coding Agents -- CONSUMING the
CodeBrix.Docker.MitLicenseForever NuGet package
================================================================================

OVERVIEW
========
CodeBrix.Docker is a cross-platform, zero-dependency .NET library for managing,
diagnosing and optimizing Docker containers and images. It speaks the Docker
Engine API directly over the daemon's own transport -- a Unix domain socket, a
Windows named pipe, a TCP endpoint, or an SSH tunnel to a remote host -- and
gives you a typed, async-only object model over the whole thing.

There are three tiers of capability, and they build on one another:

  1. LIFECYCLE -- containers, images, networks, volumes, daemon information and
     events, typed resource limits (CPU, memory, swap, PIDs) that can be set at
     creation and retuned while the container runs, log retrieval with the
     Docker stream framing already decoded, and command execution inside a
     running container -- one-shot, or as a live interactive terminal session
     with standard input, a pseudo-terminal and resize.

  2. DIAGNOSTICS -- the questions you actually ask when a container misbehaves.
     Is the CPU limit throttling it, and by how much? Was it killed by the
     kernel OOM killer? Is that alarming memory number real application memory
     or reclaimable page cache? Is the healthcheck passing? Each report carries
     the raw counters AND a plain-English Interpretation sentence.

  3. OPTIMIZATION -- an advisor rules engine that inspects a container against
     fourteen container best practices and returns findings with concrete
     recommendations, plus image analysis that runs Trivy, Dive, Hadolint and
     the Slim/mint image optimizer AS CONTAINERS. Nothing has to be installed
     on the host machine for the analysis tier to work; the tool images are
     pulled on demand.

Everything is async-only. Every public operation returns Task, Task<T> or
IAsyncEnumerable<T> and takes CancellationToken as its last parameter with a
default. There are no synchronous wrappers.

Target framework: .NET 10 or later. There is no netstandard or .NET Framework
target.

Source repository: https://github.com/ellisnet/CodeBrix.Docker


INSTALLATION
============
PackageId: CodeBrix.Docker.MitLicenseForever

    dotnet add package CodeBrix.Docker.MitLicenseForever

or in a .csproj:

    <PackageReference Include="CodeBrix.Docker.MitLicenseForever" />

IMPORTANT: the package id is CodeBrix.Docker.MitLicenseForever, NOT
"CodeBrix.Docker". The ".MitLicenseForever" suffix belongs to the PACKAGE ID
only -- it never appears in a namespace, a using directive or a type name. The
assembly is CodeBrix.Docker and the single namespace is CodeBrix.Docker.

NuGet dependencies: NONE. The package's dependency group is empty; it pulls in
nothing but the .NET runtime. All JSON goes through the in-box
System.Text.Json, and the SSH transport runs the operating system's own SSH
client rather than referencing an SSH library.

License: MIT. The package requires license acceptance.

WHAT MUST BE PRESENT AT RUN TIME

  - A reachable Docker daemon. Everything in the library talks to one; there is
    no offline or mock mode. SystemOperations.PingAsync is the cheap way to find
    out whether there is one.

  - A LINUX daemon. The library targets Linux containers. Windows and macOS
    hosts are supported as CLIENTS -- Docker Desktop in Linux-container mode is
    exactly right -- but a daemon in Windows-container mode is not.
    SystemOperations.EnsureLinuxDaemonAsync throws a DockerException naming the
    daemon's OSType when it is anything but "linux".

  - The `docker` command-line tool on PATH, but only for four operations:
    ImageOperations.BuildAsync (BuildKit builds go through the CLI, because the
    Engine API's build endpoint is the legacy builder), ImageOperations.PullAsync
    WHEN an anonymous pull is refused and a credential helper is needed, and the
    `docker cp` steps inside AnalysisOperations.AnalyzeImageEfficiencyAsync and
    AnalysisOperations.LintDockerfileAsync. Point
    DockerClientOptions.DockerCliPath at a different executable if `docker` is
    not on PATH. Everything else in the library is pure Engine API and needs no
    CLI at all.

  - For an ssh:// endpoint only: an SSH client on PATH (OpenSSH ships with
    Linux, macOS and Windows 10 and later), key-based authentication, the remote
    host key already in a known_hosts file, and the `docker` CLI installed on
    the REMOTE host. See CONNECTING TO A DAEMON.

  - For the analysis tier only: outbound network access the first time, because
    the tool images are pulled, and Trivy downloads its vulnerability database
    into a Docker volume it reuses afterwards.

Platform notes: the Unix-socket transport is the validated path and is what the
library's own test suite exercises end to end. The Windows named-pipe transport
is implemented but is not exercised by the suite on Linux. https:// endpoints
are deliberately not supported -- see CONNECTING TO A DAEMON.


KEY NAMESPACES / USINGS
=======================

    using CodeBrix.Docker;   // EVERY public type in the package

That is the whole story. The library declares exactly ONE namespace,
CodeBrix.Docker, and every one of its ninety public types lives in it. The
folders you see in the repository (Containers/, Images/, Diagnostics/,
Advisor/, Analysis/, Transport/, Client/, Common/, Cli/, Networks/, Volumes/,
System/) are FILE ORGANIZATION ONLY. They are not namespaces.

  - There is no CodeBrix.Docker.Containers, no CodeBrix.Docker.Diagnostics and
    no CodeBrix.Docker.Analysis namespace. Writing
    `using CodeBrix.Docker.Containers;` is a CS0246 compile error.
  - In particular there is deliberately no CodeBrix.Docker.System namespace: it
    would shadow the global System namespace inside the assembly.

Depending on what you touch you will also want the ordinary framework usings:

    using System;                        // TimeSpan, IProgress<T>, exceptions
    using System.Collections.Generic;    // IDictionary<,>, IReadOnlyList<>
    using System.Threading;              // CancellationToken(Source)
    using System.Threading.Tasks;        // Task, await
    using System.Linq;                   // when filtering result collections
    using System.Text;                   // Encoding, for exec stream bytes

NAMING GOTCHA -- the library's own SystemOperations is reached as
`client.System`, and inside a file that also has `using System;` the expression
`client.System.PingAsync()` is unambiguous (it is a member access, not a type
name), so no alias is needed. You only need care if you write a local variable
named `System`.


CONNECTING TO A DAEMON
======================

    using var client = DockerClient.Create();                 // resolve it
    using var client = DockerClient.Create(new DockerClientOptions
    {
        Endpoint = "unix:///var/run/docker.sock",
    });

ENDPOINT RESOLUTION ORDER
-------------------------
The endpoint is resolved once, when the client is created, in this order:

    1. DockerClientOptions.Endpoint, when it is not null or blank.
    2. The DOCKER_HOST environment variable.
    3. The platform default:
         Windows        ->  npipe://./pipe/docker_engine
         anything else  ->  unix:///var/run/docker.sock

DockerClient.Endpoint reports the string that was resolved, exactly as written.
Reading it back is the cheapest way to confirm which daemon a client is talking
to.

SUPPORTED SCHEMES
-----------------
  unix://<path>                 A Unix domain socket. The default on Linux and
                                macOS. This is the validated, primary path.

  npipe://./pipe/<name>         A Windows named pipe. The Windows default.
                                Accepted spellings include npipe://./pipe/x,
                                npipe:////./pipe/x, npipe://localhost/pipe/x and
                                npipe://\\.\pipe\x.

  tcp://host:port               Plain HTTP to a daemon listening on TCP.
  http://host:port              Same thing; http:// is accepted as a synonym.

  ssh://[user@]host[:port]      A remote daemon reached through the system SSH
                                client. Port defaults to 22.

  https://...                   NOT SUPPORTED. DockerClient.Create throws
                                NotSupportedException for an https:// endpoint,
                                with the message "TLS-secured Docker endpoints
                                (https://) are not supported in this version of
                                CodeBrix.Docker." This is a deliberate omission,
                                not an oversight: tcp:// plus DOCKER_TLS_VERIFY
                                needs a certificate authority, a server
                                certificate and a client certificate. Use ssh://
                                to reach a remote daemon; it needs none of that.

An unrecognised scheme also throws NotSupportedException, naming the scheme.

THE ssh:// TRANSPORT, IN DETAIL
-------------------------------
Docker's own CLI does not implement SSH, and neither does this library. Given an
ssh:// endpoint, both spawn the system SSH client running

    docker system dial-stdio

on the remote host, which proxies its standard input and output to the remote
/var/run/docker.sock. Ordinary HTTP then flows over that pipe. Keys, agents,
~/.ssh/config, jump hosts and known_hosts all belong to OpenSSH and are
deliberately left there -- which is why the zero-dependency guarantee survives.

Accepted forms:

    ssh://build-01                 user and port come from the SSH client's own
                                   configuration (a Host block in ~/.ssh/config
                                   still governs this form)
    ssh://deploy@build-01          user named, port 22
    ssh://deploy@build-01:2222     both named
    ssh://deploy@[fe80::1]:2222    IPv6 literal, in brackets

A path is rejected -- ssh://user@host/var/run/docker.sock throws
DockerException -- because the socket on the far end is always whatever
dial-stdio opens there.

Two options tune it:

    public string        SshExecutablePath { get; set; } = "ssh";
    public IList<string> SshArguments      { get; set; } = [];

SshArguments is inserted after the library's own options and before the
destination. Use it when a service cannot rely on a ~/.ssh/config:

    SshArguments = { "-i", "/keys/deploy" }
    SshArguments = { "-J", "bastion" }
    SshArguments = { "-o", "UserKnownHostsFile=/etc/docker/known_hosts" }

The command line the library builds for ssh://deploy@build-01:2222 with
SshArguments ["-i", "/keys/deploy"] is:

    ssh -o BatchMode=yes -o ConnectTimeout=<DefaultTimeout in seconds> -T \
        -l deploy -p 2222 -i /keys/deploy -- build-01 docker system dial-stdio

The port is passed only when it is not 22, and the user only when the endpoint
names one. ConnectTimeout comes from DockerClientOptions.DefaultTimeout, or 30
seconds when that is non-positive or infinite.

FOUR RULES THE ssh:// TRANSPORT ENFORCES DELIBERATELY

  a) NON-INTERACTIVE ONLY. `-o BatchMode=yes` is passed FIRST and is not
     optional. OpenSSH honours the first value it is given for an option, so
     nothing you add through SshArguments can reintroduce a password prompt. A
     prompt has nowhere to go from inside a library, so it fails immediately
     rather than hanging. Key-based authentication -- an agent, or an -i key --
     is the only route.

  b) HOST KEYS ARE NEVER ACCEPTED AUTOMATICALLY. StrictHostKeyChecking is never
     set, so OpenSSH's own policy applies and an unknown or changed key fails
     under BatchMode. The DockerException tells you the fix rather than the
     symptom: connect once by hand to check and record the key, then try again.
     Setting StrictHostKeyChecking=no yourself is a real security downgrade, not
     a convenience.

  c) THE REMOTE HOST NEEDS THE DOCKER CLI, because dial-stdio is a subcommand of
     the remote docker binary -- the same requirement Docker's own CLI has. A
     remote without it is reported as such, keyed on exit code 127 as well as on
     the message text (the wording of "not found" varies by remote login shell).

  d) THE LOCAL SSH CLIENT MUST EXIST. If it cannot be started, the failure names
     the executable, the endpoint, and SshExecutablePath as the knob to change.

TIMEOUTS
--------
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(100);

DefaultTimeout applies to each NON-STREAMING Engine API call, with the
exceptions listed below. Streaming calls -- GetLogsAsync, StreamStatsAsync,
StreamEventsAsync and the interactive exec stream -- are never timed out,
because their whole point is to stay open.

These NON-streaming calls are exempt as well, because each one is open-ended by
nature: how long they take is the caller's business, not the client's.

    Containers.StopAsync            honours its own timeoutSeconds
    Containers.RestartAsync         honours its own timeoutSeconds
    Containers.WaitForExitAsync     blocks until the container exits
    Containers.PruneAsync           sweeps an unbounded amount of work
    Images.PruneAsync               same
    Networks.PruneAsync             same
    Volumes.PruneAsync              both overloads

Bound all of those with a CancellationToken instead. For an ssh:// endpoint
DefaultTimeout also supplies the SSH client's ConnectTimeout, and it bounds the
HTTP-upgrade handshake of an exec session but never the hijacked stream that
follows.

DISPOSAL AND REUSE
------------------
DockerClient is IDisposable and owns one pooled HttpClient. CREATE ONE AND KEEP
IT. It is safe to use concurrently, and connections are pooled with a two-minute
idle timeout. Creating a client per call is wasteful on a local socket and
genuinely expensive over ssh://, where every new HTTP connection pays a full SSH
handshake.


THE ERROR MODEL
===============
Everything the library throws for a Docker-side problem derives from
DockerException, so one catch clause covers the lot:

    DockerException : Exception
      |
      +-- DockerApiException : DockerException
      |     HttpStatusCode StatusCode { get; }
      |     string         ResponseBody { get; }
      |     |
      |     +-- DockerContainerNotFoundException : DockerApiException
      |     +-- DockerImageNotFoundException : DockerApiException
      |
      +-- DockerCliException : DockerException
            int    ExitCode { get; }
            string StdErr   { get; }
            string Command  { get; }

  - DockerApiException is raised for any non-2xx response from the daemon. When
    the daemon's body is the usual {"message": "..."} JSON, that text becomes
    the exception Message; the untouched body is always in ResponseBody.
    DockerApiException.TryExtractDaemonMessage(string responseBody) is public if
    you want to do the same extraction yourself.

  - DockerContainerNotFoundException is thrown for a 404 on a container route,
    DockerImageNotFoundException for a 404 on an image route. Catch the specific
    type when "it isn't there" is a normal outcome for your code. NOTE that 404s
    on network and volume routes surface as a plain DockerApiException -- there
    are no not-found subclasses for those two.

  - DockerCliException is thrown when one of the four CLI-backed operations exits
    non-zero. Command is the full command line, StdErr is everything the process
    wrote to standard error, and ExitCode is the process exit code.

  - DockerException itself (with no subclass) is used for transport failures and
    for "this can never work" conditions -- an unreachable daemon, an untrusted
    SSH host key, a remote without the Docker CLI, a container that can never
    become healthy. These messages are written to tell you what to do about it,
    so surface them rather than replacing them.

  - NotSupportedException, not DockerException, is what you get for an https://
    or otherwise unknown endpoint scheme, and for CloseStandardInputAsync on a
    transport that cannot half-close.

  - ArgumentException / ArgumentNullException / ArgumentOutOfRangeException are
    thrown BEFORE any request when a spec is incomplete -- an empty
    ContainerSpec.Image, an ExecSpec with no Command, a resize with a
    non-positive dimension.

  - SystemOperations.PingAsync NEVER THROWS for a Docker-side or transport
    problem. It returns false for a daemon that is down, an unreachable host, or
    an untrusted SSH host key alike. The one exception is your own
    CancellationToken: cancelling it still surfaces OperationCanceledException.
    When the reason matters, call GetVersionAsync and read the exception.


================================================================================

CORE API REFERENCE
==================

DockerClient -- THE ENTRY POINT
-------------------------------

    public sealed class DockerClient : IDisposable
    {
        public static DockerClient Create(DockerClientOptions options = null);

        public string               Endpoint     { get; }   // as resolved
        public ContainerOperations  Containers   { get; }
        public ImageOperations      Images       { get; }
        public NetworkOperations    Networks     { get; }
        public VolumeOperations     Volumes      { get; }
        public SystemOperations     System       { get; }
        public DiagnosticsOperations Diagnostics { get; }
        public AdvisorEngine        Advisor      { get; }
        public AnalysisOperations   Analysis     { get; }

        public void Dispose();
    }

The eight operation properties are the whole API surface. They are created with
the client and are not separately constructible: there is no public constructor
on ContainerOperations, ImageOperations, NetworkOperations, VolumeOperations,
SystemOperations, DiagnosticsOperations, AdvisorEngine or AnalysisOperations.
You always reach them through a DockerClient.

    public sealed class DockerClientOptions
    {
        public string        Endpoint          { get; set; }            // null => resolve
        public string        DockerCliPath     { get; set; } = "docker";
        public string        SshExecutablePath { get; set; } = "ssh";
        public IList<string> SshArguments      { get; set; } = [];
        public TimeSpan      DefaultTimeout    { get; set; } = TimeSpan.FromSeconds(100);
    }

When Endpoint is set it is also handed to the CLI-backed operations as
DOCKER_HOST, so an image build acts on the same daemon as the rest of the
client. When it is null the child process inherits the environment and resolves
the daemon exactly as the library does.


CONTAINERS: ContainerOperations
-------------------------------

LIFECYCLE

    Task<string> CreateAsync(ContainerSpec spec,
                             CancellationToken cancellationToken = default)
        Creates without starting. Returns the new container's id.
        Throws DockerImageNotFoundException when the image is not already
        present locally -- this method NEVER pulls. Throws ArgumentException
        when ContainerSpec.Image is null or blank.

    Task StartAsync(string idOrName,
                    CancellationToken cancellationToken = default)
        Starting an already-running container succeeds silently.

    Task<string> RunAsync(ContainerSpec spec,
                          CancellationToken cancellationToken = default)
        CreateAsync followed by StartAsync. Returns the id.

    Task StopAsync(string idOrName, int timeoutSeconds = 10,
                   CancellationToken cancellationToken = default)
        SIGTERM, then SIGKILL after the grace period.

    Task RestartAsync(string idOrName, int timeoutSeconds = 10,
                      CancellationToken cancellationToken = default)

    Task KillAsync(string idOrName, string signal = "SIGKILL",
                   CancellationToken cancellationToken = default)

    Task RemoveAsync(string idOrName, bool force = false,
                     bool removeVolumes = false,
                     CancellationToken cancellationToken = default)

    Task<long> WaitForExitAsync(string idOrName,
                                CancellationToken cancellationToken = default)
        Blocks until the container exits; returns its exit code. Not timed out.

    Task PruneAsync(IDictionary<string, string> labelFilters = null,
                    CancellationToken cancellationToken = default)
        Removes stopped containers. With labelFilters, only those carrying every
        one of the given label/value pairs.

QUERY

    Task<IReadOnlyList<ContainerSummary>> ListAsync(
        bool all = false,
        IDictionary<string, string> labelFilters = null,
        CancellationToken cancellationToken = default)
        all = false lists running containers only.

    Task<ContainerInspectResult> InspectAsync(
        string idOrName, CancellationToken cancellationToken = default)

RESOURCES, STATS AND LOGS

    Task UpdateResourcesAsync(string idOrName, ResourceLimits limits,
                              CancellationToken cancellationToken = default)
        Retunes a RUNNING container in place. Only the properties you set on
        `limits` are sent; the rest are left alone.

    Task<ContainerStats> GetStatsAsync(
        string idOrName, CancellationToken cancellationToken = default)
        One sample (?stream=false).

    IAsyncEnumerable<ContainerStats> StreamStatsAsync(
        string idOrName, CancellationToken cancellationToken = default)
        A sample roughly every second until the token is cancelled or the
        container stops.

    Task<ContainerLogs> GetLogsAsync(string idOrName, int? tail = null,
                                     bool timestamps = false,
                                     CancellationToken cancellationToken = default)
        Returns the two streams already demultiplexed out of Docker's stdcopy
        framing. tail = null means the whole log.

EXECUTION

    Task<ExecResult> ExecAsync(string idOrName,
                               IReadOnlyList<string> command,
                               string user = null,
                               string workingDir = null,
                               IReadOnlyList<string> env = null,
                               CancellationToken cancellationToken = default)
        One-shot: runs the command, buffers both streams to the end, returns
        ExecResult(Stdout, Stderr, ExitCode).

    Task<ContainerExecStream> ExecStreamAsync(
        string idOrName, ExecSpec spec,
        CancellationToken cancellationToken = default)
        The interactive counterpart. See INTERACTIVE EXEC below.

    Task ResizeExecAsync(string execId, int height, int width,
                         CancellationToken cancellationToken = default)
        Tells the daemon an exec session's terminal changed size. Throws
        ArgumentOutOfRangeException for a non-positive dimension, and
        DockerApiException when the session has no terminal or has finished.

    Task<ExecInspectResult> InspectExecAsync(
        string execId, CancellationToken cancellationToken = default)
        Where the exit code of a streaming exec comes from.


THE CONTAINER SPECIFICATION
---------------------------
ContainerSpec is an object-initializer type. Image is the only required member;
the collection properties are pre-initialized, so `Labels = { ["k"] = "v" }`
and `Mounts = { MountSpec.Volume(...) }` collection-initializer syntax works.

    public sealed class ContainerSpec
    {
        public string                      Image        { get; set; } = string.Empty;  // REQUIRED
        public string                      Name         { get; set; }
        public IReadOnlyList<string>       Command      { get; set; }   // overrides CMD
        public IReadOnlyList<string>       Entrypoint   { get; set; }   // overrides ENTRYPOINT
        public IList<string>               Env          { get; set; } = [];   // "KEY=VALUE"
        public IDictionary<string, string> Labels       { get; set; } = ...;
        public string                      User         { get; set; }
        public string                      WorkingDir   { get; set; }
        public string                      HostName     { get; set; }
        public IList<PortBinding>          PortBindings { get; set; } = [];  // publish
        public IList<PortBinding>          ExposedPorts { get; set; } = [];  // expose only
        public IList<MountSpec>            Mounts       { get; set; } = [];
        public string                      NetworkName  { get; set; }
        public IList<string>               NetworkAliases { get; set; } = [];
        public RestartPolicy               RestartPolicy { get; set; }
        public bool                        AutoRemove   { get; set; }
        public bool                        Privileged   { get; set; }
        public HealthcheckSpec             Healthcheck  { get; set; }
        public string                      LogDriver    { get; set; }
        public IDictionary<string, string> LogOptions   { get; set; } = ...;
        public ResourceLimits              Limits       { get; set; }
    }

MountSpec is constructed only through its three factory methods; it has no
public constructor:

    public sealed class MountSpec
    {
        public MountKind Kind            { get; }   // Volume | Bind | Tmpfs
        public string    Source          { get; }
        public string    Target          { get; }
        public bool      ReadOnly        { get; }
        public long?     TmpfsSizeBytes  { get; }

        public static MountSpec Volume(string name, string containerPath,
                                       bool readOnly = false);
        public static MountSpec Bind(string hostPath, string containerPath,
                                     bool readOnly = false);
        public static MountSpec Tmpfs(string containerPath, long? sizeBytes = null);
    }

    public enum MountKind { Volume, Bind, Tmpfs }

Ports, restart policy and healthcheck:

    public sealed record PortBinding(int ContainerPort, int? HostPort = null,
                                     string Protocol = "tcp")
    {
        public string PortKey { get; }     // e.g. "6379/tcp"
    }

    public sealed record RestartPolicy(RestartPolicyKind Kind, int MaxRetries = 0)
    {
        public static RestartPolicy No            { get; }
        public static RestartPolicy Always        { get; }
        public static RestartPolicy UnlessStopped { get; }
        public static RestartPolicy OnFailure(int maxRetries = 0);
    }

    public enum RestartPolicyKind { No, Always, OnFailure, UnlessStopped }

    public sealed class HealthcheckSpec
    {
        public IReadOnlyList<string> Test        { get; set; }  // e.g. ["CMD-SHELL", "..."]
        public TimeSpan?             Interval    { get; set; }
        public TimeSpan?             Timeout     { get; set; }
        public TimeSpan?             StartPeriod { get; set; }
        public int?                  Retries     { get; set; }
    }

A PortBinding with HostPort set is PUBLISHED (host port -> container port); one
without is merely exposed. Leaving HostPort null in PortBindings is the same as
listing it in ExposedPorts.


RESOURCE LIMITS
---------------

    public sealed class ResourceLimits
    {
        public double? Cpus                   { get; set; }  // 0.5 = half a CPU
        public string  CpusetCpus             { get; set; }  // "0", "0,1", "0-3"
        public long?   CpuShares              { get; set; }  // relative weight, default 1024
        public long?   MemoryBytes            { get; set; }
        public long?   MemoryReservationBytes { get; set; }  // soft limit
        public long?   MemorySwapBytes        { get; set; }  // == MemoryBytes disables swap
        public long?   PidsLimit              { get; set; }

        public static long Megabytes(int mb);
        public static long Gigabytes(int gb);

        public long? ToNanoCpus();   // Cpus * 1e9, or null
        public bool  IsEmpty { get; }
    }

The same type is used at creation (ContainerSpec.Limits) and for live retuning
(UpdateResourcesAsync). Cpus is the friendly form of the daemon's NanoCpus;
0.25 means a quarter of one CPU's time, enforced by the CFS quota. To disable
swap entirely, set MemorySwapBytes equal to MemoryBytes -- that is what makes
an out-of-memory kill deterministic rather than a slow slide into swap.

Reading limits back is a different type: ContainerInspectResult.HostConfig is a
ContainerHostConfig, which reports the daemon's raw fields plus four computed
conveniences.

    public sealed class ContainerHostConfig
    {
        public long   NanoCpus          { get; init; }   // 0 = no CPU limit
        public string CpusetCpus        { get; init; }
        public long   CpuShares         { get; init; }
        public long   Memory            { get; init; }   // 0 = unlimited
        public long   MemoryReservation { get; init; }
        public long   MemorySwap        { get; init; }   // -1 = unlimited swap
        public long?  PidsLimit         { get; init; }   // null = not configured
        public bool   Privileged        { get; init; }
        public bool   AutoRemove        { get; init; }
        public bool   ReadonlyRootfs    { get; init; }
        public HostRestartPolicy RestartPolicy { get; init; }
        public LogConfig         LogConfig     { get; init; }
        public string NetworkMode       { get; init; }
        public IReadOnlyList<string> Binds { get; init; }
        public IReadOnlyDictionary<string, string> Tmpfs { get; init; }

        public bool    HasCpuLimit    { get; }   // NanoCpus > 0
        public bool    HasMemoryLimit { get; }   // Memory > 0
        public bool    IsSwapDisabled { get; }   // Memory > 0 && MemorySwap == Memory
        public double? Cpus           { get; }   // NanoCpus / 1e9, or null
    }

    public sealed class HostRestartPolicy
    {
        public string            Name                { get; set; }  // "", "always", ...
        public long              MaximumRetryCount   { get; set; }
        public RestartPolicyKind Kind                { get; }       // parsed from Name
    }

    public sealed class LogConfig
    {
        public string                      Type   { get; set; }  // "json-file", "local", ...
        public IDictionary<string, string> Config { get; set; }  // "max-size", "max-file"
    }


CONTAINER RESULTS: SUMMARY, INSPECT AND STATE
---------------------------------------------

    public sealed class ContainerSummary            // from ListAsync
    {
        public string                      Id                 { get; init; }
        public IReadOnlyList<string>       Names              { get; init; }
        public string                      Image              { get; init; }
        public string                      ImageId            { get; init; }
        public string                      Command            { get; init; }
        public long                        CreatedUnixSeconds { get; init; }
        public string                      State              { get; init; }
        public string                      Status             { get; init; }
        public IReadOnlyDictionary<string, string> Labels     { get; init; }
        public IReadOnlyList<ContainerPort> Ports             { get; init; }
        public long                        SizeRw             { get; init; }
        public long                        SizeRootFs         { get; init; }

        public string          DisplayName { get; }   // first name, no leading '/', or the short id
        public DateTimeOffset? Created     { get; }
        public bool            IsRunning   { get; }   // State == "running"
    }

    public sealed class ContainerInspectResult      // from InspectAsync
    {
        public string                 Id              { get; init; }
        public string                 Name            { get; init; }   // "/name"
        public DateTimeOffset?        Created         { get; init; }
        public string                 Image           { get; init; }
        public string                 LogPath         { get; init; }
        public long                   RestartCount    { get; init; }
        public ContainerState         State           { get; init; }
        public ContainerConfig        Config          { get; init; }
        public ContainerHostConfig    HostConfig      { get; init; }
        public ContainerNetworkSettings NetworkSettings { get; init; }
        public IReadOnlyList<ContainerMountPoint> Mounts { get; init; }

        public string DisplayName { get; }   // Name without the leading '/'
        public bool   IsRunning   { get; }
    }

    public sealed class ContainerState
    {
        public string           Status     { get; init; }  // created|running|exited|...
        public bool             Running    { get; init; }
        public bool             Paused     { get; init; }
        public bool             Restarting { get; init; }
        public bool             OomKilled  { get; init; }
        public bool             Dead       { get; init; }
        public long             Pid        { get; init; }
        public long             ExitCode   { get; init; }
        public string           Error      { get; init; }
        public DateTimeOffset?  StartedAt  { get; init; }
        public DateTimeOffset?  FinishedAt { get; init; }
        public ContainerHealth  Health     { get; init; }  // null without a healthcheck
    }

    public sealed class ContainerConfig
    {
        public string                      Image        { get; init; }
        public string                      User         { get; init; }  // "" means root
        public IReadOnlyList<string>       Env          { get; init; }
        public IReadOnlyDictionary<string, string> Labels { get; init; }
        public IReadOnlyList<string>       Cmd          { get; init; }
        public IReadOnlyList<string>       Entrypoint   { get; init; }
        public string                      WorkingDir   { get; init; }
        public string                      Hostname     { get; init; }
        public bool                        Tty          { get; init; }
        public HealthcheckSpec             Healthcheck  { get; init; }
        public IReadOnlyDictionary<string, JsonEmptyObject> ExposedPorts { get; init; }
    }

    public sealed class ContainerHealth
    {
        public string  Status        { get; init; }   // starting|healthy|unhealthy
        public long    FailingStreak { get; init; }
        public IReadOnlyList<ContainerHealthLogEntry> Log { get; init; }
        public bool    IsHealthy     { get; }
    }

    public sealed class ContainerHealthLogEntry
    {
        public DateTimeOffset? Start    { get; init; }
        public DateTimeOffset? End      { get; init; }
        public long            ExitCode { get; init; }   // 0 == healthy
        public string          Output   { get; init; }
    }

    public sealed class ContainerNetworkSettings
    {
        public IReadOnlyDictionary<string, ContainerEndpointSettings> Networks { get; init; }
        public string IpAddress { get; init; }   // on the default bridge, if any
    }

    public sealed class ContainerEndpointSettings
    {
        public string                NetworkId       { get; init; }
        public string                EndpointId      { get; init; }
        public string                IpAddress       { get; init; }
        public int                   IpPrefixLength  { get; init; }
        public string                Gateway         { get; init; }
        public string                MacAddress      { get; init; }
        public IReadOnlyList<string> Aliases         { get; init; }
    }

    public sealed class ContainerMountPoint
    {
        public string Type        { get; init; }   // volume | bind | tmpfs
        public string Name        { get; init; }
        public string Source      { get; init; }
        public string Destination { get; init; }
        public string Driver      { get; init; }
        public string Mode        { get; init; }
        public bool   ReadWrite   { get; init; }
    }

    public sealed class ContainerPort
    {
        public string Ip          { get; init; }
        public int?   PublicPort  { get; init; }   // null when not published
        public int    PrivatePort { get; init; }
        public string Protocol    { get; init; }   // "tcp" | "udp"
    }

JsonEmptyObject is a marker for the Docker API's `{}` placeholders -- the values
in an ExposedPorts dictionary. Read the KEYS ("6379/tcp"); the value carries no
information and JsonEmptyObject.Instance is the shared singleton.


LIVE STATISTICS
---------------

    public sealed class ContainerStats
    {
        public string           Id          { get; init; }
        public string           Name        { get; init; }
        public DateTimeOffset?  Read        { get; init; }
        public DateTimeOffset?  PreRead     { get; init; }
        public CpuStats         CpuStats    { get; init; }
        public CpuStats         PreCpuStats { get; init; }
        public MemoryStats      MemoryStats { get; init; }
        public PidsStats        PidsStats   { get; init; }
        public BlkioStats       BlkioStats  { get; init; }
        public IReadOnlyDictionary<string, NetworkStats> Networks { get; init; }
        public int?             NumProcs    { get; init; }

        public bool    HasLiveData             { get; }
        public double? CpuPercent();            // of one CPU x online CPUs
        public double? MemoryPercent();         // of the cgroup limit
        public double? EffectiveMemoryPercent();// anon memory only
        public double? ThrottleRatio();         // 0..1, see below
    }

EVERY numeric member of the stats tree is nullable, and that is not defensive
padding: a stopped container really does come back with an empty memory_stats
object and all-zero CPU counters. HasLiveData is the correct liveness test --
"a field is non-null" is not.

    public sealed class CpuStats
    {
        public CpuUsage       CpuUsage        { get; init; }
        public long?          SystemCpuUsage  { get; init; }
        public int?           OnlineCpus      { get; init; }
        public ThrottlingData ThrottlingData  { get; init; }
    }

    public sealed class CpuUsage
    {
        public long?                 TotalUsage        { get; init; }  // nanoseconds
        public long?                 UsageInKernelMode { get; init; }
        public long?                 UsageInUserMode   { get; init; }
        public IReadOnlyList<long>   PerCpuUsage       { get; init; }
    }

    public sealed class ThrottlingData
    {
        public long? Periods          { get; init; }
        public long? ThrottledPeriods { get; init; }
        public long? ThrottledTime    { get; init; }   // nanoseconds

        public double? ThrottleRatio();
        // null when Periods is null, and null when Periods > 0 but
        // ThrottledPeriods is null; 0 when Periods <= 0 (NOT null);
        // otherwise ThrottledPeriods / Periods.
    }

    public sealed class MemoryStats
    {
        public long? Usage    { get; init; }
        public long? MaxUsage { get; init; }
        public long? Limit    { get; init; }   // the CGROUP limit -- see the pitfalls
        public long? Failcnt  { get; init; }
        public IReadOnlyDictionary<string, long> Stats { get; init; }  // cgroup v2 keys

        public long? AnonBytes   { get; }   // "anon", falling back to "rss"
        public long? FileBytes   { get; }   // "file", falling back to "cache"
        public long? KernelBytes { get; }   // "kernel", falling back to "kernel_stack"
        public long? SlabBytes   { get; }
        public long? ShmemBytes  { get; }
        public long? Lookup(string key);    // any other cgroup counter by name
    }

    public sealed class PidsStats
    {
        public long? Current { get; init; }
        public long? Limit   { get; init; }   // see the pitfall about cgroup drivers
    }

    public sealed class BlkioStats
    {
        public IReadOnlyList<BlkioStatEntry> IoServiceBytesRecursive { get; init; }
        public IReadOnlyList<BlkioStatEntry> IoServicedRecursive     { get; init; }
        public IReadOnlyList<BlkioStatEntry> IoQueuedRecursive       { get; init; }
        public IReadOnlyList<BlkioStatEntry> IoServiceTimeRecursive  { get; init; }
        public IReadOnlyList<BlkioStatEntry> IoWaitTimeRecursive     { get; init; }

        public long? TotalBytes(string op);   // "read", "write", "total"; case-insensitive
    }

    public sealed class BlkioStatEntry
    {
        public long?  Major { get; init; }
        public long?  Minor { get; init; }
        public string Op    { get; init; }
        public long?  Value { get; init; }
    }

    public sealed class NetworkStats     // one per interface, keyed by name
    {
        public long? RxBytes   { get; init; }
        public long? RxPackets { get; init; }
        public long? RxErrors  { get; init; }
        public long? RxDropped { get; init; }
        public long? TxBytes   { get; init; }
        public long? TxPackets { get; init; }
        public long? TxErrors  { get; init; }
        public long? TxDropped { get; init; }
    }


LOGS AND ONE-SHOT EXEC
----------------------

    public sealed record ContainerLogs(string Stdout, string Stderr)
    {
        public bool   IsEmpty  { get; }
        public string Combined { get; }   // Stdout + Stderr
    }

    public sealed record ExecResult(string Stdout, string Stderr, long ExitCode)
    {
        public bool Succeeded { get; }    // ExitCode == 0
    }

GetLogsAsync and ExecAsync both hand back streams that have already been decoded
out of Docker's stdcopy framing (an 8-byte header per chunk: a stream-type byte,
three zero bytes, then a big-endian 32-bit payload length). You never see the
framing.


INTERACTIVE EXEC: A TERMINAL INSIDE THE CONTAINER
-------------------------------------------------
ExecStreamAsync is how you get a live shell in an arbitrary container. The
daemon upgrades the HTTP connection away from HTTP and the two ends then speak
the container's standard streams over it. With Tty set, the DAEMON ALLOCATES A
PSEUDO-TERMINAL INSIDE THE CONTAINER -- no host pty is involved, which is why
this needs no native dependency and behaves the same on every platform.

    public sealed class ExecSpec
    {
        public IReadOnlyList<string> Command       { get; set; }   // REQUIRED, >= 1 element
        public bool                  AttachStdin   { get; set; }   // default false
        public bool                  AttachStdout  { get; set; } = true;
        public bool                  AttachStderr  { get; set; } = true;
        public bool                  Tty           { get; set; }   // default false
        public int?                  ConsoleHeight { get; set; }   // rows, Tty only
        public int?                  ConsoleWidth  { get; set; }   // cols, Tty only
        public string                User          { get; set; }
        public string                WorkingDir    { get; set; }
        public IList<string>         Env           { get; set; } = [];   // "KEY=VALUE"
        public bool                  Privileged    { get; set; }
    }

ExecStreamAsync throws ArgumentException BEFORE touching the daemon when Command
is null or empty, when all three attach flags are false, or when either console
dimension is set to zero or less.

    public sealed class ContainerExecStream : IAsyncDisposable, IDisposable
    {
        public string ExecId                 { get; }  // feeds Resize/InspectExecAsync
        public bool   IsTty                  { get; }  // what was asked for
        public bool   UsesRawFraming         { get; }  // what the daemon answered with
        public bool   CanCloseStandardInput  { get; }  // transport capability

        public Task<ExecStreamReadResult> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default);
        public Task<ContainerLogs> ReadToEndAsync(
            CancellationToken cancellationToken = default);

        public Task WriteAsync(ReadOnlyMemory<byte> buffer,
                               CancellationToken cancellationToken = default);
        public Task WriteAsync(string text,
                               CancellationToken cancellationToken = default);  // UTF-8
        public Task WriteLineAsync(string text,
                                   CancellationToken cancellationToken = default); // UTF-8 + "\n"
        public Task CloseStandardInputAsync(
            CancellationToken cancellationToken = default);   // half-close

        public Task ResizeAsync(int height, int width,
                                CancellationToken cancellationToken = default);
        public Task<ExecInspectResult> InspectAsync(
            CancellationToken cancellationToken = default);
        public Task<long> WaitForExitAsync(
            CancellationToken cancellationToken = default);

        public void Dispose();
        public ValueTask DisposeAsync();
    }

    public readonly record struct ExecStreamReadResult(ExecStreamTarget Target, int Count)
    {
        public bool EndOfStream { get; }   // Count == 0
    }

    public enum ExecStreamTarget { None, StandardOutput, StandardError }

    public sealed class ExecInspectResult
    {
        public string Id          { get; init; }
        public bool   Running     { get; init; }
        public long?  ExitCode    { get; init; }
        public string ContainerId { get; init; }
        public long   Pid         { get; init; }
        public bool   OpenStdin   { get; init; }
        public bool   OpenStdout  { get; init; }
        public bool   OpenStderr  { get; init; }
        public bool   HasExited   { get; }    // !Running
    }

THE TWO FRAMINGS

                     Tty = true                  Tty = false
    Framing          raw, verbatim pty bytes     stdcopy frames
    Line endings     CRLF                        LF
    Input echo       yes (the pty echoes)        no
    ANSI escapes     yes                         no
    stdout/stderr    merged into one stream      kept apart
    Resize           honoured inside the box     not applicable

The library decides which framing to decode from the daemon's Content-Type
header, NOT from the Tty flag it asked for. UsesRawFraming reports what actually
came back. With a TTY, every ReadAsync reports ExecStreamTarget.StandardOutput
and ReadToEndAsync leaves ContainerLogs.Stderr empty -- that is the terminal
merging the streams, not a library shortcut. Turn the TTY off when the two must
stay apart.

THE ORDER OF OPERATIONS THAT WORKS

  1. ExecStreamAsync.
  2. Start reading, and keep reading. A command whose output nobody drains
     blocks once the daemon's buffer fills.
  3. Write input (WriteLineAsync sends LF, which both framings accept).
  4. ResizeAsync / ResizeExecAsync as the terminal changes size.
  5. Read to end of stream.
  6. THEN WaitForExitAsync or InspectExecAsync for the exit code. A hijacked
     connection carries bytes and nothing else; the exit code is not on it.

CloseStandardInputAsync shuts down the writing half of the connection, which is
how a command like `cat` sees end of file. Every transport a stock daemon
answers on supports it: Unix sockets and TCP shut the writing half down with
SHUT_WR, ssh:// closes the SSH child's standard input, and a Windows named pipe
sends the zero-length message the daemon reads as end of file. Still check
CanCloseStandardInput first -- it is a per-connection capability, and a pipe
that is not in message mode cannot carry the signal, so the call throws
NotSupportedException there and the session must be disposed instead.


IMAGES: ImageOperations
-----------------------

    Task<IReadOnlyList<ImageSummary>> ListAsync(
        bool all = false, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ImageSummary>> ListAsync(
        bool all, IDictionary<string, string> labelFilters,
        CancellationToken cancellationToken = default);

    Task<ImageInspectResult> InspectAsync(
        string reference, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ImageHistoryEntry>> GetHistoryAsync(
        string reference, CancellationToken cancellationToken = default);

    Task PullAsync(string reference, IProgress<string> progress = null,
                   CancellationToken cancellationToken = default);

    Task RemoveAsync(string reference, bool force = false,
                     CancellationToken cancellationToken = default);

    Task TagAsync(string sourceReference, string targetReference,
                  CancellationToken cancellationToken = default);

    Task PruneAsync(bool dangling = true,
                    CancellationToken cancellationToken = default);

    Task PruneAsync(bool dangling, IDictionary<string, string> labelFilters,
                    CancellationToken cancellationToken = default);

    Task<ImageBuildResult> BuildAsync(
        ImageBuildSpec spec, CancellationToken cancellationToken = default);

PullAsync tries an anonymous pull over the Engine API first and reports each
progress line to `progress`. If the registry refuses it for authentication
reasons, it automatically retries through `docker pull`, which picks up the
credential helpers configured on the machine -- and reports that it is doing so
through the same IProgress<string>. An {"error": ...} line inside an otherwise
successful response is raised as a DockerException.

BuildAsync shells out to `docker build` because BuildKit lives in the CLI; the
Engine API's own build endpoint is the legacy builder. Both streams are captured
in arrival order, so the BuildKit progress that normally goes to standard error
is in the result.

    public sealed class ImageBuildSpec
    {
        public string                      ContextDirectory { get; set; }  // REQUIRED
        public string                      DockerfilePath   { get; set; }  // default <ctx>/Dockerfile
        public IList<string>               Tags             { get; set; } = [];  // >= 1
        public IDictionary<string, string> BuildArgs        { get; set; } = ...;
        public string                      Target           { get; set; }  // multi-stage stage
        public bool                        Pull             { get; set; }
        public bool                        NoCache          { get; set; }
        public IDictionary<string, string> Labels           { get; set; } = ...;
        public IProgress<string>           Output           { get; set; }  // live build log
    }

    public sealed class ImageBuildResult
    {
        public string               ImageId      { get; init; }
        public IReadOnlyList<string> Tags        { get; init; }
        public string               Output       { get; init; }  // stdout+stderr interleaved
        public string               ShortImageId { get; }        // 12 hex digits, no prefix
    }

    public sealed class ImageSummary
    {
        public string                      Id                 { get; init; }
        public string                      ParentId           { get; init; }
        public IReadOnlyList<string>       RepoTags           { get; init; }
        public IReadOnlyList<string>       RepoDigests        { get; init; }
        public long                        CreatedUnixSeconds { get; init; }
        public long                        Size               { get; init; }
        public long                        SharedSize         { get; init; }
        public IReadOnlyDictionary<string, string> Labels     { get; init; }
        public long                        Containers         { get; init; }

        public DateTimeOffset? Created     { get; }
        public string          DisplayName { get; }   // first repo tag, or the short id
        public string          ShortId     { get; }
        public bool            IsDangling  { get; }   // no usable repo tag
    }

    public sealed class ImageInspectResult
    {
        public string                Id           { get; init; }
        public IReadOnlyList<string> RepoTags     { get; init; }
        public IReadOnlyList<string> RepoDigests  { get; init; }
        public string                Parent       { get; init; }
        public string                Comment      { get; init; }
        public DateTimeOffset?       Created      { get; init; }
        public string                Author       { get; init; }
        public string                Architecture { get; init; }
        public string                Os           { get; init; }
        public long                  Size         { get; init; }
        public ImageConfig           Config       { get; init; }
        public ImageRootFs           RootFs       { get; init; }

        public int    LayerCount  { get; }   // RootFs.Layers.Count, 0 when absent
        public string DisplayName { get; }
        public string ShortId     { get; }
    }

    public sealed class ImageConfig
    {
        public string                      User        { get; init; }
        public IReadOnlyList<string>       Env         { get; init; }
        public IReadOnlyList<string>       Cmd         { get; init; }
        public IReadOnlyList<string>       Entrypoint  { get; init; }
        public string                      WorkingDir  { get; init; }
        public IReadOnlyDictionary<string, JsonEmptyObject> ExposedPorts { get; init; }
        public IReadOnlyDictionary<string, string> Labels { get; init; }
        public HealthcheckSpec             Healthcheck { get; init; }
    }

    public sealed class ImageRootFs
    {
        public string                Type   { get; init; }   // "layers"
        public IReadOnlyList<string> Layers { get; init; }
    }

    public sealed class ImageHistoryEntry
    {
        public string                Id                 { get; init; }
        public long                  CreatedUnixSeconds { get; init; }
        public string                CreatedBy          { get; init; }  // the Dockerfile line
        public IReadOnlyList<string> Tags               { get; init; }
        public long                  Size               { get; init; }
        public string                Comment            { get; init; }

        public DateTimeOffset? Created      { get; }
        public bool            IsEmptyLayer { get; }   // Size == 0
    }


NETWORKS: NetworkOperations
---------------------------

    Task<string> CreateAsync(string name, string driver = "bridge",
                             IDictionary<string, string> labels = null,
                             CancellationToken cancellationToken = default);
        Returns the new network's id.

    Task<IReadOnlyList<NetworkSummary>> ListAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NetworkSummary>> ListAsync(
        IDictionary<string, string> labelFilters,
        CancellationToken cancellationToken = default);

    Task<NetworkInspectResult> InspectAsync(
        string idOrName, CancellationToken cancellationToken = default);

    Task RemoveAsync(string idOrName, CancellationToken cancellationToken = default);

    Task ConnectAsync(string network, string container,
                      IReadOnlyList<string> aliases = null,
                      CancellationToken cancellationToken = default);

    Task DisconnectAsync(string network, string container, bool force = false,
                         CancellationToken cancellationToken = default);

    Task PruneAsync(CancellationToken cancellationToken = default);

    Task PruneAsync(IDictionary<string, string> labelFilters,
                    CancellationToken cancellationToken = default);

    public sealed class NetworkSummary
    {
        public string           Name        { get; init; }
        public string           Id          { get; init; }
        public DateTimeOffset?  Created     { get; init; }
        public string           Scope       { get; init; }   // "local", "swarm"
        public string           Driver      { get; init; }   // "bridge", "host", "none"
        public bool             EnableIPv6  { get; init; }
        public bool             Internal    { get; init; }
        public bool             Attachable  { get; init; }
        public bool             Ingress     { get; init; }
        public NetworkIpam      Ipam        { get; init; }
        public IReadOnlyDictionary<string, string> Options { get; init; }
        public IReadOnlyDictionary<string, string> Labels  { get; init; }

        public string ShortId      { get; }
        public bool   IsPredefined { get; }   // bridge / host / none
    }

    public sealed class NetworkInspectResult   // NetworkSummary's fields, plus:
    {
        public IReadOnlyDictionary<string, NetworkContainerAttachment> Containers { get; init; }
        public string ShortId                 { get; }
        public int    AttachedContainerCount  { get; }
    }

    public sealed class NetworkContainerAttachment
    {
        public string Name        { get; init; }
        public string EndpointId  { get; init; }
        public string MacAddress  { get; init; }
        public string IPv4Address { get; init; }
        public string IPv6Address { get; init; }
    }

    public sealed class NetworkIpam
    {
        public string Driver { get; init; }
        public IReadOnlyDictionary<string, string> Options { get; init; }
        public IReadOnlyList<NetworkIpamConfig>    Config  { get; init; }
    }

    public sealed class NetworkIpamConfig
    {
        public string Subnet  { get; init; }
        public string IpRange { get; init; }
        public string Gateway { get; init; }
    }

Two containers on the same user-defined network reach each other by container
name and by any alias given through ContainerSpec.NetworkAliases or
ConnectAsync. Docker's embedded resolver answers those names; addresses are
visible in ContainerInspectResult.NetworkSettings.Networks[name].IpAddress.


VOLUMES: VolumeOperations
-------------------------

    Task<string> CreateAsync(string name = null,
                             IDictionary<string, string> labels = null,
                             CancellationToken cancellationToken = default);
        Returns the volume's name. Passing null asks the daemon for an
        anonymous volume with a generated name.

    Task<IReadOnlyList<VolumeSummary>> ListAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VolumeSummary>> ListAsync(
        IDictionary<string, string> labelFilters,
        CancellationToken cancellationToken = default);

    Task<VolumeInspectResult> InspectAsync(
        string name, CancellationToken cancellationToken = default);

    Task RemoveAsync(string name, bool force = false,
                     CancellationToken cancellationToken = default);

    Task PruneAsync(CancellationToken cancellationToken = default);

    Task PruneAsync(IDictionary<string, string> labelFilters,
                    CancellationToken cancellationToken = default);

    public sealed class VolumeSummary
    {
        public string          Name       { get; init; }
        public string          Driver     { get; init; }
        public string          Mountpoint { get; init; }   // host path
        public DateTimeOffset? CreatedAt  { get; init; }
        public IReadOnlyDictionary<string, string> Labels  { get; init; }
        public IReadOnlyDictionary<string, string> Options { get; init; }
        public string          Scope      { get; init; }
    }

    public sealed class VolumeInspectResult   // VolumeSummary's fields, plus:
    {
        public VolumeUsageData UsageData { get; init; }
    }

    public sealed class VolumeUsageData
    {
        public long Size       { get; init; }   // -1 when the daemon did not compute it
        public long RefCount   { get; init; }
        public bool IsComputed { get; }
    }


SYSTEM: SystemOperations
------------------------

    Task<bool> PingAsync(CancellationToken cancellationToken = default);
        Never throws for a Docker-side or transport problem; false means "not
        reachable", for any such reason. Cancelling the token you pass still
        raises OperationCanceledException.

    Task<DockerVersionInfo> GetVersionAsync(
        CancellationToken cancellationToken = default);

    Task<DockerSystemInfo> GetInfoAsync(
        CancellationToken cancellationToken = default);

    Task<DiskUsageInfo> GetDiskUsageAsync(
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<DockerEvent> StreamEventsAsync(
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<DockerEvent> StreamEventsAsync(
        string type, string containerIdOrName,
        CancellationToken cancellationToken = default);
        type filters on the event type ("container", "image", "network",
        "volume"); either argument may be null.

    Task EnsureLinuxDaemonAsync(CancellationToken cancellationToken = default);
        Throws DockerException when the daemon is not in Linux-container mode.

    public sealed class DockerVersionInfo
    {
        public string Version       { get; init; }
        public string ApiVersion    { get; init; }
        public string MinApiVersion { get; init; }
        public string Os            { get; init; }
        public string Arch          { get; init; }
        public string KernelVersion { get; init; }
        public string GitCommit     { get; init; }
        public string GoVersion     { get; init; }
        public string BuildTime     { get; init; }
        public bool   Experimental  { get; init; }
    }

    public sealed class DockerSystemInfo
    {
        public string Name              { get; init; }
        public string ServerVersion     { get; init; }
        public string OsType            { get; init; }   // "linux"
        public string OperatingSystem   { get; init; }
        public string KernelVersion     { get; init; }
        public string Architecture      { get; init; }
        public string CgroupVersion     { get; init; }   // "1" or "2"
        public string CgroupDriver      { get; init; }   // "systemd" or "cgroupfs"
        public string StorageDriver     { get; init; }
        public string LoggingDriver     { get; init; }
        public long   NCpu              { get; init; }
        public long   MemTotal          { get; init; }
        public long   Containers        { get; init; }
        public long   ContainersRunning { get; init; }
        public long   ContainersPaused  { get; init; }
        public long   ContainersStopped { get; init; }
        public long   Images            { get; init; }
        public bool   MemoryLimit       { get; init; }
        public bool   SwapLimit         { get; init; }   // false => OOM tests are unreliable
        public bool   CpuCfsQuota       { get; init; }
        public bool   PidsLimit         { get; init; }
        public IReadOnlyList<string> Warnings { get; init; }
    }

CgroupDriver and SwapLimit are worth reading before you rely on limits: see the
pitfalls about PidsStats.Limit and about swap.

    public sealed class DiskUsageInfo
    {
        public long LayersSizeBytes            { get; init; }
        public int  ImageCount                 { get; init; }
        public long ImagesSizeBytes            { get; init; }
        public int  ReclaimableImageCount      { get; init; }
        public int  ContainerCount             { get; init; }
        public long ContainersSizeBytes        { get; init; }
        public int  VolumeCount                { get; init; }
        public long VolumesSizeBytes           { get; init; }
        public int  ReclaimableVolumeCount     { get; init; }
        public long BuildCacheSizeBytes        { get; init; }
        public long ReclaimableBuildCacheBytes { get; init; }
        public long TotalSizeBytes             { get; }
    }

    public sealed class DockerEvent
    {
        public string           Type      { get; init; }   // "container", "image", ...
        public string           Action    { get; init; }   // "create", "start", "die", ...
        public DockerEventActor Actor     { get; init; }
        public string           Scope     { get; init; }
        public string           Status    { get; init; }   // legacy alias of Action
        public string           Id        { get; init; }   // legacy alias of Actor.Id
        public string           From      { get; init; }
        public long             Time      { get; init; }   // Unix seconds
        public long             TimeNano  { get; init; }
        public DateTimeOffset?  Timestamp { get; }         // from TimeNano, else Time
    }

    public sealed class DockerEventActor
    {
        public string Id { get; init; }
        public IReadOnlyDictionary<string, string> Attributes { get; init; }
        // "image", "name" and the container's labels
    }


DIAGNOSTICS: DiagnosticsOperations
----------------------------------
Every report carries the raw counters AND an Interpretation sentence written for
a human. Show the Interpretation; use the counters for logic.

    Task<CpuThrottlingReport> GetCpuThrottlingAsync(
        string idOrName, CancellationToken cancellationToken = default);

    Task<MemoryBreakdownReport> GetMemoryBreakdownAsync(
        string idOrName, CancellationToken cancellationToken = default);

    Task<OomReport> CheckOomAsync(
        string idOrName, CancellationToken cancellationToken = default);

    Task<HealthReport> GetHealthAsync(
        string idOrName, CancellationToken cancellationToken = default);

    Task WaitForHealthyAsync(string idOrName, TimeSpan timeout,
                             CancellationToken cancellationToken = default);

    Task<ContainerDiagnosticsReport> DiagnoseAsync(
        string idOrName, CancellationToken cancellationToken = default);
        All four reports in one call, plus a Summary that leads with the worst
        finding.

WaitForHealthyAsync polls the container's inspect state and returns as soon as
it reports healthy. It has THREE failure modes, and two of them are fail-fast:

    DockerException   the container defines no healthcheck at all, so it can
                      never report healthy -- raised on the FIRST poll
    DockerException   the container is neither running nor restarting, so it
                      can never become healthy -- raised as soon as that is true
    TimeoutException  the timeout expired; the message names the last health
                      status and the failing streak

    public sealed class CpuThrottlingReport
    {
        public string          ContainerName      { get; init; }
        public bool            HasLiveData        { get; init; }
        public long            Periods            { get; init; }
        public long            ThrottledPeriods   { get; init; }
        public long            ThrottledTimeNanos { get; init; }
        public double          ThrottleRatio      { get; init; }   // 0..1
        public ThrottleSeverity Severity          { get; init; }
        public string          Interpretation     { get; init; }
        public TimeSpan        ThrottledTime      { get; }
    }

    public enum ThrottleSeverity { None, Moderate, High, Critical }
    // None < 5%, Moderate 5-25%, High 25-75%, Critical > 75%

    public sealed class MemoryBreakdownReport
    {
        public string  ContainerName          { get; init; }
        public bool    HasLiveData            { get; init; }
        public long    UsageBytes             { get; init; }
        public long?   LimitBytes             { get; init; }  // CONFIGURED limit, null when none
        public long?   AnonBytes              { get; init; }  // application memory
        public long?   FileBytes              { get; init; }  // page cache, reclaimable
        public long?   KernelBytes            { get; init; }
        public double? UsagePercent           { get; init; }  // of the configured limit
        public double? EffectiveUsagePercent  { get; init; }  // anon / limit
        public bool    IsPageCacheDominated   { get; init; }
        public string  Interpretation         { get; init; }
    }

    public sealed class OomReport
    {
        public string          ContainerName    { get; init; }
        public bool            IsRunning        { get; init; }
        public bool            WasOomKilled     { get; init; }
        public long            ExitCode         { get; init; }
        public long            RestartCount     { get; init; }
        public DateTimeOffset? FinishedAt       { get; init; }
        public long?           MemoryLimitBytes { get; init; }
        public string          Interpretation   { get; init; }
    }

    public sealed class HealthReport
    {
        public string  ContainerName  { get; init; }
        public bool    HasHealthcheck { get; init; }
        public string  Status         { get; init; }   // starting|healthy|unhealthy
        public long    FailingStreak  { get; init; }
        public IReadOnlyList<ContainerHealthLogEntry> RecentLogs { get; init; }
        public string  Interpretation { get; init; }
        public bool    IsHealthy      { get; }
    }

    public sealed class ContainerDiagnosticsReport
    {
        public string                ContainerId   { get; init; }
        public string                ContainerName { get; init; }
        public string                Status        { get; init; }
        public bool                  IsRunning     { get; init; }
        public CpuThrottlingReport   CpuThrottling { get; init; }   // required
        public MemoryBreakdownReport Memory        { get; init; }   // required
        public OomReport             Oom           { get; init; }   // required
        public HealthReport          Health        { get; init; }   // required
        public string                Summary       { get; init; }
    }

HasLiveData on the CPU and memory reports distinguishes "no throttling / low
memory" from "the container is not running and there is nothing to measure".
Check it before drawing conclusions from a zero.


ADVISOR: AdvisorEngine
----------------------

    public sealed class AdvisorEngine
    {
        public static IReadOnlyList<string> RuleIds { get; }   // CB001 .. CB014

        public Task<IReadOnlyList<AdvisorFinding>> AnalyzeContainerAsync(
            string idOrName, CancellationToken cancellationToken = default);

        public Task<IReadOnlyList<AdvisorFinding>> AnalyzeAllContainersAsync(
            CancellationToken cancellationToken = default);
    }

    public sealed record AdvisorFinding(
        string          RuleId,          // "CB007"
        AdvisorSeverity Severity,
        string          ContainerName,   // friendly name, no leading slash
        string          Title,           // "No healthcheck defined"
        string          Detail,          // what was observed, with numbers
        string          Recommendation); // the exact property or flag to change

    public enum AdvisorSeverity { Info = 0, Warning = 1, Critical = 2 }

A rule that does not fire contributes nothing, so an empty list means a clean
bill of health. The rules themselves are internal; you cannot register your own.

    Id     Severity        Fires when
    -----  --------------  --------------------------------------------------
    CB001  Warning         No memory limit set (HostConfig.Memory == 0)
    CB002  Warning         Memory limit set but swap is not disabled
    CB003  Warning         No PID limit set -- fork-bomb exposure
    CB004  Info            No CPU limit set
    CB005  Warning/Critical CPU limit is throttling the workload
                           (> 25% Warning, > 75% Critical); running only
    CB006  Warning         Application memory is close to the limit
                           (anon >= 90% of the limit); running only
    CB007  Warning         No healthcheck defined, on image or container
    CB008  Warning         Container runs as root
    CB009  Info            Memory limit set without a reservation
    CB010  Critical        Container runs privileged
    CB011  Warning         Log driver has no size limit (json-file, no max-size)
    CB012  Warning         Container was OOM-killed
    CB013  Info            Memory usage is dominated by page cache;
                           running only, and needs at least 4 MB of cache
    CB014  Info            Image reference is not pinned (:latest or untagged)

CB005, CB006 and CB013 need live counters, so they are SKIPPED -- not failed --
for a container that is not running.


ANALYSIS: AnalysisOperations
----------------------------
Four industry tools, each run AS A CONTAINER by this library itself. Nothing is
installed on the host; the tool image is pulled on demand, the container is
labelled and is always removed in a finally block.

    public sealed class AnalysisOperations
    {
        public const string ToolLabelName  = "codebrix.docker.tool";
        public const string ToolLabelValue = "true";
        public const string ContainerNamePrefix = "codebrix-tool-";
        public const string DefaultTrivyCacheVolumeName = "codebrix-docker-trivy-cache";

        public string TrivyImage    { get; set; } = "aquasec/trivy:latest";
        public string DiveImage     { get; set; } = "wagoodman/dive:latest";
        public string HadolintImage { get; set; } = "hadolint/hadolint:latest";
        public string SlimImage     { get; set; } = "mintoolkit/mint:latest";

        public Task<TrivyScanResult> ScanImageAsync(
            string imageReference, TrivyScanOptions options = null,
            CancellationToken cancellationToken = default);

        public Task<DiveAnalysisResult> AnalyzeImageEfficiencyAsync(
            string imageReference, CancellationToken cancellationToken = default);

        public Task<HadolintResult> LintDockerfileAsync(
            string dockerfilePath, CancellationToken cancellationToken = default);

        public Task<SlimResult> OptimizeImageAsync(
            string imageReference, SlimOptions options = null,
            CancellationToken cancellationToken = default);   // EXPERIMENTAL
    }

The four public constants are there so your own cleanup can find what the
analysis tier created: every tool container carries the ToolLabelName label and
a name beginning with ContainerNamePrefix, and Trivy's database volume carries
the same label.

Trivy, Dive and Slim all need the Docker socket, which is bind-mounted into
their container through the Engine API's own mounts array. Hadolint does not --
it only reads a Dockerfile.

TRIVY -- vulnerability scanning

    public sealed class TrivyScanOptions
    {
        public string        ToolImage       { get; set; }   // overrides TrivyImage for one call
        public IList<string> Severities      { get; set; } = [];  // ["HIGH", "CRITICAL"]
        public bool          IgnoreUnfixed   { get; set; }
        public string        CacheVolumeName { get; set; } = AnalysisOperations.DefaultTrivyCacheVolumeName;
        public TimeSpan?     Timeout         { get; set; }
    }

    public sealed class TrivyScanResult
    {
        public string ImageReference { get; init; }   // required
        public string ArtifactName   { get; init; }
        public IReadOnlyList<TrivyVulnerability> Vulnerabilities { get; init; }  // required
        public IReadOnlyDictionary<string, int>  CountBySeverity { get; init; }  // required
        public int    Total    { get; }
        public long   ExitCode { get; init; }
        public int    CountOf(string severity);   // case-insensitive; 0 when absent
    }

    public sealed record TrivyVulnerability(
        string Id,               // "CVE-2026-40200"
        string PkgName,
        string InstalledVersion,
        string FixedVersion,
        string Severity,         // UNKNOWN|LOW|MEDIUM|HIGH|CRITICAL
        string Title)
    {
        public string Target { get; init; }   // the scan target it came from
        public bool   HasFix { get; }         // FixedVersion is not empty
    }

CountBySeverity omits severities with no findings, which is why CountOf exists.
Trivy's vulnerability database is downloaded into the named cache volume on the
first scan and reused afterwards, so only the first call is slow.

DIVE -- layer efficiency

    public sealed class DiveAnalysisResult
    {
        public string  ImageReference   { get; init; }   // required
        public double  EfficiencyScore  { get; init; }   // required, 0..1; 1 = nothing wasted
        public long    WastedBytes      { get; init; }   // required
        public long    TotalSizeBytes   { get; init; }
        public IReadOnlyList<DiveLayerInfo> Layers { get; init; }   // required, build order
        public long    ExitCode         { get; init; }
        public double  WastedPercent    { get; }
    }

    public sealed record DiveLayerInfo(int Index, long SizeBytes, string Command)
    {
        public string Digest { get; init; }
    }

A score below roughly 0.9 usually means files are written and then overwritten
or deleted in a later layer, so both copies stay in the image. Dive's
continuous-integration mode returns a non-zero exit code when an image fails its
built-in rules; that is a FINDING, not an error, and it appears in ExitCode
rather than as an exception.

HADOLINT -- Dockerfile linting

    public sealed class HadolintResult
    {
        public string DockerfilePath { get; init; }   // required
        public IReadOnlyList<HadolintFinding> Findings { get; init; }   // required
        public IReadOnlyDictionary<string, int> CountByLevel { get; init; }  // required
        public int    Total    { get; }
        public long   ExitCode { get; init; }
    }

    public sealed record HadolintFinding(string Code,    // "DL3008"
                                         string Level,   // style|info|warning|error
                                         int    Line,
                                         string Message)
    {
        public int Column { get; init; }
    }

CountByLevel is keyed by Hadolint's own lowercase level names, and levels with
no findings are absent.

SLIM / MINT -- experimental image minification

    public sealed class SlimOptions
    {
        public string        ToolImage           { get; set; }
        public string        OutputTag           { get; set; }   // default "<reference>.slim"
        public IList<string> HttpProbePaths      { get; set; } = [];
        public int           ContinueAfterSeconds { get; set; } = 1;
        public TimeSpan      Timeout             { get; set; } = TimeSpan.FromMinutes(10);
    }

    public sealed class SlimResult
    {
        public string  OriginalImage      { get; init; }   // required
        public string  OptimizedImage     { get; init; }   // required
        public bool    Succeeded          { get; init; }   // required
        public long    ExitCode           { get; init; }
        public long?   OriginalSizeBytes  { get; init; }
        public long?   OptimizedSizeBytes { get; init; }
        public string  Output             { get; init; }   // required, the tool's console log
        public double? SizeReduction      { get; }         // fraction saved, or null
    }

This one is genuinely EXPERIMENTAL, and the reason is in how it works: it runs
the container for ContinueAfterSeconds, watches which files are actually
touched, and rebuilds the image from those. Give a service long enough to start,
and set HttpProbePaths for an HTTP server so the optimizer exercises its routes
(each path becomes a --http-probe-cmd argument; with the list empty, probing is
disabled entirely, which is right for anything that is not an HTTP server).
Verify the result before shipping it.

The default tool image is mintoolkit/mint:latest. The project renamed itself
from "slim" to "mint"; the older dslim/slim repository is retired at version
1.40.11 and CANNOT talk to Docker 25 or later, because it negotiates Engine API
1.24 and the daemon's minimum is 1.40. If you deliberately need the old build on
an old daemon, assign AnalysisOperations.SlimImage, or SlimOptions.ToolImage for
a single call. The public type names keep the Slim* spelling.


EVERY PUBLIC TYPE, BY AREA
--------------------------
The package has exactly NINETY public types, all in the CodeBrix.Docker
namespace, and all of them are listed here. There is no hidden surface, and
nothing in this list is off-limits.

    ENTRY POINT (1)
        DockerClient

    OPERATIONS -- reached only through a DockerClient (8)
        ContainerOperations, ImageOperations, NetworkOperations,
        VolumeOperations, SystemOperations, DiagnosticsOperations,
        AdvisorEngine, AnalysisOperations

    EXCEPTIONS (5)
        DockerException, DockerApiException, DockerContainerNotFoundException,
        DockerImageNotFoundException, DockerCliException

    THINGS YOU CONSTRUCT -- specs, options and value types (12)
        DockerClientOptions, ContainerSpec, ResourceLimits, MountSpec,
        PortBinding, RestartPolicy, HealthcheckSpec, LogConfig, ExecSpec,
        ImageBuildSpec, TrivyScanOptions, SlimOptions

    ENUMS (5)
        MountKind, RestartPolicyKind, ExecStreamTarget, ThrottleSeverity,
        AdvisorSeverity

    THINGS YOU RECEIVE -- containers (19)
        ContainerSummary, ContainerInspectResult, ContainerState,
        ContainerConfig, ContainerHostConfig, HostRestartPolicy,
        ContainerNetworkSettings, ContainerEndpointSettings,
        ContainerMountPoint, ContainerPort, ContainerHealth,
        ContainerHealthLogEntry, ContainerLogs, ExecResult, ExecInspectResult,
        ExecStreamReadResult, ContainerExecStream, ContainerStats, PidsStats

    THINGS YOU RECEIVE -- statistics (7)
        CpuStats, CpuUsage, ThrottlingData, MemoryStats, BlkioStats,
        BlkioStatEntry, NetworkStats

    THINGS YOU RECEIVE -- images (7)
        ImageSummary, ImageInspectResult, ImageConfig, ImageRootFs,
        ImageHistoryEntry, ImageBuildResult, JsonEmptyObject

    THINGS YOU RECEIVE -- networks and volumes (8)
        NetworkSummary, NetworkInspectResult, NetworkContainerAttachment,
        NetworkIpam, NetworkIpamConfig, VolumeSummary, VolumeInspectResult,
        VolumeUsageData

    THINGS YOU RECEIVE -- daemon (5)
        DockerVersionInfo, DockerSystemInfo, DiskUsageInfo, DockerEvent,
        DockerEventActor

    THINGS YOU RECEIVE -- diagnostics and advisor (6)
        CpuThrottlingReport, MemoryBreakdownReport, OomReport, HealthReport,
        ContainerDiagnosticsReport, AdvisorFinding

    THINGS YOU RECEIVE -- analysis (7)
        TrivyScanResult, TrivyVulnerability, DiveAnalysisResult, DiveLayerInfo,
        HadolintResult, HadolintFinding, SlimResult

Only ContainerExecStream is disposable among the result types; the rest are
plain data and need no cleanup. DockerClient is the other IDisposable.


================================================================================

COMPLETE EXAMPLES
=================
Every example below was compiled and executed against a live Docker daemon
before it was written down. Each is a complete method body; the using
directives at the top of the file are:

    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using CodeBrix.Docker;


Example 1: Connect to the daemon and report what it is
------------------------------------------------------

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
    Console.WriteLine($"cgroup {info.CgroupVersion} via the {info.CgroupDriver} driver, storage {info.StorageDriver}");
    Console.WriteLine($"Containers: {info.ContainersRunning} running of {info.Containers}; images: {info.Images}");

    var usage = await client.System.GetDiskUsageAsync();
    Console.WriteLine($"Reclaimable: {usage.ReclaimableImageCount} image(s), {usage.ReclaimableVolumeCount} volume(s)");

    // Docker 29.7.2 (API 1.55) on linux/amd64
    // Host dellprecision7770: 24 CPUs, 63991 MB
    // cgroup 2 via the systemd driver, storage overlayfs
    // Containers: 0 running of 0; images: 12
    // Reclaimable: 12 image(s), 8 volume(s)


Example 2: Run a container, read its output, clean up
-----------------------------------------------------

    using var client = DockerClient.Create();

    await client.Images.PullAsync("alpine:latest");

    var id = await client.Containers.RunAsync(new ContainerSpec
    {
        Image = "alpine:latest",
        Name = "codebrix-readme-hello",
        Command = ["sh", "-c", "echo 'from the container'; echo 'and an error' >&2"],
        Labels = { ["codebrix.docker.readme"] = "true" },
    });

    var exitCode = await client.Containers.WaitForExitAsync(id);
    var logs = await client.Containers.GetLogsAsync(id);

    Console.WriteLine($"exit {exitCode}");
    Console.WriteLine("stdout: " + logs.Stdout.Trim());
    Console.WriteLine("stderr: " + logs.Stderr.Trim());

    await client.Containers.RemoveAsync(id, force: true);

    // exit 0
    // stdout: from the container
    // stderr: and an error


Example 3: Typed resource limits, and retuning them while it runs
-----------------------------------------------------------------

    using var client = DockerClient.Create();

    var id = await client.Containers.RunAsync(new ContainerSpec
    {
        Image = "alpine:latest",
        Command = ["sh", "-c", "while :; do :; done"],
        Labels = { ["codebrix.docker.readme"] = "true" },
        Limits = new ResourceLimits
        {
            Cpus = 0.25,
            MemoryBytes = ResourceLimits.Megabytes(64),
            MemorySwapBytes = ResourceLimits.Megabytes(64),   // == MemoryBytes disables swap
            MemoryReservationBytes = ResourceLimits.Megabytes(48),
            PidsLimit = 128,
        },
    });

    var before = await client.Containers.InspectAsync(id);
    Console.WriteLine($"cpus={before.HostConfig.Cpus} memory={before.HostConfig.Memory} " +
                      $"swapDisabled={before.HostConfig.IsSwapDisabled} pids={before.HostConfig.PidsLimit}");

    await client.Containers.UpdateResourcesAsync(id, new ResourceLimits { Cpus = 1.0 });

    var after = await client.Containers.InspectAsync(id);
    Console.WriteLine($"cpus after retune = {after.HostConfig.Cpus}");

    await client.Containers.RemoveAsync(id, force: true);

    // cpus=0.25 memory=67108864 swapDisabled=True pids=128
    // cpus after retune = 1


Example 4: Live statistics, one sample and then a stream
--------------------------------------------------------

    using var client = DockerClient.Create();

    var id = await client.Containers.RunAsync(new ContainerSpec
    {
        Image = "alpine:latest",
        Command = ["sh", "-c", "while :; do :; done"],
        Labels = { ["codebrix.docker.readme"] = "true" },
        Limits = new ResourceLimits { Cpus = 0.2, MemoryBytes = ResourceLimits.Megabytes(64) },
    });

    var stats = await client.Containers.GetStatsAsync(id);
    Console.WriteLine($"live={stats.HasLiveData} cpu={stats.CpuPercent():F1}% " +
                      $"mem={stats.MemoryPercent():F1}% throttled={stats.ThrottleRatio():P0} " +
                      $"pids={stats.PidsStats?.Current}");

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
    var samples = 0;
    try
    {
        await foreach (var sample in client.Containers.StreamStatsAsync(id, cts.Token))
        {
            samples++;
            Console.WriteLine($"  sample {samples}: cpu {sample.CpuPercent():F1}%");
        }
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("  stream cancelled after " + samples + " sample(s)");
    }

    await client.Containers.RemoveAsync(id, force: true);

    // live=True cpu=20.2% mem=0.7% throttled=91% pids=1
    //   sample 1: cpu %          <- the FIRST streamed sample has no delta: CpuPercent() is null
    //   sample 2: cpu 19.8%
    //   sample 3: cpu 20.2%
    //   sample 4: cpu 20.2%
    //   stream cancelled after 4 sample(s)


Example 5: Diagnose a container that ran out of memory
------------------------------------------------------

    using var client = DockerClient.Create();

    var id = await client.Containers.RunAsync(new ContainerSpec
    {
        Image = "alpine:latest",
        Command = ["sh", "-c", "dd if=/dev/zero of=/hog/fill bs=1M count=200"],
        Labels = { ["codebrix.docker.readme"] = "true" },
        Mounts = { MountSpec.Tmpfs("/hog", ResourceLimits.Megabytes(512)) },
        Limits = new ResourceLimits
        {
            MemoryBytes = ResourceLimits.Megabytes(64),
            MemorySwapBytes = ResourceLimits.Megabytes(64),
        },
    });

    await client.Containers.WaitForExitAsync(id);

    var oom = await client.Diagnostics.CheckOomAsync(id);
    Console.WriteLine($"OOM killed: {oom.WasOomKilled}, exit {oom.ExitCode}");
    Console.WriteLine(oom.Interpretation);

    var report = await client.Diagnostics.DiagnoseAsync(id);
    Console.WriteLine("summary: " + report.Summary);
    Console.WriteLine($"cpu severity {report.CpuThrottling.Severity}; " +
                      $"memory live={report.Memory.HasLiveData}; " +
                      $"healthcheck={report.Health.HasHealthcheck}");

    await client.Containers.RemoveAsync(id, force: true);

    // OOM killed: True, exit 137
    // Container 'distracted_mayer' was terminated by the kernel OOM killer (exit code 137)
    // at 2026-09-01 05:42:54Z and its memory limit is 64 MB; raise ResourceLimits.MemoryBytes
    // or fix the workload's memory growth.
    // cpu severity None; memory live=False; healthcheck=False

Filling a tmpfs larger than the memory limit is a reliable way to provoke a real
OOM kill, because tmpfs pages are charged to the container's memory cgroup and
cannot be reclaimed once swap is disabled.


Example 6: CPU throttling and the memory breakdown
--------------------------------------------------

    using var client = DockerClient.Create();

    var id = await client.Containers.RunAsync(new ContainerSpec
    {
        Image = "alpine:latest",
        Command = ["sh", "-c", "dd if=/dev/zero of=/tmp/f bs=1M count=40 2>/dev/null; while :; do :; done"],
        Labels = { ["codebrix.docker.readme"] = "true" },
        Limits = new ResourceLimits { Cpus = 0.05, MemoryBytes = ResourceLimits.Megabytes(128) },
    });

    await Task.Delay(TimeSpan.FromSeconds(3));

    var cpu = await client.Diagnostics.GetCpuThrottlingAsync(id);
    Console.WriteLine($"{cpu.ThrottledPeriods}/{cpu.Periods} periods throttled " +
                      $"({cpu.ThrottleRatio:P0}), severity {cpu.Severity}, " +
                      $"{cpu.ThrottledTime.TotalMilliseconds:F0} ms lost");
    Console.WriteLine(cpu.Interpretation);

    var memory = await client.Diagnostics.GetMemoryBreakdownAsync(id);
    Console.WriteLine($"usage {memory.UsageBytes} limit {memory.LimitBytes} " +
                      $"anon {memory.AnonBytes} file {memory.FileBytes} " +
                      $"usage% {memory.UsagePercent:F1} effective% {memory.EffectiveUsagePercent:F1} " +
                      $"pageCacheDominated={memory.IsPageCacheDominated}");
    Console.WriteLine(memory.Interpretation);

    await client.Containers.RemoveAsync(id, force: true);

    // 43/43 periods throttled (100%), severity Critical, 4333 ms lost
    // Container 'practical_fermat' was throttled in 100% of 43 CPU scheduling periods,
    // stalling for 4.33s in total; the CPU limit is far too restrictive for this workload
    // (the quota is 0.05 CPU) - raise ResourceLimits.Cpus or reduce the worker/thread count.
    // usage 43610112 limit 134217728 anon 61440 file 41943040 usage% 32.5 effective% 0.0
    //   pageCacheDominated=True
    // Of the 41.6 MB charged to container 'practical_fermat', 40 MB (96%) is reclaimable page
    // cache and only 60 KB is application memory, so the headline usage figure overstates real
    // demand - size ResourceLimits.MemoryBytes against the application figure.


Example 7: Healthchecks and WaitForHealthyAsync
-----------------------------------------------

    using var client = DockerClient.Create();

    var id = await client.Containers.RunAsync(new ContainerSpec
    {
        Image = "alpine:latest",
        Command = ["sh", "-c", "sleep 120"],
        Labels = { ["codebrix.docker.readme"] = "true" },
        Healthcheck = new HealthcheckSpec
        {
            Test = ["CMD-SHELL", "test -d /tmp"],
            Interval = TimeSpan.FromSeconds(1),
            Timeout = TimeSpan.FromSeconds(2),
            Retries = 3,
        },
    });

    await client.Diagnostics.WaitForHealthyAsync(id, TimeSpan.FromSeconds(30));

    var health = await client.Diagnostics.GetHealthAsync(id);
    Console.WriteLine($"hasHealthcheck={health.HasHealthcheck} status={health.Status} " +
                      $"healthy={health.IsHealthy} failingStreak={health.FailingStreak} " +
                      $"logEntries={health.RecentLogs.Count}");
    Console.WriteLine(health.Interpretation);

    await client.Containers.RemoveAsync(id, force: true);

    // hasHealthcheck=True status=healthy healthy=True failingStreak=0 logEntries=1
    // Container 'modest_villani' is passing its healthcheck.


Example 8: The advisor on a deliberately badly configured container
-------------------------------------------------------------------

    using var client = DockerClient.Create();

    var id = await client.Containers.RunAsync(new ContainerSpec
    {
        Image = "alpine:latest",
        Command = ["sh", "-c", "sleep 60"],
        Labels = { ["codebrix.docker.readme"] = "true" },
        Privileged = true,
    });

    var findings = await client.Advisor.AnalyzeContainerAsync(id);

    foreach (var f in findings.OrderByDescending(x => x.Severity))
    {
        Console.WriteLine($"[{f.Severity}] {f.RuleId} {f.Title}");
        Console.WriteLine("        " + f.Recommendation);
    }

    Console.WriteLine("rules shipped: " + string.Join(", ", AdvisorEngine.RuleIds));

    await client.Containers.RemoveAsync(id, force: true);

    // [Critical] CB010 Container runs privileged
    //         Set ContainerSpec.Privileged to false (drop docker run --privileged) and grant
    //         only the specific capabilities or device mounts the workload actually needs.
    // [Warning] CB001 No memory limit set
    // [Warning] CB003 No PID limit set
    // [Warning] CB007 No healthcheck defined
    // [Warning] CB008 Container runs as root
    // [Warning] CB011 Log driver has no size limit
    // [Info] CB004 No CPU limit set
    // [Info] CB014 Image reference is not pinned
    // rules shipped: CB001, CB002, CB003, CB004, CB005, CB006, CB007, CB008, CB009, CB010, CB011, CB012, CB013, CB014


Example 9: One-shot exec, and the shell an image does not ship
--------------------------------------------------------------

    using var client = DockerClient.Create();

    var id = await client.Containers.RunAsync(new ContainerSpec
    {
        Image = "alpine:latest",
        Command = ["sh", "-c", "sleep 120"],
        Labels = { ["codebrix.docker.readme"] = "true" },
    });

    // One-shot: run, buffer, return.
    var result = await client.Containers.ExecAsync(id, ["sh", "-c", "echo out; echo err >&2; exit 3"]);
    Console.WriteLine($"one-shot exit {result.ExitCode} succeeded={result.Succeeded}");
    Console.WriteLine("  stdout: " + result.Stdout.Trim() + " | stderr: " + result.Stderr.Trim());

    // A shell the image does not ship: no exception, no hang, exit code 127.
    await using (var missing = await client.Containers.ExecStreamAsync(id, new ExecSpec
    {
        Command = ["/bin/bash"],
    }))
    {
        var output = await missing.ReadToEndAsync();
        var inspect = await missing.InspectAsync();
        Console.WriteLine($"  missing shell: exit {inspect.ExitCode}, running={inspect.Running}");
        Console.WriteLine("    message on stdout: " + output.Stdout.Trim());
        Console.WriteLine("    stderr was: '" + output.Stderr.Trim() + "'");
    }

    await client.Containers.RemoveAsync(id, force: true);

    // one-shot exit 3 succeeded=False
    //   stdout: out | stderr: err
    //   missing shell: exit 127, running=False
    //     message on stdout: OCI runtime exec failed: exec failed: unable to start container
    //     process: exec: "/bin/bash": stat /bin/bash: no such file or directory
    //     stderr was: ''

Note where that message arrives: on STANDARD OUTPUT, not standard error. The
daemon upgraded the connection normally and then wrote the container runtime's
complaint onto the ordinary output stream before closing it.


Example 10: A live terminal session -- a read pump plus typed input
-------------------------------------------------------------------

    using var client = DockerClient.Create();

    var id = await client.Containers.RunAsync(new ContainerSpec
    {
        Image = "alpine:latest",
        Command = ["sh", "-c", "sleep 120"],
        Labels = { ["codebrix.docker.readme"] = "true" },
    });

    var shellPath = await PickShellAsync(client, id);
    Console.WriteLine("shell picked: " + (shellPath ?? "(none -- this image has no shell)"));

    await using var session = await client.Containers.ExecStreamAsync(id, new ExecSpec
    {
        Command = [shellPath],
        AttachStdin = true,
        Tty = true,
        ConsoleHeight = 24,
        ConsoleWidth = 80,
        Env = { "PS1=box$ " },
    });

    var screen = new StringBuilder();
    var pump = Task.Run(async () =>
    {
        var buffer = new byte[4096];
        while (true)
        {
            var read = await session.ReadAsync(buffer);
            if (read.EndOfStream)
            {
                break;
            }

            // read.Target is StandardOutput for every chunk of a TTY session.
            screen.Append(Encoding.UTF8.GetString(buffer, 0, read.Count));
        }
    });

    await session.WriteLineAsync("stty size");
    await Task.Delay(300);
    await client.Containers.ResizeExecAsync(session.ExecId, height: 40, width: 120);
    await Task.Delay(300);
    await session.WriteLineAsync("stty size");
    await Task.Delay(300);
    await session.WriteLineAsync("exit 7");

    await pump;
    var exitCode = await session.WaitForExitAsync();

    Console.WriteLine("exit " + exitCode);
    foreach (var line in screen.ToString().Replace("\u001b", "\\e").Split("\r\n"))
    {
        Console.WriteLine("  | " + line);
    }

    await client.Containers.RemoveAsync(id, force: true);

...with this helper, which is the correct way to find a shell in an arbitrary
image -- run one and look at the exit code, rather than trusting the image's
reputation:

    // Probe for a usable shell: run it and look for exit code 127.
    private static async Task<string> PickShellAsync(DockerClient client, string containerId)
    {
        foreach (var candidate in new[] { "/bin/bash", "/bin/sh", "/bin/ash", "/busybox/sh" })
        {
            var probe = await client.Containers.ExecAsync(containerId, [candidate, "-c", "exit 0"]);
            if (probe.ExitCode != 127)
            {
                return candidate;
            }
        }

        return null;
    }

    // shell picked: /bin/sh
    // exit 7
    //   | stty size
    //   | box$ stty size
    //   | 24 80
    //   | box$ \e[6nbox$ \e[Jstty size
    //   | 40 120
    //   | box$ \e[6nexit 7
    //   |

The \e[6n and \e[J in that transcript are real ANSI escape sequences -- a cursor
position request and an erase-to-end-of-display -- which BusyBox's ash emits only
when it believes it is talking to a genuine terminal. `stty size` inside the
container reports 24 80 and then 40 120, so the resize really reached the
process. Feed this byte stream to a VT emulator to render it.


Example 11: Images -- pull with progress, inspect, history, tag
---------------------------------------------------------------

    using var client = DockerClient.Create();

    var lines = 0;
    var progress = new Progress<string>(_ => lines++);
    await client.Images.PullAsync("alpine:3.19", progress);
    Console.WriteLine($"pull reported {lines} progress line(s)");

    var image = await client.Images.InspectAsync("alpine:3.19");
    Console.WriteLine($"{image.DisplayName} {image.ShortId} {image.Size} bytes, " +
                      $"{image.Architecture}/{image.Os}, {image.LayerCount} layer(s)");

    var history = await client.Images.GetHistoryAsync("alpine:3.19");
    Console.WriteLine($"history: {history.Count} entr(ies), " +
                      $"{history.Count(h => h.IsEmptyLayer)} of them empty layers");

    await client.Images.TagAsync("alpine:3.19", "codebrix-readme/alpine:tagged");
    var tagged = await client.Images.ListAsync();
    Console.WriteLine("tagged copy present: " +
                      tagged.Any(i => i.RepoTags != null && i.RepoTags.Contains("codebrix-readme/alpine:tagged")));

    await client.Images.RemoveAsync("codebrix-readme/alpine:tagged", force: true);

    // pull reported 3 progress line(s)
    // alpine:3.19 6baf43584bcb 3429495 bytes, amd64/linux, 1 layer(s)
    // history: 2 entr(ies), 1 of them empty layers
    // tagged copy present: True


Example 12: Build an image from a Dockerfile
--------------------------------------------

    using var client = DockerClient.Create();

    var context = Path.Combine(Path.GetTempPath(), "codebrix-readme-build");
    Directory.CreateDirectory(context);
    await File.WriteAllTextAsync(Path.Combine(context, "Dockerfile"),
        """
        FROM alpine:3.19 AS base
        ARG GREETING=hello
        RUN echo "$GREETING" > /greeting.txt

        FROM base AS final
        CMD ["cat", "/greeting.txt"]
        """);

    var build = await client.Images.BuildAsync(new ImageBuildSpec
    {
        ContextDirectory = context,
        Tags = { "codebrix-readme/built:latest" },
        BuildArgs = { ["GREETING"] = "built by CodeBrix.Docker" },
        Target = "final",
        Labels = { ["codebrix.docker.readme"] = "true" },
    });

    Console.WriteLine($"built {build.ShortImageId} tagged {string.Join(", ", build.Tags)}");
    Console.WriteLine("build log lines: " + build.Output.Split('\n').Length);

    var id = await client.Containers.RunAsync(new ContainerSpec
    {
        Image = "codebrix-readme/built:latest",
        Labels = { ["codebrix.docker.readme"] = "true" },
    });
    await client.Containers.WaitForExitAsync(id);
    Console.WriteLine("container said: " + (await client.Containers.GetLogsAsync(id)).Stdout.Trim());

    await client.Containers.RemoveAsync(id, force: true);
    await client.Images.RemoveAsync("codebrix-readme/built:latest", force: true);
    Directory.Delete(context, recursive: true);

    // built 5f828c92e01e tagged codebrix-readme/built:latest
    // build log lines: 32
    // container said: built by CodeBrix.Docker


Example 13: A private network, a named volume, and label-scoped cleanup
-----------------------------------------------------------------------

    using var client = DockerClient.Create();
    var labels = new Dictionary<string, string> { ["codebrix.docker.readme"] = "true" };

    var networkId = await client.Networks.CreateAsync("codebrix-readme-net", "bridge", labels);
    var volumeName = await client.Volumes.CreateAsync("codebrix-readme-vol", labels);

    var writer = await client.Containers.RunAsync(new ContainerSpec
    {
        Image = "alpine:latest",
        Name = "codebrix-readme-writer",
        Command = ["sh", "-c", "echo 'shared state' > /data/note.txt; sleep 30"],
        Labels = { ["codebrix.docker.readme"] = "true" },
        Mounts = { MountSpec.Volume(volumeName, "/data") },
        NetworkName = "codebrix-readme-net",
        NetworkAliases = { "writer" },
    });

    await Task.Delay(TimeSpan.FromSeconds(1));

    var reader = await client.Containers.RunAsync(new ContainerSpec
    {
        Image = "alpine:latest",
        Command = ["sh", "-c", "cat /data/note.txt; ping -c 1 -W 2 writer > /dev/null && echo 'writer resolved'"],
        Labels = { ["codebrix.docker.readme"] = "true" },
        Mounts = { MountSpec.Volume(volumeName, "/data", readOnly: true) },
        NetworkName = "codebrix-readme-net",
    });

    await client.Containers.WaitForExitAsync(reader);
    Console.WriteLine((await client.Containers.GetLogsAsync(reader)).Stdout.Trim());

    var inspect = await client.Networks.InspectAsync(networkId);
    Console.WriteLine($"network {inspect.Name} ({inspect.Driver}) has " +
                      $"{inspect.AttachedContainerCount} attached container(s)");

    // Tear down exactly what this code created, and nothing else.
    foreach (var c in await client.Containers.ListAsync(all: true, labelFilters: labels))
    {
        await client.Containers.RemoveAsync(c.Id, force: true);
    }
    await client.Volumes.PruneAsync(labels);
    await client.Networks.PruneAsync(labels);

    Console.WriteLine("volumes left with our label: " + (await client.Volumes.ListAsync(labels)).Count);
    Console.WriteLine("networks left with our label: " + (await client.Networks.ListAsync(labels)).Count);

    // shared state
    // writer resolved
    // network codebrix-readme-net (bridge) has 1 attached container(s)
    // volumes left with our label: 0
    // networks left with our label: 0

Label everything you create, and prune by label. That pattern is what makes
teardown total and scoped: the label-filtered PruneAsync overloads never touch
anything you did not label.


Example 14: Scan, score and lint (containerized tools)
------------------------------------------------------

    using var client = DockerClient.Create();

    var scan = await client.Analysis.ScanImageAsync("alpine:3.19", new TrivyScanOptions
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

    var dive = await client.Analysis.AnalyzeImageEfficiencyAsync("alpine:3.19");
    Console.WriteLine($"efficiency {dive.EfficiencyScore:P1}, {dive.WastedBytes} wasted bytes " +
                      $"({dive.WastedPercent:P2}) across {dive.Layers.Count} layer(s)");

    var dockerfile = Path.Combine(Path.GetTempPath(), "codebrix-readme.Dockerfile");
    await File.WriteAllTextAsync(dockerfile,
        """
        FROM alpine
        RUN apk add curl
        """);

    var lint = await client.Analysis.LintDockerfileAsync(dockerfile);
    Console.WriteLine($"hadolint: {lint.Total} finding(s)");
    foreach (var f in lint.Findings)
    {
        Console.WriteLine($"  line {f.Line} {f.Level,-7} {f.Code}: {f.Message}");
    }

    File.Delete(dockerfile);

    // alpine:3.19: 2 vulnerabilit(ies), 0 critical, 2 high
    //   HIGH     CVE-2026-40200 in musl 1.2.4_git20230717-r5 -> fixed in 1.2.4_git20230717-r6
    //   HIGH     CVE-2026-40200 in musl-utils 1.2.4_git20230717-r5 -> fixed in ...-r6
    // efficiency 100.0%, 0 wasted bytes (0.00%) across 1 layer(s)
    // hadolint: 3 finding(s)
    //   line 1 warning DL3006: Always tag the version of an image explicitly
    //   line 2 warning DL3018: Pin versions in apk add. ...
    //   line 2 info    DL3019: Use the `--no-cache` switch ...


Example 15: Watch the daemon's event stream while something happens
--------------------------------------------------------------------

    using var client = DockerClient.Create();

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var seen = new List<string>();

    var watcher = Task.Run(async () =>
    {
        try
        {
            await foreach (var evt in client.System.StreamEventsAsync("container", null, cts.Token))
            {
                seen.Add($"{evt.Type}/{evt.Action} {evt.Actor?.Attributes?.GetValueOrDefault("image")}");
                if (seen.Count >= 4)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The stream ends when the token is cancelled; that is the normal way to stop it.
        }
    });

    await Task.Delay(500);
    var id = await client.Containers.RunAsync(new ContainerSpec
    {
        Image = "alpine:latest",
        Command = ["true"],
        Labels = { ["codebrix.docker.readme"] = "true" },
    });
    await client.Containers.WaitForExitAsync(id);
    await client.Containers.RemoveAsync(id, force: true);

    await watcher;
    foreach (var line in seen)
    {
        Console.WriteLine("  event: " + line);
    }

    //   event: container/create alpine:latest
    //   event: container/start alpine:latest
    //   event: container/die alpine:latest
    //   event: container/destroy alpine:latest


Example 16: Reach a remote daemon over ssh://
---------------------------------------------
Everything in the library works over ssh:// exactly as it does locally --
including interactive exec, standard input and its half-close.

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

    Console.WriteLine("ping:    " + await remote.System.PingAsync());
    var version = await remote.System.GetVersionAsync();
    Console.WriteLine($"version: Docker {version.Version} (API {version.ApiVersion})");
    var containers = await remote.Containers.ListAsync();
    Console.WriteLine($"running: {containers.Count} container(s) on the remote daemon");

    var someContainerId = containers[0].Id;

    var probe = await remote.Containers.ExecAsync(someContainerId, ["sh", "-c", "echo hello from over ssh"]);
    Console.WriteLine("exec:    " + probe.Stdout.Trim());

    await using (var shell = await remote.Containers.ExecStreamAsync(someContainerId, new ExecSpec
    {
        Command = ["sh"],
        AttachStdin = true,
    }))
    {
        Console.WriteLine("half-close available over ssh: " + shell.CanCloseStandardInput);
        await shell.WriteLineAsync("echo streamed over ssh");
        await shell.CloseStandardInputAsync();
        var output = await shell.ReadToEndAsync();
        Console.WriteLine("stream:  " + output.Stdout.Trim());
    }

    // ping:    True
    // version: Docker 29.7.2 (API 1.55)
    // running: 1 container(s) on the remote daemon
    // exec:    hello from over ssh
    // half-close available over ssh: True
    // stream:  streamed over ssh

The same thing with no options at all is DOCKER_HOST=ssh://root@build-01:2222
plus a plain DockerClient.Create(); the SSH client then reads ~/.ssh/config and
~/.ssh/known_hosts as usual.


Example 17: The error model, and what an unreachable remote reports
--------------------------------------------------------------------

    using var client = DockerClient.Create();
    Console.WriteLine("endpoint: " + client.Endpoint);

    try
    {
        await client.Containers.InspectAsync("no-such-container-anywhere");
    }
    catch (DockerContainerNotFoundException ex)
    {
        Console.WriteLine($"container 404: {ex.StatusCode} -- {ex.Message}");
    }

    try
    {
        await client.Images.InspectAsync("codebrix-readme/definitely-not-here:v9");
    }
    catch (DockerImageNotFoundException ex)
    {
        Console.WriteLine($"image 404: {ex.StatusCode} -- {ex.Message}");
    }

    // https:// is not supported; ssh:// is the supported way to a remote daemon.
    try
    {
        using var tls = DockerClient.Create(new DockerClientOptions { Endpoint = "https://build-01:2376" });
    }
    catch (NotSupportedException ex)
    {
        Console.WriteLine("https rejected: " + ex.Message);
    }

    using var remote = DockerClient.Create(new DockerClientOptions
    {
        Endpoint = "ssh://deploy@build-01.invalid:2222",
        SshArguments = { "-i", "/keys/deploy" },
    });
    Console.WriteLine("remote ping (never throws): " + await remote.System.PingAsync());
    try
    {
        await remote.System.GetVersionAsync();
    }
    catch (DockerException ex)
    {
        Console.WriteLine("remote version failed: " + ex.Message);
    }

    // endpoint: unix:///var/run/docker.sock
    // container 404: NotFound -- No such container: no-such-container-anywhere
    // image 404: NotFound -- No such image: codebrix-readme/definitely-not-here:v9
    // https rejected: TLS-secured Docker endpoints (https://) are not supported in this
    //   version of CodeBrix.Docker.
    // remote ping (never throws): False
    // remote version failed: The SSH connection to 'deploy@build-01.invalid' on port 2222
    //   could not be established. Check that the host is reachable and that its SSH service
    //   is listening on that port. The SSH client exited with code 255 and reported: ...

The transport's own message is what surfaces, not a generic "an error occurred
while sending the request". Show it to the user; it is written to be actionable.


================================================================================

MINIMUM VIABLE PROJECT TEMPLATE
===============================
A complete, working console application. Both files as shown compile and run.

MyDockerTool.csproj

    <Project Sdk="Microsoft.NET.Sdk">

      <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>net10.0</TargetFramework>
        <Nullable>disable</Nullable>
        <ImplicitUsings>disable</ImplicitUsings>
      </PropertyGroup>

      <ItemGroup>
        <PackageReference Include="CodeBrix.Docker.MitLicenseForever" />
      </ItemGroup>

    </Project>

Program.cs

    using System;
    using System.Threading.Tasks;
    using CodeBrix.Docker;

    namespace MyDockerTool;

    internal static class Program
    {
        private static async Task<int> Main()
        {
            using var client = DockerClient.Create();

            if (!await client.System.PingAsync())
            {
                Console.Error.WriteLine("No Docker daemon at " + client.Endpoint);
                return 1;
            }

            await client.Images.PullAsync("alpine:latest");

            var id = await client.Containers.RunAsync(new ContainerSpec
            {
                Image = "alpine:latest",
                Command = ["sh", "-c", "echo hello from CodeBrix.Docker"],
                Labels = { ["my-tool"] = "true" },
                Limits = new ResourceLimits
                {
                    Cpus = 0.5,
                    MemoryBytes = ResourceLimits.Megabytes(128),
                },
            });

            try
            {
                var exitCode = await client.Containers.WaitForExitAsync(id);
                var logs = await client.Containers.GetLogsAsync(id);
                Console.WriteLine(logs.Stdout.TrimEnd());
                return (int)exitCode;
            }
            finally
            {
                await client.Containers.RemoveAsync(id, force: true);
            }
        }
    }

    // hello from CodeBrix.Docker

Nullable and ImplicitUsings are disabled above only to match the library's own
conventions; the package works perfectly well from a project that enables
either. The library's reference types are not annotated, so a nullable-enabled
consumer sees them as oblivious rather than as non-null promises -- treat
anything documented here as "may be null" as genuinely nullable.


================================================================================

PERFORMANCE TIPS
================

1. CREATE ONE DockerClient AND KEEP IT. It owns a pooled HttpClient with a
   two-minute idle connection timeout. Over a Unix socket a fresh client is
   merely wasteful; over ssh:// every new HTTP connection pays a whole SSH
   handshake, so a per-call client is dramatically slower than a long-lived one.

2. PREFER ListAsync OVER InspectAsync WHEN A SUMMARY WILL DO. ContainerSummary
   carries id, names, image, state, status, labels and ports and comes from one
   request for the whole machine. Inspecting N containers is N requests.

3. FILTER SERVER-SIDE. Every ListAsync and PruneAsync that accepts labelFilters
   sends the filter to the daemon, so it never materialises objects you are
   about to discard. Filtering a full list in LINQ costs a bigger response and
   more allocation.

4. STREAM STATS RATHER THAN POLLING. StreamStatsAsync holds one connection and
   yields a sample about once a second. Calling GetStatsAsync in a loop opens a
   request per sample, and each one costs the daemon a full cgroup read.

5. THE FIRST STREAMED STATS SAMPLE HAS NO CPU PERCENTAGE. CpuPercent() needs a
   previous sample to compute a delta, and the first frame has none, so it
   returns null. Skip it rather than treating it as zero.

6. ONE Diagnostics.DiagnoseAsync BEATS FOUR SEPARATE CALLS. It gathers one
   inspect and one stats sample and computes all four reports from them; calling
   GetCpuThrottlingAsync, GetMemoryBreakdownAsync, CheckOomAsync and
   GetHealthAsync separately repeats that work four times.

7. THE ADVISOR IS PER-CONTAINER WORK. AnalyzeAllContainersAsync inspects and
   samples every container it finds, so on a busy host it is proportionally
   expensive. Run it on demand, not on a timer.

8. GIVE TRIVY ITS CACHE VOLUME. TrivyScanOptions.CacheVolumeName defaults to a
   shared named volume that holds the vulnerability database. The first scan
   downloads it; every scan afterwards reuses it. Do not point different scans
   at different volume names unless you mean to.

9. THE ANALYSIS TIER PULLS IMAGES. The first ScanImageAsync,
   AnalyzeImageEfficiencyAsync, LintDockerfileAsync or OptimizeImageAsync pulls
   its tool image. Warm them with Images.PullAsync at startup if a first-call
   stall matters. OptimizeImageAsync is in a class of its own for cost -- it
   runs the target container and rebuilds the image.

10. TAIL YOUR LOG READS. GetLogsAsync with tail = null returns the container's
    whole history as two strings in memory. Pass a tail count for anything
    long-running.

11. USE A CancellationToken FOR EVERY STREAM. Streaming calls are deliberately
    exempt from DefaultTimeout, so a token is the only thing that will stop
    StreamStatsAsync, StreamEventsAsync, GetLogsAsync or an exec session.

12. DRAIN AN EXEC STREAM WHILE THE COMMAND RUNS. Output nobody reads blocks the
    command once the daemon's buffer fills. Read on one task and write on
    another, as in Example 10 -- do not write everything and then read.


================================================================================

COMMON PITFALLS TO AVOID
========================

1. DO NOT confuse the package id with the namespace.
   Package  : CodeBrix.Docker.MitLicenseForever
   Namespace: CodeBrix.Docker  (one namespace, all ninety public types)

2. DO NOT write "using CodeBrix.Docker.Containers;" or
   "using CodeBrix.Docker.Diagnostics;" or "using CodeBrix.Docker.Analysis;".
   Those are FOLDERS in the repository, not namespaces, and any such using is a
   CS0246 compile error. There is deliberately no CodeBrix.Docker.System
   namespace either.

3. DO NOT expect CreateAsync or RunAsync to pull a missing image. They do not.
   A missing image is a DockerImageNotFoundException. Call
   Images.PullAsync(reference) first -- it is a no-op when the image is already
   local.

4. DO NOT treat a missing shell as an exception. Asking for /bin/bash in an
   image that has none (alpine, busybox and the official redis alpine images
   all lack it) still gets a successful stream upgrade. The daemon writes
   "OCI runtime exec failed: ... no such file or directory" ON STANDARD OUTPUT
   and closes; InspectExecAsync then reports exit code 127. Probe for a shell by
   running one and checking for 127, as in Example 10's PickShellAsync.

5. DO NOT look for the exec exit code on the stream. A hijacked connection
   carries bytes and nothing else. Read the output to end of stream, THEN call
   WaitForExitAsync or InspectExecAsync.

6. DO NOT expect stderr from a TTY session. With ExecSpec.Tty = true the
   terminal merges both streams: ContainerLogs.Stderr is always empty and every
   ExecStreamReadResult.Target is StandardOutput. That is the terminal, not a
   library shortcut. Set Tty = false when the two must stay apart.

7. DO NOT call CloseStandardInputAsync without checking
   ContainerExecStream.CanCloseStandardInput. Unix sockets, TCP, ssh:// and a
   Windows named pipe all support the half-close against a stock daemon, but it
   remains a per-connection capability -- a pipe that is not in message mode
   cannot carry the signal and the call throws NotSupportedException there.
   Dispose the session instead.

8. DO NOT read PidsStats.Limit to decide whether a container is CAPPED. What it
   reports depends on the daemon's cgroup driver. Under the SYSTEMD driver an
   uncapped container inherits the systemd scope's TasksMax -- a large, finite,
   perfectly real number (76464 on a stock systemd whose kernel threads-max is
   509764). Under the cgroupfs driver the same container reports the
   "unlimited" sentinel, which the library surfaces as null.
   ContainerHostConfig.PidsLimit -- the CONFIGURED limit -- is the property that
   answers "is this capped?". Read DockerSystemInfo.CgroupDriver if you need to
   know which world you are in.

9. DO NOT expect ThrottlingData.ThrottleRatio() to return null when nothing was
   throttled. It returns 0 when Periods is zero or negative. It returns null in
   two cases only: when Periods itself is missing, and when Periods is positive
   but ThrottledPeriods is missing. "0" therefore means "measured, no
   throttling"; use ContainerStats.HasLiveData or
   CpuThrottlingReport.HasLiveData to detect "not running, nothing measured".

10. DO NOT confuse MemoryBreakdownReport.LimitBytes with MemoryStats.Limit. The
    report's LimitBytes is the container's CONFIGURED limit (HostConfig.Memory),
    and is null when none is set. MemoryStats.Limit is the CGROUP limit, which
    for an unlimited container is the host's total memory. That is why
    UsagePercent and EffectiveUsagePercent are null for an unlimited container
    rather than a meaningless percentage of host RAM.

11. DO NOT read a big memory number as pressure without the breakdown. Page
    cache is charged to the container and is reclaimable.
    MemoryBreakdownReport.AnonBytes is the application memory that actually
    matters; IsPageCacheDominated flags the case where the headline figure lies.

12. DO NOT test liveness by checking whether a stats field is non-null. Stats
    for a stopped container come back with an empty memory_stats object and
    all-zero CPU counters -- present, but meaningless. ContainerStats.HasLiveData
    is the correct test.

13. DO NOT call Volumes.PruneAsync() with no filters and expect named volumes to
    go. Since Engine API 1.42 the daemon requires an explicit opt-in before it
    will consider named volumes, so the no-filter overload prunes ANONYMOUS
    volumes only -- deliberately, because an unfiltered sweep over named volumes
    destroys user data. The label-filtered overload does reclaim named volumes,
    but only ones carrying your labels. Calling it with an empty filter falls
    back to anonymous-only.

14. DO NOT forget that WaitForHealthyAsync fails FAST as well as slow. Besides
    TimeoutException on expiry it throws DockerException immediately when the
    container defines no healthcheck at all, and as soon as the container is
    neither running nor restarting -- because in both cases it can never become
    healthy.

15. DO NOT expect not-found subclasses for networks and volumes. A 404 on a
    container route is DockerContainerNotFoundException and on an image route
    DockerImageNotFoundException, but networks and volumes surface a plain
    DockerApiException with StatusCode == NotFound.

16. DO NOT compare ImageBuildResult.ImageId against a registry manifest digest.
    On a modern buildx with the containerd image store, a plain build emits an
    attestation manifest and an OCI image INDEX, and the id resolved from the
    built tag is the INDEX digest, not the image-manifest digest. It is correct
    and usable as a local reference -- `docker image inspect` accepts it -- but
    it will not equal a digest obtained from a registry manifest or from an
    older daemon. Compare by tag, or index digest to index digest.

17. DO NOT assume the analysis tier is free of the CLI. ScanImageAsync needs no
    CLI, but AnalyzeImageEfficiencyAsync and LintDockerfileAsync move files in
    and out of their tool container with `docker cp`, so they need the docker
    executable on PATH (DockerClientOptions.DockerCliPath). Images.BuildAsync
    and an authenticated Images.PullAsync are the other two.

18. DO NOT rely on DockerClientOptions.SshArguments reaching the CLI-backed
    operations. Over an ssh:// endpoint, BuildAsync and a credentialled
    PullAsync run the DOCKER CLI, which makes its OWN ssh invocation: it does
    not see SshArguments, and it reads the invoking user's ~/.ssh/config and
    known_hosts (OpenSSH resolves "~" from the passwd entry, so HOME cannot
    redirect it). If you build images over ssh://, the remote host must be in
    your own known_hosts with your key reachable by your own SSH configuration.
    Everything on the Engine API path has no such requirement.

19. DO NOT set StrictHostKeyChecking=no through SshArguments to get past an
    unknown host. That is a real security downgrade. Connect to the host once by
    hand so OpenSSH records the key, or point UserKnownHostsFile at a file you
    manage.

20. DO NOT expect an https:// endpoint to work. It throws NotSupportedException
    at client creation. TLS to a Docker daemon needs a certificate authority and
    a matched server/client certificate pair; ssh:// is the supported route to a
    remote daemon and needs none of it.

21. DO NOT create a DockerClient per operation. See PERFORMANCE TIPS 1 -- and
    note the ssh:// case, where it is not a micro-optimization.

22. DO NOT let an open-ended call run without a CancellationToken.
    DefaultTimeout deliberately does not apply to logs, stats streams, events or
    exec sessions -- nor to the non-streaming calls that are open-ended by
    nature: Containers.StopAsync, Containers.RestartAsync,
    Containers.WaitForExitAsync, Containers.PruneAsync, Images.PruneAsync,
    Networks.PruneAsync and both Volumes.PruneAsync overloads.

23. DO NOT set MemoryBytes without MemorySwapBytes when you want a deterministic
    OOM kill. With swap still available the container slides rather than dies.
    Set MemorySwapBytes equal to MemoryBytes to disable swap -- and check
    DockerSystemInfo.SwapLimit, because on a host without swap accounting the
    daemon cannot enforce it at all.

24. DO NOT parse Interpretation strings. Every diagnostics report gives you the
    raw counters beside the sentence; branch on those. The Interpretation is for
    display and its wording is not a contract.

25. DO NOT hold a container id after RemoveAsync. Later calls with it raise
    DockerContainerNotFoundException, which is correct but is easy to mistake
    for a transport problem.

26. DO NOT target .NET versions below 10.0.


================================================================================

WHAT THIS PACKAGE DOES NOT DO
=============================

Do NOT reach for this package to:

  - Talk to a Windows-container daemon. The library targets Linux containers;
    EnsureLinuxDaemonAsync exists to tell you when the daemon is in the wrong
    mode. Windows and macOS are fine as CLIENT platforms.

  - Connect over TLS. https:// endpoints throw NotSupportedException. Reach a
    remote daemon over ssh:// instead.

  - Implement SSH. The ssh:// transport runs the operating system's SSH client;
    it does not speak the protocol, manage keys, prompt for passwords, accept
    host keys, or read ~/.ssh/config itself. All of that is OpenSSH's.

  - Orchestrate a cluster. There are no Swarm services, no stacks, no nodes, no
    secrets, no configs, no tasks. This is single-daemon container management.

  - Read or write Compose files. There is no compose model, no YAML parser and
    no `docker compose` integration.

  - Manage a registry. There is no login, no push, no registry search, no
    catalogue browsing and no credential storage. PullAsync falls back to the
    `docker` CLI precisely so that the machine's existing credential helpers do
    that job.

  - Copy files into or out of containers as a public API. There is no
    CopyToContainerAsync or archive export. (The analysis tier uses `docker cp`
    internally, but that is not exposed.)

  - Attach to a container's main process. There is no attach API; exec is the
    way in, and it starts a NEW process in the container.

  - Pause, unpause, rename, commit or export containers, or import and save
    images as tarballs.

  - Emulate a terminal. It hands you the daemon's raw byte stream, ANSI escape
    sequences and all. Rendering that is a VT emulator's job -- for example
    CodeBrix.Terminal, which is what this stream was shaped to feed.

  - Provide any UI. There are no controls, no rendering, no progress widgets;
    IProgress<string> callbacks are as far as it goes.

  - Run its own tools. Trivy, Dive, Hadolint and Slim/mint are third-party
    projects run as containers; the library parses their output but does not
    vendor, bundle or reimplement them, and their findings are theirs.

  - Register custom advisor rules. The fourteen shipped rules are internal and
    the set is fixed.

  - Work offline. Every operation goes to a daemon; there is no mock, no replay
    and no in-memory mode.

  - Offer synchronous APIs. Everything is Task-based. There are no .Result or
    .Wait() wrappers, and adding your own is a good way to deadlock.

  - Run on .NET versions below 10.0.

This package IS for: managing the full lifecycle of Linux containers, images,
networks and volumes on one Docker daemon (local, TCP, or remote over SSH);
setting and retuning typed resource limits; reading logs, live statistics and
daemon events; running commands inside containers one-shot or as a live
interactive terminal; diagnosing throttling, OOM kills, memory composition and
health; and analysing containers and images for configuration and security
problems.


================================================================================

WORKING EXAMPLES ON GITHUB
==========================

The integration test suite is the largest body of compiling, working usage of
this package. It runs against a REAL daemon -- there are no mocks -- so every
test is a worked example of an operation actually succeeding:

    https://github.com/ellisnet/CodeBrix.Docker/tree/main/tests/CodeBrix.Docker.Tests

Feature-to-test-file map:

  Daemon information: ping, version, info, disk usage, event streaming
    https://github.com/ellisnet/CodeBrix.Docker/blob/main/tests/CodeBrix.Docker.Tests/SystemTests.cs

  Container lifecycle: run, create/start, stop, restart, kill, remove, wait,
  logs, exec, list and inspect, prune
    https://github.com/ellisnet/CodeBrix.Docker/blob/main/tests/CodeBrix.Docker.Tests/ContainerLifecycleTests.cs

  Typed resource limits, live retuning with UpdateResourcesAsync, and the
  OOM-kill path
    https://github.com/ellisnet/CodeBrix.Docker/blob/main/tests/CodeBrix.Docker.Tests/ResourceLimitTests.cs

  Live and streamed statistics, CPU/memory percentages, throttling counters and
  PID counts
    https://github.com/ellisnet/CodeBrix.Docker/blob/main/tests/CodeBrix.Docker.Tests/StatsTests.cs

  Diagnostics: throttling, memory breakdown, OOM reports, health and
  WaitForHealthyAsync
    https://github.com/ellisnet/CodeBrix.Docker/blob/main/tests/CodeBrix.Docker.Tests/DiagnosticsTests.cs

  The advisor rules, on both well- and badly-configured containers
    https://github.com/ellisnet/CodeBrix.Docker/blob/main/tests/CodeBrix.Docker.Tests/AdvisorTests.cs

  Images: pull, build (including --target and --build-arg), tag, inspect,
  history, remove and prune
    https://github.com/ellisnet/CodeBrix.Docker/blob/main/tests/CodeBrix.Docker.Tests/ImageTests.cs

  Networks: create, connect with aliases, disconnect, inspect, name resolution
    https://github.com/ellisnet/CodeBrix.Docker/blob/main/tests/CodeBrix.Docker.Tests/NetworkTests.cs

  Volumes: create, mount, share data between containers, inspect, prune
    https://github.com/ellisnet/CodeBrix.Docker/blob/main/tests/CodeBrix.Docker.Tests/VolumeTests.cs

  The analysis tier: Trivy, Dive and Hadolint against real images
    https://github.com/ellisnet/CodeBrix.Docker/blob/main/tests/CodeBrix.Docker.Tests/AnalysisTests.cs

  The Slim/mint image optimizer (opt-in)
    https://github.com/ellisnet/CodeBrix.Docker/blob/main/tests/CodeBrix.Docker.Tests/SlimTests.cs

  Proof that operations do not leak containers, networks or volumes
    https://github.com/ellisnet/CodeBrix.Docker/blob/main/tests/CodeBrix.Docker.Tests/ResourceLeakGuardTests.cs

  The shared client fixture and label-scoped cleanup, which is a good model for
  your own teardown
    https://github.com/ellisnet/CodeBrix.Docker/blob/main/tests/CodeBrix.Docker.Tests/Infrastructure/DockerTestFixture.cs

To read one as plain text, swap the host for raw.githubusercontent.com:

    https://raw.githubusercontent.com/ellisnet/CodeBrix.Docker/main/tests/CodeBrix.Docker.Tests/ContainerLifecycleTests.cs

The repository also carries tests for the streaming exec API
(ExecStreamTests.cs), the ssh:// transport and its containerised sshd harness
(SshTransportTests.cs, Infrastructure/SshdTestHarness.cs), the PID-limit wire
converter (PidsStatsTests.cs) and the analysis tool-image defaults
(AnalysisOperationsTests.cs), all in the same tests/CodeBrix.Docker.Tests
folder.

A full sample application built on this package -- a Redis topology manager with
container management, live diagnostics and an interactive console into any
container -- lives in the repository at samples/RedisSetupTool. See
EXTRAS-README.txt for what it is and how to run it.


================================================================================

QUICK REFERENCE CARD
====================

PACKAGE     CodeBrix.Docker.MitLicenseForever   (MIT; .NET 10+; zero dependencies)
NAMESPACE   using CodeBrix.Docker;              (the only one; 90 public types)

CLIENT      using var client = DockerClient.Create();
            DockerClient.Create(new DockerClientOptions { Endpoint = "...",
                DockerCliPath = "docker", SshExecutablePath = "ssh",
                SshArguments = { ... }, DefaultTimeout = TimeSpan.FromSeconds(100) })
            client.Endpoint                       // the endpoint actually resolved
ENDPOINTS   unix:///var/run/docker.sock | npipe://./pipe/docker_engine |
            tcp://host:port | ssh://[user@]host[:port]      https:// -> throws
            order: options.Endpoint -> DOCKER_HOST -> platform default

SYSTEM      client.System.PingAsync()                     // bool, never throws
            .GetVersionAsync() -> DockerVersionInfo
            .GetInfoAsync() -> DockerSystemInfo           // CgroupDriver, SwapLimit
            .GetDiskUsageAsync() -> DiskUsageInfo
            .StreamEventsAsync([type, idOrName,] ct) -> IAsyncEnumerable<DockerEvent>
            .EnsureLinuxDaemonAsync()

CONTAINERS  client.Containers
            .RunAsync(spec) / .CreateAsync(spec) / .StartAsync(id)  -> string id
            .StopAsync(id, timeoutSeconds: 10) / .RestartAsync / .KillAsync(id, "SIGKILL")
            .RemoveAsync(id, force: false, removeVolumes: false)
            .WaitForExitAsync(id) -> long
            .ListAsync(all: false, labelFilters: null) -> IReadOnlyList<ContainerSummary>
            .InspectAsync(id) -> ContainerInspectResult
            .PruneAsync(labelFilters: null)
            .UpdateResourcesAsync(id, new ResourceLimits { Cpus = 1.0 })

SPEC        new ContainerSpec { Image = "...",            // the only required member
              Name, Command, Entrypoint, Env, Labels, User, WorkingDir, HostName,
              PortBindings, ExposedPorts, Mounts, NetworkName, NetworkAliases,
              RestartPolicy, AutoRemove, Privileged, Healthcheck,
              LogDriver, LogOptions, Limits }
MOUNTS      MountSpec.Volume(name, path, readOnly: false)
            MountSpec.Bind(hostPath, path, readOnly: false)
            MountSpec.Tmpfs(path, sizeBytes: null)
PORTS       new PortBinding(containerPort, hostPort, "tcp")   // hostPort null = expose only
RESTART     RestartPolicy.No / .Always / .UnlessStopped / .OnFailure(maxRetries)
HEALTH      new HealthcheckSpec { Test = ["CMD-SHELL", "..."], Interval, Timeout,
                                  StartPeriod, Retries }
LIMITS      new ResourceLimits { Cpus, CpusetCpus, CpuShares, MemoryBytes,
                MemoryReservationBytes, MemorySwapBytes, PidsLimit }
            ResourceLimits.Megabytes(n) / .Gigabytes(n)
            swap off  ->  MemorySwapBytes == MemoryBytes

STATS       .GetStatsAsync(id) -> ContainerStats           // one sample
            .StreamStatsAsync(id, ct) -> IAsyncEnumerable<ContainerStats>
            stats.HasLiveData / .CpuPercent() / .MemoryPercent()
                 / .EffectiveMemoryPercent() / .ThrottleRatio()
            first streamed sample: CpuPercent() is null (no delta yet)

LOGS        .GetLogsAsync(id, tail: null, timestamps: false) -> ContainerLogs
            logs.Stdout / .Stderr / .Combined / .IsEmpty

EXEC        .ExecAsync(id, ["sh", "-c", "..."], user, workingDir, env) -> ExecResult
            result.Stdout / .Stderr / .ExitCode / .Succeeded
            .ExecStreamAsync(id, new ExecSpec { Command = ["/bin/sh"],
                AttachStdin = true, Tty = true, ConsoleHeight = 24, ConsoleWidth = 80,
                Env = { "PS1=$ " }, User, WorkingDir, Privileged })
                -> ContainerExecStream (IAsyncDisposable)
            stream.ReadAsync(buffer) -> ExecStreamReadResult { Target, Count, EndOfStream }
            stream.WriteAsync(text) / .WriteLineAsync(text) / .CloseStandardInputAsync()
            stream.ResizeAsync(rows, cols) / .InspectAsync() / .WaitForExitAsync()
            stream.ExecId / .IsTty / .UsesRawFraming / .CanCloseStandardInput
            .ResizeExecAsync(execId, rows, cols) / .InspectExecAsync(execId)
            missing shell -> no exception, exit code 127 on standard OUTPUT

IMAGES      client.Images
            .PullAsync(reference, progress) / .RemoveAsync(ref, force)
            .TagAsync(source, target) / .InspectAsync(ref) -> ImageInspectResult
            .ListAsync(all, labelFilters) -> IReadOnlyList<ImageSummary>
            .GetHistoryAsync(ref) -> IReadOnlyList<ImageHistoryEntry>
            .PruneAsync(dangling: true, labelFilters)
            .BuildAsync(new ImageBuildSpec { ContextDirectory, DockerfilePath, Tags,
                BuildArgs, Target, Pull, NoCache, Labels, Output }) -> ImageBuildResult

NETWORKS    client.Networks
            .CreateAsync(name, "bridge", labels) -> string id
            .ConnectAsync(net, container, aliases) / .DisconnectAsync(net, container, force)
            .ListAsync([labelFilters]) / .InspectAsync(id) / .RemoveAsync(id)
            .PruneAsync([labelFilters])

VOLUMES     client.Volumes
            .CreateAsync(name, labels) -> string name    // null name = anonymous
            .ListAsync([labelFilters]) / .InspectAsync(name) / .RemoveAsync(name, force)
            .PruneAsync()             // ANONYMOUS volumes only
            .PruneAsync(labelFilters) // named volumes too, but only yours

DIAGNOSTICS client.Diagnostics
            .DiagnoseAsync(id) -> ContainerDiagnosticsReport   // all four at once
            .GetCpuThrottlingAsync(id) -> CpuThrottlingReport  // ThrottleRatio, Severity
            .GetMemoryBreakdownAsync(id) -> MemoryBreakdownReport  // Anon vs File bytes
            .CheckOomAsync(id) -> OomReport                    // WasOomKilled, ExitCode
            .GetHealthAsync(id) -> HealthReport
            .WaitForHealthyAsync(id, TimeSpan.FromSeconds(30))
            every report carries .Interpretation (display) and raw counters (logic)

ADVISOR     client.Advisor.AnalyzeContainerAsync(id) -> IReadOnlyList<AdvisorFinding>
            client.Advisor.AnalyzeAllContainersAsync()
            AdvisorEngine.RuleIds                  // CB001..CB014
            finding.RuleId / .Severity / .ContainerName / .Title / .Detail / .Recommendation

ANALYSIS    client.Analysis
            .ScanImageAsync(ref, new TrivyScanOptions { Severities = { "HIGH" },
                IgnoreUnfixed, CacheVolumeName, Timeout, ToolImage }) -> TrivyScanResult
            .AnalyzeImageEfficiencyAsync(ref) -> DiveAnalysisResult
            .LintDockerfileAsync(path) -> HadolintResult
            .OptimizeImageAsync(ref, new SlimOptions { OutputTag, HttpProbePaths,
                ContinueAfterSeconds, Timeout, ToolImage }) -> SlimResult   // EXPERIMENTAL
            tool images: TrivyImage, DiveImage, HadolintImage, SlimImage
            tool label : AnalysisOperations.ToolLabelName = "codebrix.docker.tool"

ERRORS      DockerException
              DockerApiException (StatusCode, ResponseBody)
                DockerContainerNotFoundException / DockerImageNotFoundException
              DockerCliException (ExitCode, StdErr, Command)
            NotSupportedException  -> unknown/https endpoint, half-close on a
                                      byte-mode pipe
            ArgumentException      -> incomplete spec, thrown before any request

CLI NEEDED  Images.BuildAsync, an authenticated Images.PullAsync,
            Analysis.AnalyzeImageEfficiencyAsync, Analysis.LintDockerfileAsync

TARGET      .NET 10 or later
LICENSE     MIT


================================================================================
