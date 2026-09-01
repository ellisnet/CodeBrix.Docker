================================================================================
EXTRAS-README: CodeBrix.Docker
Samples, tools and other content in this repository that is not part of a
NuGet package
================================================================================

Nothing described here ships in the CodeBrix.Docker.MitLicenseForever package.
It is all here to demonstrate and verify the library.


THE REDIS SETUP TOOL SAMPLE (samples/RedisSetupTool/)
=====================================================
A CodeBrix.Platform desktop application that stands up, manages and tears down
Redis databases in thirteen topologies, manages any container on the daemon, and
opens a real shell inside one -- exercising very nearly the whole
CodeBrix.Docker surface on the way.

It builds and runs on all six platform heads. Everything it creates carries its
own labels, so it can always be swept without touching anything else on the
daemon.


THE EIGHT SECTIONS
------------------
A navigation rail on the left with per-section count badges, a header carrying a
daemon status pill and a global refresh, and one section at a time on the right.

  DASHBOARD
      Four counter cards that double as navigation (Redis instances, containers,
      images, volumes); the daemon's own description; disk usage as five bars
      with reclaimable figures; advisor counts by severity with the three
      highest findings; and the last twenty daemon events.

  REDIS INSTANCES
      The centrepiece. One card per instance: topology chip, name, a state pill
      counting running nodes, a dot per node with its host port, and a CONNECT
      block of copyable rows -- endpoints, service name, username, connection
      string and a ready-to-paste redis-cli command, with the password masked
      behind a show/hide toggle. Verify connects a real Redis client and lists
      every check it ran. Console, Logs, Start, Stop, Restart and Destroy cover
      the lifecycle; Destroy names what it will remove before it does it. A
      resource-capped instance also shows a diagnostics strip. Filter by
      topology, by state, or by name; Sweep all destroys every instance.

  NEW INSTANCE
      The topology catalog grouped by category on the left, and on the right the
      chosen topology's explanation, a form generated from its parameters (text,
      password with a generate button, number, choice, switch and multi-line
      editors), a preview of the host ports it would take, what is still wrong
      with the request, and a Create button that stays disabled until nothing
      is. Creating streams progress lines; a failure reports the rollback.

  CONTAINERS
      Every container on the daemon, not only this tool's. Filter by all /
      running / managed-by-this-tool, or search. The detail pane has five tabs:
      Overview (everything inspect knows, plus networks, mounts, limits and a
      collapsible environment block), Logs (with a tail size, timestamps, and an
      auto-refresh that says it polls, because the daemon offers no follow API
      here), Stats (live CPU and memory bars, a sixty-sample sparkline, and the
      process, network and block figures), Diagnostics (throttling, memory
      breakdown, OOM and health, each with the library's own interpretation
      sentence) and Advisor. A toolbar carries start, stop, restart, kill with a
      signal picker, remove, console and copy-id; the section can prune stopped
      containers.

  CONSOLES
      A tab strip of live terminal sessions. The shell is found by probing
      candidates and reading the exit code -- 127 means the image does not ship
      it -- and the status strip shows the resolved shell, the grid size and the
      session state, offering Reopen once it ends. Resizing the window resizes
      the remote pty.

  IMAGES
      Every image, with pull, tag, remove and prune-dangling, plus the four
      containerised analysis tools CodeBrix.Docker wraps: scan (Trivy),
      efficiency (Dive) and Dockerfile lint (Hadolint), each writing into a
      shared output block; the detail pane lists the image's facts and layers.

  NETWORKS AND VOLUMES
      Two stacked halves, each a list with a detail block and create, remove and
      prune actions. Removing a network or volume that belongs to a live
      instance warns first and suggests destroying the instance instead.

  SYSTEM
      The daemon in full, disk usage with the four prune buttons, a live event
      stream with a type filter and a pause toggle, the whole advisor sweep with
      a severity filter, and one destructive action: Sweep RedisSetupTool
      resources, which lists every instance and its container and volume counts
      before it asks.


