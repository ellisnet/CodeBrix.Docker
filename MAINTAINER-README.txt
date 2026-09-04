================================================================================
MAINTAINER-README: CodeBrix.Docker
Notes for people and agents MAINTAINING this repository - not for package
consumers
================================================================================

If you are CONSUMING the NuGet package, stop reading and open AGENT-README.txt
instead. Everything below is about the repository itself: how it is laid out,
how it builds, how it is tested, how it is packaged, where its vendored code
came from, and the conventions the source follows.


PURPOSE AND SCOPE
=================
This repository produces exactly one NuGet package:

    PackageId:  CodeBrix.Docker.MitLicenseForever
    Assembly:   CodeBrix.Docker
    Namespace:  CodeBrix.Docker  (one namespace; folders are not namespaces)
    Project:    src/CodeBrix.Docker/CodeBrix.Docker.csproj
    License:    MIT
    Consumer documentation: AGENT-README.txt (repo root)

The library is a cross-platform, zero-dependency .NET client for the Docker
Engine API, with three tiers layered on top of it: container/image/network/
volume lifecycle with typed resource limits; a diagnostics tier (CPU throttling,
OOM detection, memory composition, health); and an optimization tier (a
fourteen-rule advisor plus containerized Trivy / Dive / Hadolint / Slim
analysis).

It also carries one sample application, samples/RedisSetupTool, which is not
part of the package. See EXTRAS-README.txt.


REPOSITORY LAYOUT
=================
    src/CodeBrix.Docker/             the library project (the only packable
                                     project in the repository)
      Client/                        DockerClient, DockerClientOptions, and the
                                     internal DockerApiClient that carries all
                                     the HTTP plumbing
      Transport/                     endpoint parsing and the connect
                                     callbacks: DockerEndpoint,
                                     DockerEndpointKind, DockerConnectionFactory,
                                     DockerHijackConnection, HijackedStream,
                                     HttpResponseStream, IWriteClosableStream,
                                     SshDialStdioConnection, SshProcessStream
      Common/                        DockerJson (the shared JsonSerializerOptions
                                     and its converters), the exception types,
                                     QueryStringBuilder, JsonEmptyObject
      Containers/                    ContainerOperations plus every container
                                     DTO: specs, inspect, summaries, stats,
                                     exec types, MultiplexedStreamReader
      Images/                        ImageOperations and image DTOs, including
                                     the CLI-backed BuildKit build path
      Networks/                      NetworkOperations and DTOs
      Volumes/                       VolumeOperations and DTOs
      System/                        SystemOperations and DTOs (version, info,
                                     disk usage, events)
      Diagnostics/                   DiagnosticsOperations, the four report
                                     types, ThrottleSeverity,
                                     DiagnosticsFormatting
      Advisor/                       AdvisorEngine, IAdvisorRule, AdvisorContext,
                                     AdvisorFinding, AdvisorSeverity, and the
                                     fourteen rules under Advisor/Rules/
      Analysis/                      AnalysisOperations plus the four tools'
                                     option/result types and output parsers
      Cli/                           DockerCliRunner and CliResult, the
                                     Process-based shell-out to `docker`
      InternalsVisibleTo.cs          grants CodeBrix.Docker.Tests

    tests/CodeBrix.Docker.Tests/     the xunit.v3 integration suite
      Infrastructure/                DockerTestFixture (the collection fixture),
                                     DockerTestCollection, EnvGatedFactAttribute,
                                     SshdTestHarness, TestSupport

    samples/RedisSetupTool/          the sample application (see EXTRAS-README)

    CodeBrix.Docker.slnx             the solution. Its Solution Items folder
                                     carries .gitignore, AGENT-README.txt,
                                     EXTRAS-README.txt, global.json,
                                     icon-codebrix-128.png, LICENSE,
                                     MAINTAINER-README.txt, README-INDEX.txt,
                                     README.md and THIRD-PARTY-NOTICES.txt

THE FLAT-NAMESPACE RULE - DO NOT "FIX" IT
------------------------------------------
Every public and internal type in the library declares `namespace
CodeBrix.Docker;`. The folders above are FILE ORGANIZATION ONLY. This is
deliberate and load-bearing in two ways:

  - the public API is a single using directive for a consumer, which is what
    AGENT-README promises; and
  - a folder-scoped `CodeBrix.Docker.System` namespace would shadow the global
    System namespace inside the assembly, and every file that needed
    System.Threading.Tasks would have to fight it.

Do not add folder-scoped namespaces to this repository.


