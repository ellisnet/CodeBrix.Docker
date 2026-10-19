namespace CodeBrix.Docker;

/// <summary>
/// Options for <see cref="ImageOperations.PullAsync(string, ImagePullOptions, System.IProgress{string}, System.Threading.CancellationToken)"/>.
/// </summary>
/// <example>
/// <code>
/// await docker.Images.PullAsync("alpine:latest",
///     new ImagePullOptions { Platform = "linux/arm64" }, progress: null);
/// </code>
/// </example>
public sealed class ImagePullOptions
{
    /// <summary>
    /// Gets or sets the platform to pull, in <c>os/arch[/variant]</c> form, for example
    /// <c>linux/arm64</c> or <c>linux/riscv64</c>. It is sent as the <c>platform</c> query parameter of
    /// <c>POST /images/create</c> (and as <c>--platform</c> when the pull falls back to the
    /// <c>docker</c> command line). <see langword="null"/> or blank, the default, lets the daemon pick
    /// its own platform. One value only; a comma-separated list is rejected.
    /// </summary>
    /// <remarks>
    /// Running a container of a foreign architecture afterwards needs the host to emulate it - on Linux,
    /// a qemu-user binfmt handler registered for that architecture (for example
    /// <c>/proc/sys/fs/binfmt_misc/qemu-aarch64</c>).
    /// </remarks>
    public string Platform { get; set; }
}
