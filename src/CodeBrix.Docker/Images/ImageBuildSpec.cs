using System;
using System.Collections.Generic;

namespace CodeBrix.Docker;

/// <summary>
/// Everything needed to build an image. <see cref="ContextDirectory"/> and at least one entry in
/// <see cref="Tags"/> are required.
/// </summary>
/// <remarks>
/// Builds run through the <c>docker</c> command line so that BuildKit is used; the Engine API's own
/// build endpoint drives the legacy builder only.
/// </remarks>
/// <example>
/// <code>
/// var result = await docker.Images.BuildAsync(new ImageBuildSpec
/// {
///     ContextDirectory = contextPath,
///     Tags = { "my-app:latest" },
///     Target = "runtime",
///     Labels = { ["org.opencontainers.image.source"] = "https://example.com/repo" },
/// });
/// </code>
/// </example>
public sealed class ImageBuildSpec
{
    /// <summary>Gets or sets the build context directory. Required.</summary>
    public string ContextDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Dockerfile path. When omitted, <c>Dockerfile</c> in
    /// <see cref="ContextDirectory"/> is used.
    /// </summary>
    public string DockerfilePath { get; set; }

    /// <summary>Gets or sets the tags to apply to the built image. At least one is required.</summary>
    public IList<string> Tags { get; set; } = [];

    /// <summary>Gets or sets the build arguments passed as <c>--build-arg</c>.</summary>
    public IDictionary<string, string> BuildArgs { get; set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets the multi-stage build stage to stop at. When omitted the final stage is built.
    /// </summary>
    public string Target { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to always attempt to pull a newer version of the base
    /// images.
    /// </summary>
    public bool Pull { get; set; }

    /// <summary>Gets or sets a value indicating whether to ignore the build cache.</summary>
    public bool NoCache { get; set; }

    /// <summary>Gets or sets the labels to apply to the built image.</summary>
    public IDictionary<string, string> Labels { get; set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets an optional receiver for build log lines as they arrive. The same lines are
    /// available in <see cref="ImageBuildResult.Output"/> once the build finishes.
    /// </summary>
    public IProgress<string> Output { get; set; }
}