BUILDING
========
Standard SDK build from the repository root:

    dotnet restore CodeBrix.Docker.slnx
    dotnet build   CodeBrix.Docker.slnx -c Release

Target framework: net10.0 only, LangVersion latest. The library has ZERO
PackageReference entries -- verify that after any change, because it is a
contract with consumers, not a preference (see CODING CONVENTIONS).

GenerateDocumentationFile is ON, so every public member must carry an XML doc
comment. Fix CS1591 at the source; never add a project-wide <NoWarn> and never
suppress it inline.

The Release build is 0 warnings / 0 errors and must stay that way. If you need
to build only the library while someone else is working in samples/:

    dotnet build src/CodeBrix.Docker/CodeBrix.Docker.csproj -c Release


TESTING
=======
    tests/CodeBrix.Docker.Tests -- xunit.v3 4.0.0, xunit.runner.visualstudio
    4.0.0, Microsoft.NET.Test.Sdk 18.9.0 and SilverAssertions. Sixteen test
    classes, 98 test members, 102 test cases (one [Theory] contributes five).

THIS IS AN INTEGRATION SUITE, NOT A UNIT SUITE. It requires a running Docker
daemon and does real work against it: it pulls busybox:latest, alpine:latest,
alpine:3.19 and nginx:alpine at startup, builds images, starts containers,
creates networks and volumes, and provokes real OOM kills and real CPU
throttling. Two small classes are the exception and need no daemon at all --
PidsStatsTests (wire-level converter behaviour) and AnalysisOperationsTests
(argument-shape and reference-splitting behaviour).

HOW TO RUN IT
-------------
    dotnet test CodeBrix.Docker.slnx

RUNNER GOTCHA: on SDK 10.0.400, `dotnet test` can report ZERO TESTS for this
xunit.v3 project. Run the built entry point directly instead -- it is a normal
executable, it gives clean per-class counts, and it takes filters:

    dotnet build tests/CodeBrix.Docker.Tests -c Release
    tests/CodeBrix.Docker.Tests/bin/Release/net10.0/CodeBrix.Docker.Tests
    tests/CodeBrix.Docker.Tests/bin/Release/net10.0/CodeBrix.Docker.Tests \
        -class CodeBrix.Docker.Tests.DiagnosticsTests

global.json at the repository root sets "test": { "runner":
"Microsoft.Testing.Platform" } and MSBuild finds it by walking up, so the sample
projects under samples/ inherit it too.

LABELS, NAMES AND THE SWEEP
---------------------------
Every resource a test creates is labelled, and DockerTestFixture.DisposeAsync
force-removes everything carrying those labels:

    DockerTestFixture.NamePrefix            "codebrix-test-"
    DockerTestFixture.LabelName             "codebrix.docker.tests"
    DockerTestFixture.LabelValue            "true"
    DockerTestFixture.ImageRepositoryPrefix "codebrix-test/"
    AnalysisOperations.ToolLabelName        "codebrix.docker.tool"

Individual tests still clean up eagerly; the sweep is the backstop that keeps a
failed run from leaving residue. Two consequences:

  - NEVER RUN TWO INSTANCES OF THE SUITE CONCURRENTLY on one daemon. The sweep
    is machine-wide over those labels, so the first one to finish tears down the
    other's containers mid-test.
  - The whole suite is one non-parallel collection (DockerTestCollection,
    DisableParallelization = true) for the same reason.

THE ENVIRONMENT GATE
--------------------
    CODEBRIX_DOCKER_TEST_SLIM=1

gates EXACTLY ONE test -- SlimTests.OptimizeImageAsync_ProducesASmallerImage --
through Infrastructure/EnvGatedFactAttribute.cs, a FactAttribute subclass that
sets Skip unless the named variable equals the expected value. Nothing else in
the suite is gated. The default run is therefore 102 total / 101 passed / 1
skipped; with the gate open it is 102 / 102 / 0.

TREAT THE GATED RUN AS PART OF A RELEASE CHECK, NOT AN OPTIONAL EXTRA. That
single test is what caught a shipped library defect that had never been
exercised (see NOTES, "The Slim to mint history"). It costs about 19 seconds
with the current tool image.

THE sshd HARNESS
----------------
There is no sshd on a typical development workstation and starting one would
need root, so the suite RUNS ITS OWN. Infrastructure/SshdTestHarness.cs builds
two images from one two-stage Dockerfile (alpine:3.19 + openssh-server, then
+ docker-cli), starts a container from each, and publishes them on the first
free ports from 2222 upward. The one WITH the Docker CLI gets
/var/run/docker.sock bind-mounted, so the full path under test is

    ssh child process -> docker system dial-stdio -> mounted socket -> the real
    daemon