THE THIRTEEN TOPOLOGIES
-----------------------
    A1  plain standalone
    A2  requirepass
    A3  ACL users
    A5  RDB + AOF persistence
    A6  maxmemory plus an eviction policy
    B1  primary plus one replica
    C1  Sentinel: 1 primary + 2 replicas + 3 sentinels
    D2  cluster: 3 primaries + 3 replicas, slots split three ways
    E3  Redis 6.2, the compatibility floor
    E4  Valkey 8.1
    F3  modules on Redis 8 (search, bloom, timeseries, JSON, vector set)
    G1  memory-capped container plus maxmemory -- the diagnostics showcase
    H1  Redlock quorum: five independent masters with AOF

Many instances of many topologies run at once. The unit the user manages is an
INSTANCE, not the topology, so two instances of A1 coexist. A host port
allocator hands out free ports from 6400-6999 (sentinels 26400-26999, cluster
data 7400-7999 with bus ports at +10000). Instance identity lives in Docker
labels -- codebrix.redissetup.instance, .topology, .role, .node and friends --
so the tool can be closed and reopened and still find, manage and completely
tear down what it created, with no side-car state file to drift. Each instance
gets its own network and volumes named from its instance id.


PROJECT LAYOUT
--------------
    samples/RedisSetupTool/
      RedisSetupTool.slnx

      src/RedisSetupTool.UI/               shared XAML (.shproj + .projitems)
      src/RedisSetupTool.Core/             view models and application logic

      src/libs/RedisSetupTool.DockerManagement/
          THE ONLY PROJECT THAT REFERENCES CodeBrix.Docker. Every Docker
          operation lives in it: the generic container/image/network/volume
          surface, the topology definitions and their orchestration, the port
          allocator, the label schema, instance discovery and teardown, and the
          exec-stream plumbing. No view models, no XAML, no CodeBrix.Platform
          reference -- the library is meant to be peelable and reusable for a
          different container project. It exposes its own DTOs at the seam and
          maps from CodeBrix.Docker types internally.

      src/libs/RedisSetupTool.RedisManagement/
          The Redis client concerns -- connect, ping, exercise, health probes,
          Redlock acquisition, per-topology verification. It is the only project
          that references StackExchange.Redis, which is what will make a later
          swap to a CodeBrix Redis client a one-project change.

      src/libs/RedisSetupTool.TerminalView/
          Bridges a CodeBrix.Docker exec stream into the TerminalView control.

      src/RedisSetupTool.LinuxX11/         the six platform heads
      src/RedisSetupTool.LinuxWayland/
      src/RedisSetupTool.LinuxFrameBuffer/
      src/RedisSetupTool.MacOS/
      src/RedisSetupTool.Win32Skia/
      src/RedisSetupTool.WinWpfSkia/

      tests/libs/*.Tests/                  one test project per library


RUNNING IT
----------
A running Docker daemon is required; the tool pulls the Redis and Valkey images
it needs on demand. Pick the head that matches the machine:

    dotnet run --project samples/RedisSetupTool/src/RedisSetupTool.LinuxX11
    dotnet run --project samples/RedisSetupTool/src/RedisSetupTool.LinuxWayland
    dotnet run --project samples/RedisSetupTool/src/RedisSetupTool.LinuxFrameBuffer
    dotnet run --project samples/RedisSetupTool/src/RedisSetupTool.MacOS
    dotnet run --project samples/RedisSetupTool/src/RedisSetupTool.Win32Skia
    dotnet run --project samples/RedisSetupTool/src/RedisSetupTool.WinWpfSkia

The frame-buffer head runs without a window manager, so it enables the platform
software keyboard and file-open picker; the others need neither. The WinWpfSkia
head is net10.0-windows and renders in software.

The sample projects are listed in the repository's CodeBrix.Docker.slnx under
/Samples/, /Samples/Libraries/ and /Samples/Tests/, so building the solution
builds them too. To build only the library while working on it, build
src/CodeBrix.Docker/CodeBrix.Docker.csproj directly.


DRIVING IT UNATTENDED
---------------------
Set REDISSETUP_AUTOMATION to run one scripted pass through the application's own
commands after the first refresh. It exists so the UI can be verified on a head
where synthetic clicks are unreliable; it is off unless the variable is set.

    REDISSETUP_AUTOMATION=tour           visit every section in turn
    REDISSETUP_AUTOMATION=a1-roundtrip   create an A1 instance, read its
                                         endpoints, verify it with a real Redis
                                         client, walk the container tabs, open
                                         and use a console, then destroy it
    REDISSETUP_AUTOMATION=demo           create one A1, one A2 and one B1 and
                                         leave them running, to have something
                                         to look at. Clean them up with the
                                         System section's sweep.

Scripts can be combined: a1-roundtrip+tour runs both. REDISSETUP_AUTOMATION_LOG
names a file for the step-by-step log, which otherwise goes to standard output.

    REDISSETUP_AUTOMATION=a1-roundtrip \
    REDISSETUP_AUTOMATION_LOG=/tmp/run.log \
        dotnet run --project samples/RedisSetupTool/src/RedisSetupTool.LinuxX11


THE SAMPLE'S TEST SUITES
------------------------
Three projects, one per library. Run them as built executables -- on SDK
10.0.400 `dotnet test` reports zero tests for this runner:

    tests/libs/<project>/bin/Release/net10.0/<project>

    RedisSetupTool.DockerManagement.Tests    94 tests, about a minute. Needs a
                                             running daemon: it creates and
                                             destroys real containers.
    RedisSetupTool.RedisManagement.Tests     34 tests, under two seconds. No
                                             daemon needed.
    RedisSetupTool.TerminalView.Tests        12 tests, under a second. No daemon
                                             needed.

Two environment gates control what runs:

    REDISSETUP_TEST_HEAVY=1              also run the C1, D2 and H1 topologies,
                                         which are six, six and five containers
                                         and would otherwise triple the pass.
    REDISSETUP_TEST_REDIS=host:port      also run the live Redis probe tests
                                         against an endpoint you supply.

Without them those tests are skipped, which is the three skips each suite
reports. Every resource the suites create carries codebrix.redissetup.tests=true
and is swept by that label alone, so a suite run never touches an instance made
by the application -- or anything else on the daemon.


A NOTE ON THE DOCKER REFERENCE
------------------------------
RedisSetupTool.DockerManagement references CodeBrix.Docker with
<PrivateAssets>all</PrivateAssets>, so the boundary is structural rather than a
convention: a downstream project that names a CodeBrix.Docker type fails to
compile. PrivateAssets also stops the runtime asset flowing, so the same project
carries an MSBuild target that republishes the assembly as a copy-to-output item
and an AssemblyLoadContext resolver that finds it at load time. Both live in the
one project that owns the reference; no head, view model or test project needs
anything of its own.


THE LIBRARY'S TEST SUITE (tests/CodeBrix.Docker.Tests/)
=======================================================
Not packed, not referenced by consumers -- but it is the largest body of
compiling, working usage of the package, which is why AGENT-README.txt points
consumers at it from its WORKING EXAMPLES ON GITHUB section.

It is an INTEGRATION suite against a real daemon: it pulls busybox, alpine,
alpine:3.19 and nginx:alpine, builds images, starts containers, provokes genuine
OOM kills and CPU throttling, runs the containerized analysis tools, and stands
up its own sshd container to exercise the ssh:// transport. Sixteen classes, 102
test cases, about two minutes on a warm machine.

    dotnet test CodeBrix.Docker.slnx

One test is opt-in behind CODEBRIX_DOCKER_TEST_SLIM=1 (the experimental image
optimizer, which is slow). Everything else runs by default. See
MAINTAINER-README.txt for the runner gotcha on SDK 10.0.400, the label-sweep
rules, and why two instances of the suite must never run at once.


================================================================================
