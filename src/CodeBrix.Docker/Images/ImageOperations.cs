using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Docker;

/// <summary>
/// Image lifecycle, inspection and build operations.
/// </summary>
public sealed class ImageOperations
{
    private static readonly string[] AuthenticationHints =
    [
        "denied",
        "unauthorized",
        "authentication required",
        "no basic auth credentials",
        "requires 'docker login'",
        "docker login",
    ];

    private readonly DockerApiClient _api;
    private readonly DockerCliRunner _cli;

    internal ImageOperations(DockerApiClient api, DockerClientOptions options)
    {
        _api = api;
        _cli = new DockerCliRunner(options);
    }

    // ---------------------------------------------------------------------------------------
    // Listing and inspection
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Lists the images stored locally.
    /// </summary>
    /// <param name="all">
    /// When <see langword="true"/>, includes intermediate layers as well as the top-level images.
    /// </param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The matching images.</returns>
    public Task<IReadOnlyList<ImageSummary>> ListAsync(bool all = false,
        CancellationToken cancellationToken = default) =>
        ListAsync(all, labelFilters: null, cancellationToken);

    /// <summary>
    /// Lists the images stored locally, restricted to those carrying the given labels.
    /// </summary>
    /// <param name="all">
    /// When <see langword="true"/>, includes intermediate layers as well as the top-level images.
    /// </param>
    /// <param name="labelFilters">
    /// Label filters. An entry with an empty value matches the label's presence.
    /// </param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The matching images.</returns>
    public async Task<IReadOnlyList<ImageSummary>> ListAsync(bool all,
        IDictionary<string, string> labelFilters, CancellationToken cancellationToken = default)
    {
        var query = new QueryStringBuilder()
            .AddIfTrue("all", all)
            .AddLabelFilters(labelFilters);

        var images = await _api
            .GetAsync<List<ImageSummary>>(query.AppendTo("images/json"), cancellationToken)
            .ConfigureAwait(false);

        return images;
    }

    /// <summary>
    /// Gets the full description of an image.
    /// </summary>
    /// <param name="reference">The image reference — a tag, a digest or an id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The inspect result.</returns>
    /// <exception cref="DockerImageNotFoundException">No such image exists locally.</exception>
    public Task<ImageInspectResult> InspectAsync(string reference, CancellationToken cancellationToken = default) =>
        _api.GetAsync<ImageInspectResult>($"images/{Reference(reference)}/json", cancellationToken);

    /// <summary>
    /// Reads an image's build history, newest layer first.
    /// </summary>
    /// <param name="reference">The image reference — a tag, a digest or an id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The layers that make up the image.</returns>
    /// <exception cref="DockerImageNotFoundException">No such image exists locally.</exception>
    public async Task<IReadOnlyList<ImageHistoryEntry>> GetHistoryAsync(string reference,
        CancellationToken cancellationToken = default)
    {
        var history = await _api
            .GetAsync<List<ImageHistoryEntry>>($"images/{Reference(reference)}/history", cancellationToken)
            .ConfigureAwait(false);

        return history;
    }

    // ---------------------------------------------------------------------------------------
    // Lifecycle
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Pulls an image from its registry.
    /// </summary>
    /// <param name="reference">
    /// The image reference, for example <c>alpine:latest</c> or
    /// <c>ghcr.io/owner/image@sha256:…</c>. A reference without a tag pulls <c>:latest</c>.
    /// </param>
    /// <param name="progress">An optional receiver for the daemon's progress lines.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the image is present locally.</returns>
    /// <remarks>
    /// The pull first runs anonymously over the Engine API, which carries no credentials. If the
    /// registry refuses that, the pull is retried through the <c>docker</c> command line so that the
    /// user's configured credential helpers apply. No timeout is applied to either attempt — cancel
    /// through <paramref name="cancellationToken"/> instead.
    /// </remarks>
    /// <exception cref="DockerException">The registry or the daemon rejected the pull.</exception>
    /// <exception cref="DockerCliException">The command-line fallback also failed.</exception>
    public async Task PullAsync(string reference, IProgress<string> progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);

        var (name, tag) = SplitReference(reference);
        var query = new QueryStringBuilder()
            .Add("fromImage", name)
            .Add("tag", tag);

        try
        {
            await PullOverApiAsync(query.AppendTo("images/create"), progress, cancellationToken).ConfigureAwait(false);
            return;
        }
        catch (DockerException ex) when (!cancellationToken.IsCancellationRequested && IsAuthenticationFailure(ex))
        {
            progress?.Report(
                $"Anonymous pull of '{reference}' was refused; retrying through the docker command line.");
        }