and because that is the same daemon the suite is using, a test can assert that
both clients report the same version and see the same containers. The one
WITHOUT the Docker CLI is the negative case for "the remote has no docker".

NOTHING TOUCHES ~/.ssh. A throwaway ed25519 key pair is generated per run into a
temporary directory; the containers' own host keys are read back out with
Containers.ExecAsync and written into a scratch known_hosts naming each
published port; an empty known_hosts file provides the untrusted-host case. The
client is pointed at all of that through the PUBLIC DockerClientOptions.
SshArguments surface (-F /dev/null, UserKnownHostsFile, GlobalKnownHostsFile=
/dev/null, IdentitiesOnly=yes, -i <scratch key>), so the tests exercise the same
seam a consumer uses rather than a test-only hook.

The harness is ALWAYS ON, not env-gated: it costs about 20 seconds including
both image builds, needs nothing the suite did not already need, and guards a
transport that would otherwise be entirely untested. Its images and containers
carry the suite's label and the codebrix-test/ repository prefix, so the normal
sweep removes them; the fixture owns the harness, builds it lazily on first use
once per run, and disposes the temporary directory.

WHAT THE SUITE COSTS
--------------------
Roughly two minutes on a warm machine, single process, Release build: about 121
seconds with the default gate and about 130 with CODEBRIX_DOCKER_TEST_SLIM=1.
The analysis tier is the expensive part -- it pulls several gigabytes of tool
images the first time and downloads Trivy's vulnerability database -- so give
AnalysisTests its own pass while iterating rather than dragging it through every
run:

    tests/.../CodeBrix.Docker.Tests -class CodeBrix.Docker.Tests.AnalysisTests

ENVIRONMENT ASSUMPTIONS THAT ARE EASY TO BREAK
----------------------------------------------
  - DNS PROBES USE ROOTED NAMES. Docker copies the host's /etc/resolv.conf
    search list verbatim into every container. On a machine whose router hands
    out a search domain, BusyBox's nslookup expands a bare label against that
    list and never retries the bare name, so it returns NXDOMAIN for a container
    that resolves perfectly well. NetworkTests therefore appends a trailing dot
    (a private Rooted() helper) to suppress search-list expansion. Do not
    "simplify" that away; the probe is measuring Docker's embedded DNS, not the
    DNS configuration of whichever machine the suite happens to run on. The
    `ping -c 1 -W 2 alpha` probe in the same file is deliberately left
    unrooted -- musl's resolver honours ndots:0 and tries the bare name first --
    so the file checks two differently-implemented resolvers.

  - PID LIMITS DEPEND ON THE CGROUP DRIVER. Under the systemd driver an uncapped
    container inherits the systemd scope's TasksMax, a large finite number;
    under cgroupfs it reports the "unlimited" sentinel that
    PidsStats.UnlimitedAsNullInt64Converter maps to null. StatsTests asserts
    what is true on both (null, or greater than 1024) and PidsStatsTests fences
    the converter at the wire level, where the host's driver cannot reach it.

  - OOM TESTS NEED SWAP ACCOUNTING. `docker info` must report SwapLimit: true.
    Without it the daemon cannot enforce memory-swap limits and the OOM tests
    become flaky or never fire.

  - THE OOM TRIGGER IS A TMPFS FILL, NOT `tail /dev/zero`. BusyBox's tail caps
    its own read-ahead buffer, so it exits 1 with OOMKilled false. The suite
    instead mounts a tmpfs larger than the memory limit and dd's into it
    (OomSpecs.MemoryHog): tmpfs pages are charged to the container's memory
    cgroup and cannot be reclaimed with swap disabled, which reproduces
    OOMKilled true / exit 137 every time.


PACKAGING AND PUBLISHING
========================
GeneratePackageOnBuild is true, so every build of the library project emits a
fresh .nupkg. To pack deliberately:

    dotnet pack src/CodeBrix.Docker/CodeBrix.Docker.csproj -c Release -o <dir>

VERSIONING is the CodeBrix date-stamped scheme, computed in the csproj from
System.DateTime.UtcNow as

    1.<whole years since _VersionBaseYear>.<day of year>.<minute of day UTC>

with _VersionBaseYear = 2026. It is monotonically increasing but it is NOT
SemVer: major is pinned to 1 and minor encodes the year, so neither says
anything about API compatibility. Two builds inside the same UTC minute produce
the SAME version -- never publish two packages from one minute. To re-baseline
the minor number, change _VersionBaseYear. Do not replace the version block with
a literal <Version>; a hardcoded <Version>1.0.244.0</Version> is exactly what
this block replaced.

WHAT SHIPS INSIDE THE NUPKG, declared as <None ... Pack="true" PackagePath="">
in the library csproj:

    icon-codebrix-128.png    (PackageIcon)
    README.md                (PackageReadmeFile)
    AGENT-README.txt         (the consumer guide, taken from the repo root)
    THIRD-PARTY-NOTICES.txt

MAINTAINER-README.txt, EXTRAS-README.txt and README-INDEX.txt are repo-only and
are NOT packed. Neither is anything under samples/ or tests/.

PackageLicenseExpression is MIT and PackageRequireLicenseAcceptance is true.

THE EMPTY DEPENDENCY GROUP IS A RELEASE CHECK. After packing, confirm the nuspec
still carries

    <dependencies><group targetFramework="net10.0" /></dependencies>

If a dependency ever appears there, something added a PackageReference to the
library project and the zero-dependency promise in AGENT-README is broken.


PROVENANCE AND VENDORED SOURCES
===============================
CodeBrix.Docker is original code, not a fork. A limited amount of logic was
ADAPTED from Docker.DotNet (https://github.com/dotnet/Docker.DotNet), MIT
licensed, Copyright (c) .NET Foundation and Contributors, version v3.125.15 at
commit c0f44a7000221a7bc1ced1154939e19caeea067a (2024-10-30). Full attribution
is in THIRD-PARTY-NOTICES.txt.

THE REFERENCE CLONE lives at ~/ClaudeHome/Docker.DotNet, at that same commit.
(Older notes in this repository referred to a Windows path, C:\Temp\Docker.DotNet;
that path is dead and the clone above replaces it.) The clone is a reference
only -- it is not a submodule, it is not vendored into the tree, and there is
deliberately NO NuGet reference to Docker.DotNet. Prefer writing clean
net10-idiomatic code with the clone open beside you; copy literally only where
the logic is intricate, which in practice means the stdcopy demultiplexer and
the half-close contract.

EVERY ADAPTED FILE CARRIES THIS HEADER, verbatim, as its first line:

    // Adapted from Docker.DotNet (https://github.com/dotnet/Docker.DotNet), MIT License, Copyright (c) .NET Foundation and Contributors.

Exactly three files carry it today:

    src/CodeBrix.Docker/Containers/MultiplexedStreamReader.cs
    src/CodeBrix.Docker/Transport/IWriteClosableStream.cs
    src/CodeBrix.Docker/Transport/HijackedStream.cs

Transport/DockerHijackConnection.cs deliberately carries NO header: upstream
solves the same problem with its own Microsoft.Net.Http.Client stack, and none
of that was used. If you adapt anything further, add the header AND extend
THIRD-PARTY-NOTICES.txt; if you rewrite a file until nothing of the original
remains, remove the header rather than leaving a false attribution.

The upstream areas that were relevant, all small: src/Docker.DotNet/
MultiplexedStream.cs (246 lines), Endpoints/ExecOperations.cs (111),
Microsoft.Net.Http.Client/WriteClosableStream.cs (10) and IPeekableStream.cs
(18).


CODING CONVENTIONS
==================
These are the repository-specific rules. They apply to the library and the test
project alike.

  - NULLABLE REFERENCE TYPES ARE OFF, and so are implicit usings. Never use '?'
    on a reference type (string?, MyClass?) and never use the null-forgiving '!'
    operator or a #nullable directive. Value-type nullables (int?, long?,
    TimeSpan?, DateTimeOffset?, nullable enums) are Nullable<T> and are used
    freely -- much of the diagnostics contract depends on them. Nullability of
    reference types is expressed in XML doc comments and enforced by runtime
    guards, not by the compiler.

  - EVERY FILE LISTS ITS OWN USING DIRECTIVES, System.* first and then the rest
    alphabetically. There are no global usings.

  - FILE-SCOPED NAMESPACES ONLY (namespace CodeBrix.Docker;), never
    block-scoped, and always the flat CodeBrix.Docker namespace.

  - THE PUBLIC API IS ASYNC-ONLY. Every public operation returns Task, Task<T>
    or IAsyncEnumerable<T>, and takes `CancellationToken cancellationToken =
    default` as its LAST parameter. No synchronous wrappers, ever.

  - ZERO NUGET DEPENDENCIES IN THE LIBRARY PROJECT. All JSON goes through the
    in-box System.Text.Json via Common/DockerJson.cs. This is not a preference:
    it is documented in AGENT-README and verifiable in the packed nuspec. If a
    problem seems to need a package, the answer is almost always to shell out
    to something the operating system already has -- which is exactly what the
    ssh:// transport does, and why the interactive exec API uses the daemon's
    own pty rather than a native pty library.

  - NO NEW THIRD-PARTY NUGETS ANYWHERE, per the family rule: CodeBrix.* and
    Microsoft packages are fine; xUnit (and SilverAssertions) in the .Tests
    project are the standing exception.

  - XML DOC COMMENTS ON EVERY PUBLIC AND PROTECTED MEMBER.
    GenerateDocumentationFile is on; fix CS1591 at the source and never
    suppress it.

  - TESTS are named <ClassUnderTest>Tests.cs with PascalCase method names in the
    Member_Behaviour_Condition shape, //Arrange //Act //Assert comments in
    multi-statement tests, and TestContext.Current.CancellationToken passed to
    every cancellable call (xUnit1051). NEW tests are written SilverAssertions
    style (.Should().Be(), .BeNull(), .Equal()); the several hundred existing
    raw Assert.* calls stay as they are and are not to be converted.

  - PROSE RULES for anything written in this repository: no firearm metaphors
    ("pitfall", "gotcha", "sharp edge" instead), and the family's banned-name
    rule applies -- where the upstream UI framework behind some sibling
    CodeBrix repositories would be named, write "the upstream project".