        await PullOverCliAsync(reference, progress, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes an image, or one of its tags when other tags remain.
    /// </summary>
    /// <param name="reference">The image reference — a tag, a digest or an id.</param>
    /// <param name="force">
    /// When <see langword="true"/>, removes the image even when it carries several tags or is
    /// referenced by a stopped container.
    /// </param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the daemon has removed the image.</returns>
    /// <exception cref="DockerImageNotFoundException">No such image exists locally.</exception>
    public Task RemoveAsync(string reference, bool force = false, CancellationToken cancellationToken = default)
    {
        var query = new QueryStringBuilder().AddIfTrue("force", force);
        return _api.DeleteAsync(query.AppendTo($"images/{Reference(reference)}"), cancellationToken);
    }

    /// <summary>
    /// Adds a tag to an existing image.
    /// </summary>
    /// <param name="sourceReference">The image to tag — a tag, a digest or an id.</param>
    /// <param name="targetReference">
    /// The new reference, for example <c>my-app:v2</c>. Without a tag, <c>:latest</c> is used.
    /// </param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the tag exists.</returns>
    /// <exception cref="DockerImageNotFoundException">The source image does not exist locally.</exception>
    public Task TagAsync(string sourceReference, string targetReference,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetReference);

        var (repository, tag) = SplitReference(targetReference);
        var query = new QueryStringBuilder()
            .Add("repo", repository)
            .Add("tag", tag ?? "latest");

        return _api.PostAsync(query.AppendTo($"images/{Reference(sourceReference)}/tag"), body: null,
            cancellationToken);
    }

    /// <summary>
    /// Prunes unused images.
    /// </summary>
    /// <param name="dangling">
    /// When <see langword="true"/> (the default), removes only untagged images. When
    /// <see langword="false"/>, removes every image no container references — which is a much
    /// broader sweep.
    /// </param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the daemon has finished pruning.</returns>
    public Task PruneAsync(bool dangling = true, CancellationToken cancellationToken = default) =>
        PruneAsync(dangling, labelFilters: null, cancellationToken);

    /// <summary>
    /// Prunes unused images carrying the given labels.
    /// </summary>
    /// <param name="dangling">
    /// When <see langword="true"/>, removes only untagged images. When <see langword="false"/>,
    /// removes every matching image no container references.
    /// </param>
    /// <param name="labelFilters">
    /// Label filters restricting what is pruned. An entry with an empty value matches the label's
    /// presence. Supplying filters is the safe way to clean up images a test or tool created.
    /// </param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the daemon has finished pruning.</returns>
    public Task PruneAsync(bool dangling, IDictionary<string, string> labelFilters,
        CancellationToken cancellationToken = default)
    {
        var query = new QueryStringBuilder()
            .AddFilter("dangling", dangling ? "true" : "false")
            .AddLabelFilters(labelFilters);

        return _api.PostAsync<ImagesPruneResponse>(query.AppendTo("images/prune"), body: null,
            cancellationToken, applyTimeout: false);
    }

    // ---------------------------------------------------------------------------------------
    // Build
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Builds an image from a context directory.
    /// </summary>
    /// <param name="spec">The build specification.</param>
    /// <param name="cancellationToken">A cancellation token that kills the build when cancelled.</param>
    /// <returns>The built image's id, its tags and the build log.</returns>
    /// <remarks>
    /// The build shells out to <c>docker build</c> so that BuildKit is used; the Engine API's build
    /// endpoint drives the legacy builder only. No timeout is applied.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="spec"/> is missing a context or a tag.</exception>
    /// <exception cref="DirectoryNotFoundException">The context directory does not exist.</exception>
    /// <exception cref="FileNotFoundException">The Dockerfile does not exist.</exception>
    /// <exception cref="DockerCliException">The build failed.</exception>
    public async Task<ImageBuildResult> BuildAsync(ImageBuildSpec spec,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);

        if (string.IsNullOrWhiteSpace(spec.ContextDirectory))
        {
            throw new ArgumentException("ImageBuildSpec.ContextDirectory is required.", nameof(spec));
        }

        if (spec.Tags.Count == 0)
        {
            throw new ArgumentException("ImageBuildSpec.Tags must contain at least one tag.", nameof(spec));
        }

        var context = Path.GetFullPath(spec.ContextDirectory);
        if (!Directory.Exists(context))
        {
            throw new DirectoryNotFoundException($"The build context directory '{context}' does not exist.");
        }

        var dockerfile = string.IsNullOrWhiteSpace(spec.DockerfilePath)
            ? Path.Combine(context, "Dockerfile")
            : Path.GetFullPath(spec.DockerfilePath);

        if (!File.Exists(dockerfile))
        {
            throw new FileNotFoundException($"The Dockerfile '{dockerfile}' does not exist.", dockerfile);
        }

        var args = new List<string> { "build", "-f", dockerfile };