NOTES
=====

NEVER `git commit` AND NEVER `git push` IN THIS REPOSITORY. Leave all changes in
the working tree; Jeremy handles every git operation. Read-only git (status,
log, diff, ls-tree) is fine.

WHAT HAS BEEN VALIDATED, AND ON WHAT
------------------------------------
The whole suite has been run against a native Linux daemon: Docker 29.7.2, API
1.55, cgroup v2 with the SYSTEMD driver, overlayfs with the containerd image
store, SwapLimit true, buildx 0.36.1 / BuildKit v0.32.2, .NET SDK 10.0.400. All
102 tests pass in both gate modes.

Validated on that host: the unix-socket transport; the ssh:// transport
end to end against the containerised sshd; container lifecycle; one-shot and
streaming exec including standard input and its half-close; logs and stream
demultiplexing; image build / pull / tag / inspect / history / prune; networks
and Docker's embedded DNS; volumes and tmpfs; live and streamed stats; typed
resource limits including update-in-place; OOM detection; cgroup v2 CPU
throttling counters; health and WaitForHealthy; all fourteen advisor rules; and
the containerized Trivy / Dive / Hadolint / Slim analysis tier.

NOT VALIDATED: THE WINDOWS NAMED-PIPE TRANSPORT. DockerConnectionFactory selects
DockerEndpointKind.NamedPipe only for an npipe:// endpoint and dials it with
System.IO.Pipes.NamedPipeClientStream. Docker on Linux never listens on a named
pipe, and a synthetic .NET pipe-server test would prove only that .NET's pipe
client works, not that Docker's pipe server is spoken to correctly. The path is
therefore recorded as UNTESTED -- neither validated nor known broken. Anyone
with a Windows daemon should exercise it and report back.

Also unchanged and still true: https:// endpoints throw NotSupportedException by
design, and tcp:// / http:// parse to DockerEndpointKind.Tcp.

THE DOCKER-DESKTOP SOCKET CLAIM, CORRECTED
------------------------------------------
An older note in this repository said that bind-mounting /var/run/docker.sock
into a tool container is rewritten by Docker Desktop to its proxied socket. That
is a DOCKER DESKTOP remark, not a general fact. On native Linux there is no
proxy and no rewrite: the daemon binds the real inode straight into the tool
container. Mechanically the library never uses the `--mount` CLI flag at all --
AnalysisOperations builds MountSpec.Bind(...) and ContainerOperations sends it
in the Engine API's HostConfig.Mounts array with Type "bind" (the API equivalent
of `--mount type=bind`, not the legacy Binds string list). That is the identical
code path on both platforms, and it is the correct one.

THE `docker cp` STEPS ARE A PORTABILITY CHOICE, NOT A REQUIREMENT
-----------------------------------------------------------------
Dive's JSON report is written to /tmp/dive.json inside its own container and
retrieved with `docker cp` afterwards; Hadolint's Dockerfile is copied INTO a
created-but-not-yet-started container the same way. Both exist because
bind-mounting a Windows host path into a Linux container depends on Docker
Desktop file sharing being configured for that drive, which is not portable. On
native Linux a bind mount would work just as well, and both `docker cp` paths
were confirmed still working here. The code path is deliberately NOT forked for
Linux: keeping `docker cp` costs one CLI invocation and buys portability across
every host filesystem-sharing arrangement. The consumer-visible consequence --
that those two operations need the docker executable on PATH -- is documented in
AGENT-README.

BUILDX 0.36 ATTESTATION MANIFESTS
---------------------------------
A plain `docker build -t <tag> .` on a modern buildx with the containerd image
store exports an image manifest, an ATTESTATION manifest and a MANIFEST LIST
(an OCI image index), and names the tag to the index. Consequently
ImageOperations.ResolveBuiltImageIdAsync, which inspects the first tag and
returns ImageInspectResult.Id, yields the INDEX digest rather than the
image-manifest digest. It is a real, resolvable local reference -- `docker image
inspect` accepts it, and tag, history, layer count, labels and size all read
back through it -- so no code change was needed. It is recorded as a documented
characteristic, and as a consumer-facing pitfall in AGENT-README, because a
caller comparing it against a registry manifest digest will see a
different-looking value for the same build.

Two harmless echoes of the same change, recorded so nobody chases them: the
ExtractImageIdFromLog fallback in BuildAsync is not exercised on the default
builder (the docker driver loads its result into the local image store, so the
inspect succeeds), and mint logs `finishCommand: output image ID mismatch` at
error level during optimization for the index-versus-manifest reason -- it exits
0 and the optimized image is correct.

THE SLIM TO MINT HISTORY, AND THE NAMING DECISION
-------------------------------------------------
AnalysisOperations.SlimImage used to default to `dslim/slim:latest`. That
repository is ABANDONED: its newest tag is 1.40.11, pushed 2024-02-02, and that
build negotiates Docker Engine API version 1.24. Docker 25 raised the minimum
supported API version to 1.40, so the retired image is refused by the daemon
before it inspects anything -- it fails in under two seconds on every current
daemon. Setting DOCKER_API_VERSION in the tool container changes nothing,
because the version is baked into that build's Docker client.

The upstream project renamed itself from "slim" to "mint" (slimtoolkit ->
mintoolkit), and the maintained continuation accepts the IDENTICAL command line
that AnalysisOperations.BuildSlimCommand already produces. The default is now
`mintoolkit/mint:latest`, with an XML <remarks> block on the property recording
the rename, the API-version incompatibility, and how to opt back to the retired
image for anyone on an older daemon.

DECISION (Jeremy, 2026-08-31): THE Slim* NAMES STAY. AnalysisOperations.SlimImage,
SlimOptions, SlimResult, SlimOptions.ToolImage, OptimizeImageAsync, the ".slim"
default output tag, SlimTests and CODEBRIX_DOCKER_TEST_SLIM all keep their
current spelling. "Slim" reads as the verb, the package has not shipped under
any other name, and the tool image is an implementation detail behind an
overridable property. Do not rename them to Mint*.

One cleanliness note: with mint, a gated Slim run leaves the daemon exactly as
it found it -- zero container and zero image residue. The retired dslim image
had been leaving a stray `docker-slim-empty-image:latest` behind, which the
suite does not sweep because it carries neither the test label nor the
codebrix-test/ repository prefix. That stray no longer occurs.

WHY THE EXEC HIJACK REQUEST IS WRITTEN BY HAND
----------------------------------------------
HttpClient cannot hand back a 101-upgraded connection in a form that allows the
WRITING HALF to be closed on its own, and that half-close is what signals end of
standard input. Transport/DockerHijackConnection.cs therefore dials the SAME
transport DockerConnectionFactory uses for ordinary calls
(DockerConnectionFactory.ConnectAsync was extracted for exactly this), writes
one HTTP/1.1 request with Connection: Upgrade / Upgrade: tcp, parses the status
line and headers itself, and keeps the socket. Bytes that arrive in the same
read as the response headers are replayed by HijackedStream before it touches
the socket again, so no output is lost. A refused hijack is read back as an
ordinary error body and translated through DockerApiClient.CreateApiException,
so callers get the usual exception hierarchy rather than an unreadable stream.
DockerClientOptions.DefaultTimeout bounds the handshake ONLY, never the hijacked
stream that follows.

MultiplexedStreamReader is an INSTANCE class now. It grew an incremental reader
(ReadAsync / WriteAsync / CloseWriteAsync / ReadRemainingAsync, IDisposable +
IAsyncDisposable) that handles both framings, while keeping its two static
members -- ReadToEndAsync and Demultiplex -- byte for byte. Container logs and
the one-shot ExecAsync still use the static path, which sniffs the buffer rather
than being told which framing to expect.