        foreach (var tag in spec.Tags)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                throw new ArgumentException("ImageBuildSpec.Tags must not contain empty tags.", nameof(spec));
            }

            args.Add("-t");
            args.Add(tag);
        }

        foreach (var (key, value) in spec.BuildArgs)
        {
            args.Add("--build-arg");
            args.Add($"{key}={value}");
        }

        foreach (var (key, value) in spec.Labels)
        {
            args.Add("--label");
            args.Add($"{key}={value}");
        }

        if (!string.IsNullOrWhiteSpace(spec.Target))
        {
            args.Add("--target");
            args.Add(spec.Target);
        }

        if (spec.Pull)
        {
            args.Add("--pull");
        }

        if (spec.NoCache)
        {
            args.Add("--no-cache");
        }

        args.Add(context);

        var log = new BuildLog(spec.Output);
        await _cli.RunAsync(args, context, log, cancellationToken).ConfigureAwait(false);

        var output = log.ToString();
        var tags = spec.Tags.ToArray();

        return new ImageBuildResult
        {
            ImageId = await ResolveBuiltImageIdAsync(tags[0], output, cancellationToken).ConfigureAwait(false),
            Tags = tags,
            Output = output,
        };
    }

    // ---------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------

    private async Task PullOverApiAsync(string path, IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        await using var stream = await _api.PostForStreamAsync(path, body: null, cancellationToken)
            .ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                return;
            }

            if (line.Length == 0 || line.AsSpan().IsWhiteSpace())
            {
                continue;
            }

            ImagePullProgressMessage message;
            try
            {
                message = DockerJson.Deserialize<ImagePullProgressMessage>(line);
            }
            catch (JsonException)
            {
                continue;
            }

            if (message is null)
            {
                continue;
            }

            if (message.ErrorMessage is { Length: > 0 } error)
            {
                throw new DockerException($"The Docker daemon could not pull the image: {error}");
            }

            if (progress is not null)
            {
                var text = message.Describe();
                if (text.Length > 0)
                {
                    progress.Report(text);
                }
            }
        }
    }

    private async Task PullOverCliAsync(string reference, IProgress<string> progress,
        CancellationToken cancellationToken) =>
        await _cli.RunAsync(["pull", reference], workingDir: null, progress, cancellationToken)
            .ConfigureAwait(false);

    private async Task<string> ResolveBuiltImageIdAsync(string tag, string output,
        CancellationToken cancellationToken)
    {
        try
        {
            var inspect = await InspectAsync(tag, cancellationToken).ConfigureAwait(false);
            if (inspect.Id.Length > 0)
            {
                return inspect.Id;
            }
        }
        catch (DockerException)
        {
            // The builder may not have loaded the image into the local store; fall back to the log.
        }

        return ExtractImageIdFromLog(output);
    }

    /// <summary>
    /// Recovers an image id from a BuildKit log, for builders that do not load their result into the
    /// local image store.
    /// </summary>
    private static string ExtractImageIdFromLog(string output)
    {
        const string prefix = "sha256:";
        var index = output.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            var start = index + prefix.Length;
            var end = start;
            while (end < output.Length && Uri.IsHexDigit(output[end]))
            {
                end++;
            }

            if (end - start == 64)
            {
                return output[index..end];
            }

            index = output.IndexOf(prefix, index + prefix.Length, StringComparison.OrdinalIgnoreCase);
        }

        return string.Empty;
    }

    private static bool IsAuthenticationFailure(DockerException exception)
    {
        if (exception is DockerApiException api
            && api.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return true;
        }

        var message = exception.Message;
        foreach (var hint in AuthenticationHints)
        {
            if (message.Contains(hint, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Splits a reference into the part before the tag and the tag itself. A digest reference is
    /// returned whole, since the daemon accepts it in the <c>fromImage</c> parameter.
    /// </summary>
    private static (string Name, string Tag) SplitReference(string reference)
    {
        if (reference.Contains('@', StringComparison.Ordinal))
        {
            return (reference, null);
        }

        var lastSlash = reference.LastIndexOf('/');
        var lastColon = reference.LastIndexOf(':');

        return lastColon > lastSlash && lastColon >= 0
            ? (reference[..lastColon], reference[(lastColon + 1)..])
            : (reference, "latest");
    }

    /// <summary>
    /// Escapes an image reference for use in a path segment. The daemon's image routes match the
    /// rest of the path, so the separators an image reference is built from stay literal.
    /// </summary>
    private static string Reference(string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        return Uri.EscapeDataString(reference)
            .Replace("%2F", "/", StringComparison.Ordinal)
            .Replace("%3A", ":", StringComparison.Ordinal);
    }

    /// <summary>
    /// Collects build output in arrival order while forwarding it to the caller's receiver.
    /// </summary>
    private sealed class BuildLog(IProgress<string> inner) : IProgress<string>
    {
        private readonly StringBuilder _builder = new();

        public void Report(string value)
        {
            lock (_builder)
            {
                _builder.AppendLine(value);
            }

            inner?.Report(value);
        }

        public override string ToString()
        {
            lock (_builder)
            {
                return _builder.ToString();
            }
        }
    }
}