The library decides the framing from the daemon's Content-Type
(application/vnd.docker.raw-stream versus .multiplexed-stream), NOT from the Tty
flag it asked for. Keep it that way.

HOW THE ssh:// TRANSPORT IS WIRED
---------------------------------
Transport/SshProcessStream.cs is a Stream over the SSH child process's standard
input and output, and it OWNS that process: dispose kills the whole tree.
Standard error is drained on a background task from the moment the child starts
(capped at 64 KB), which is what makes the actionable error messages possible --
without it a failed handshake is an empty pipe.

Diagnosis is LAZY, not a startup probe. A healthy connect pays nothing extra;
when a read returns zero bytes or a pipe throws, the stream waits up to five
seconds for the child to exit and raises a classified DockerException only if it
exited non-zero. A clean end of stream (exit code 0) is still a clean end of
stream.

SshProcessStream implements IWriteClosableStream, so INTERACTIVE EXEC WORKS OVER
ssh://. Closing standard input on the child makes OpenSSH forward end of file to
the remote command, and dial-stdio shuts down the writing half of the remote
socket -- the same half-close a local Unix socket gets from SHUT_WR.
HijackedStream delegates its half-close to an inner IWriteClosableStream when it
is not sitting on a NetworkStream, which is the one line that made this work.

HOW THE npipe:// HALF-CLOSE IS WIRED
------------------------------------
A Windows named pipe has no shutdown(SHUT_WR), and the library used to say so:
CanCloseStandardInput was false there and interactive exec could only signal end
of input by dropping the whole connection. That was wrong. The daemon creates
its pipe in MESSAGE mode precisely so a ZERO-LENGTH MESSAGE can stand in for end
of file -- that is what go-winio, the pipe library on the daemon's side, calls
CloseWrite(), and it is how `docker exec -i` works on Windows.

Transport/NamedPipeDockerStream.cs wraps the NamedPipeClientStream and
implements IWriteClosableStream on that convention. Two things are easy to get
wrong and worth not reintroducing:

  1. THE EMPTY WRITE HAS TO BE NATIVE. PipeStream returns early for an empty
     buffer, so WriteAsync(ReadOnlyMemory<byte>.Empty) sends NOTHING -- the peer
     never sees a message and the command never sees end of file. The signal
     goes straight to WriteFile with a count of zero.

  2. THE OVERLAPPED EVENT HANDLE NEEDS ITS LOW BIT SET. The handle is opened
     PipeOptions.Asynchronous and bound to a thread pool I/O completion port;
     the low bit is what tells Windows to signal the event instead of queueing a
     completion packet nobody is waiting on.

Message mode is detected with GetNamedPipeInfo rather than
PipeStream.TransmissionMode, which reports the mode THIS PROCESS asked for, not
the one the server created the pipe with (it says Byte for docker_engine).
A pipe that really is byte-mode still reports CanCloseWrite false and still
throws, so the old contract holds where it genuinely applies.

The wrapper is opt-in: DockerConnectionFactory.ConnectAsync takes a
writeClosable flag and only DockerHijackConnection passes true, so the pooled
HttpClient connections are unchanged. Nothing in the Unix-socket or TCP path was
touched -- those still half-close through NetworkStream and Socket.Shutdown.

ContainerExecStream.CanCloseStandardInput is therefore true on every transport a
stock daemon answers on.

TWO TRANSPORT DEFECTS FIXED, WORTH NOT REINTRODUCING
----------------------------------------------------
  1. TRANSPORT ERROR MESSAGES WERE BEING SWALLOWED. Every transport raises a
     DockerException with advice of its own, but it is raised inside
     SocketsHttpHandler's connect callback, which wraps it in an
     HttpRequestException whose own message is the generic "An error occurred
     while sending the request." DockerApiClient.SendAsync now walks the inner
     chain for the transport's own DockerException and reports what it said.
     This had been hiding the unix-socket and TCP messages too, not only the SSH
     ones.

  2. CLI-BACKED OPERATIONS IGNORED THE CLIENT'S ENDPOINT. Image builds and
     credentialled pulls shell out to the docker command line, which resolves
     its own daemon from DOCKER_HOST -- so a client built on ssh://elsewhere
     would have built images on the LOCAL daemon without saying so.
     DockerCliRunner now passes DockerClientOptions.Endpoint to the child as
     DOCKER_HOST whenever one is configured; when none is, the child inherits
     the environment and resolves it exactly as DockerEndpoint.Resolve does.
     Covered by BuildAsync_WhenTheClientNamesAnEndpoint_RunsTheDockerCliAgainst
     ThatEndpoint.

     The residual caveat is documented for consumers: over ssh://, those
     CLI-backed operations use the DOCKER CLI's OWN ssh invocation. It does not
     see DockerClientOptions.SshArguments and it reads the invoking user's
     ~/.ssh/config and known_hosts (OpenSSH resolves "~" from the passwd entry,
     so HOME cannot redirect it).

WHY MemoryBreakdownReport.LimitBytes IS THE CONFIGURED LIMIT
-----------------------------------------------------------
It reports HostConfig.Memory, and null when none is set -- NOT the cgroup limit
the stats endpoint reports, which for an unlimited container is the host's total
memory. UsagePercent and EffectiveUsagePercent are therefore null for a
container with no memory limit, rather than a meaningless percentage of host
RAM. Do not "fix" this by reading MemoryStats.Limit.

Related: Advisor rule CB003 (NoPidsLimitRule) reads HostConfig.PidsLimit -- the
CONFIGURED limit -- and uses PidsStats only for informational prose. Had it read
the stats value it would have gone silently quiet on any systemd-cgroup host,
where every container carries a large inherited pids.max. Keep that distinction
in any new rule: HostConfig answers "was this configured", PidsStats answers
"what is the kernel enforcing".

OTHER DELIBERATE DESIGN CHOICES, SO THEY ARE NOT MISTAKEN FOR OVERSIGHTS
------------------------------------------------------------------------
  - Analysis pulls images itself through POST /images/create (draining the
    JSON-lines progress stream) rather than calling ImageOperations, and creates
    Trivy's cache volume directly through POST /volumes/create, so that
    AnalysisOperations depends only on DockerApiClient and ContainerOperations
    per its internal constructor. AnalyzeImageEfficiencyAsync and
    OptimizeImageAsync also pull the image BEING ANALYZED when it is not present
    locally, because Dive and Slim read it from the daemon; ScanImageAsync does
    not, because Trivy resolves references itself.

  - Tool entrypoints differ and the command shapes encode that: aquasec/trivy's
    entrypoint is `trivy` and wagoodman/dive's is /usr/local/bin/dive, so both
    take bare arguments as ContainerSpec.Command, while hadolint/hadolint has NO
    entrypoint (its Cmd is ["/bin/hadolint","-"]) and therefore needs
    /bin/hadolint as the first command element.

  - Trivy exits 0 with findings when --exit-code is not passed, and Dive in
    CI=true mode exits 0 for an image that passes its built-in rules. Non-zero
    tool exit codes that merely encode findings are surfaced in the result's
    ExitCode, not raised -- which is what DockerCliRunner.TryRunAsync exists
    for.

  - Containers.PruneAsync discards the daemon's ContainersDeleted /
    SpaceReclaimed report because its return type is Task, by design.

  - 404 mapping is NOT extended to networks and volumes:
    DockerApiClient.CreateApiException returns a plain DockerApiException for
    networks/... and volumes/... routes, matching an error model that names
    container and image not-found types only.

  - Image references are escaped leniently: Uri.EscapeDataString is applied and
    then %2F and %3A are restored, because the daemon's image routes match the
    rest of the path and a reference such as ghcr.io/owner/name:tag must keep
    its separators literal.

  - PullAsync reads the progress stream itself (PostForStreamAsync plus a line
    loop) rather than using GetJsonLinesAsync<T>, which is GET-only. An
    {"error": ...} line in an otherwise-200 response is raised as a
    DockerException, and when its text looks like an authentication refusal the
    pull is retried through `docker pull`.

  - ImageBuildResult.ImageId is resolved by inspecting the first tag. If that
    inspect fails (a builder that does not load its result into the local image
    store), the id is recovered by scanning the build log for a 64-hex-digit
    sha256: value; if that also fails the property is empty rather than
    throwing.

  - Advisor rules that need live counters are SKIPPED, not failed, for stopped
    containers (CB005, CB006, CB013). CB013 additionally requires at least 4 MB
    of page cache so it does not fire on idle containers whose few hundred
    kilobytes of cache technically dominate their usage.

THE AI-AGENT POINTER STUBS
--------------------------
AGENTS.md, CLAUDE.md, .clinerules, .cursorrules, .cursor/rules/agent-readme.mdc,
.windsurfrules, .github/copilot-instructions.md and .junie/guidelines.md all
point at README-INDEX.txt. They are byte-identical family-wide content, not
per-repo content: keep them in sync with the canonical copies and never let a
scaffolding tool rewrite them.


================================================================================
END OF MAINTAINER-README
