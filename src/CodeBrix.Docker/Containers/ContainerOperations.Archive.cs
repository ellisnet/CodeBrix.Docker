using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Docker;

/// <summary>
/// Copying files into and out of containers, over the Engine API's <c>/containers/{id}/archive</c>
/// endpoint.
/// </summary>
public sealed partial class ContainerOperations
{
    private const string PathStatHeader = "X-Docker-Container-Path-Stat";

    // ---------------------------------------------------------------------------------------
    // Copying: stream level
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Extracts a tar archive into a directory inside a container. The container may be running or
    /// merely created.
    /// </summary>
    /// <param name="idOrName">The container id or name.</param>
    /// <param name="tarStream">
    /// An uncompressed tar archive (the daemon also accepts gzip, bzip2 and xz). It is read once, from
    /// its current position, and streamed to the daemon without being buffered; it is not disposed.
    /// </param>
    /// <param name="containerPath">
    /// The directory to extract into. It must already exist in the container; the archive's entry
    /// names are relative to it.
    /// </param>
    /// <param name="options">
    /// Optional. Only <see cref="CopyToContainerOptions.NoOverwriteDirNonDir"/> and
    /// <see cref="CopyToContainerOptions.CopyUidGid"/> apply to a caller-built archive.
    /// </param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the daemon has extracted the archive.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tarStream"/> is <see langword="null"/>.</exception>
    /// <exception cref="DockerContainerNotFoundException">No such container exists.</exception>
    /// <exception cref="DockerApiException">
    /// Status 404: <paramref name="containerPath"/> does not exist. Status 400: it is not a directory, or
    /// it is on a read-only mount or a read-only root file system (older daemons answer 403 for a
    /// read-only volume).
    /// </exception>
    /// <remarks>No timeout is applied, so a large archive is not cut off; cancel through
    /// <paramref name="cancellationToken"/> instead.</remarks>
    public Task CopyToContainerAsync(string idOrName, Stream tarStream, string containerPath,
        CopyToContainerOptions options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tarStream);
        return PutArchiveAsync(idOrName, containerPath, options,
            (destination, token) => tarStream.CopyToAsync(destination, token), cancellationToken);
    }

    /// <summary>
    /// Reads a file or directory out of a container as a tar archive.
    /// </summary>
    /// <param name="idOrName">The container id or name.</param>
    /// <param name="containerPath">The file or directory to read.</param>
    /// <param name="cancellationToken">A cancellation token covering the request.</param>
    /// <returns>
    /// The raw, uncompressed tar stream, read straight off the connection. The caller owns it and must
    /// dispose it. The archive's root entry is named after the last segment of
    /// <paramref name="containerPath"/>: reading <c>/etc</c> yields <c>etc/</c>, <c>etc/hosts</c> and so
    /// on, and reading <c>/etc/hosts</c> yields the single entry <c>hosts</c>.
    /// </returns>
    /// <exception cref="DockerContainerNotFoundException">No such container exists.</exception>
    /// <exception cref="DockerApiException">Status 404: <paramref name="containerPath"/> does not exist.</exception>
    /// <remarks>No timeout is applied once the response has started.</remarks>
    public async Task<Stream> CopyFromContainerAsync(string idOrName, string containerPath,
        CancellationToken cancellationToken = default)
    {
        var response = await SendArchiveRequestAsync(HttpMethod.Get, idOrName, containerPath, content: null,
            HttpCompletionOption.ResponseHeadersRead, applyTimeout: false, cancellationToken).ConfigureAwait(false);

        try
        {
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return new HttpResponseStream(stream, response);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Describes one path inside a container — whether it exists, what kind of node it is, its size,
    /// mode and modification time — without transferring any content.
    /// </summary>
    /// <param name="idOrName">The container id or name.</param>
    /// <param name="containerPath">The path to describe. A symbolic link is described, not followed.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The path's description.</returns>
    /// <exception cref="DockerContainerNotFoundException">No such container exists.</exception>
    /// <exception cref="DockerApiException">Status 404: <paramref name="containerPath"/> does not exist.</exception>
    public async Task<ContainerPathStat> StatPathAsync(string idOrName, string containerPath,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendArchiveRequestAsync(HttpMethod.Head, idOrName, containerPath, content: null,
            HttpCompletionOption.ResponseContentRead, applyTimeout: true, cancellationToken).ConfigureAwait(false);

        return ParsePathStat(response, containerPath);
    }

    // ---------------------------------------------------------------------------------------
    // Copying: host folders and files
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Copies a host folder into a container: its contents, or the folder itself when
    /// <see cref="CopyToContainerOptions.IncludeRootFolder"/> is set. The archive is built while it is
    /// sent, so a large tree is never held in memory.
    /// </summary>
    /// <param name="idOrName">The container id or name. It may be running or merely created.</param>
    /// <param name="hostDirectory">The host folder to copy.</param>
    /// <param name="containerPath">The directory to copy into. It must already exist in the container.</param>
    /// <param name="options">Exclusions, modes, link handling and progress; <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the daemon has extracted everything.</returns>
    /// <exception cref="DirectoryNotFoundException"><paramref name="hostDirectory"/> does not exist.</exception>
    /// <exception cref="DockerContainerNotFoundException">No such container exists.</exception>
    /// <exception cref="DockerApiException">
    /// Status 404: <paramref name="containerPath"/> does not exist. Status 400: it is not a directory, or
    /// it is on a read-only mount or a read-only root file system (older daemons answer 403 for a
    /// read-only volume).
    /// </exception>
    public Task CopyDirectoryToContainerAsync(string idOrName, string hostDirectory, string containerPath,
        CopyToContainerOptions options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostDirectory);
        if (!Directory.Exists(hostDirectory))
        {
            throw new DirectoryNotFoundException($"The host directory '{hostDirectory}' does not exist.");
        }

        var writer = new ContainerArchiveWriter(options);
        return PutArchiveAsync(idOrName, containerPath, options,
            (destination, token) => writer.WriteDirectoryAsync(destination, hostDirectory, token), cancellationToken);
    }

    /// <summary>
    /// Copies one host file into a directory inside a container, keeping its file name.
    /// </summary>
    /// <param name="idOrName">The container id or name. It may be running or merely created.</param>
    /// <param name="hostFilePath">The host file to copy.</param>
    /// <param name="containerDirectory">The directory to copy into. It must already exist in the container.</param>
    /// <param name="options">
    /// Modes and progress; <see langword="null"/> for the defaults. The folder-only options
    /// (<see cref="CopyToContainerOptions.ExcludePatterns"/>,
    /// <see cref="CopyToContainerOptions.IncludeRootFolder"/>) do not apply.
    /// </param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the daemon has written the file.</returns>
    /// <exception cref="FileNotFoundException"><paramref name="hostFilePath"/> does not exist.</exception>
    /// <exception cref="DockerContainerNotFoundException">No such container exists.</exception>
    /// <exception cref="DockerApiException">
    /// Status 404: <paramref name="containerDirectory"/> does not exist. Status 400: it is not a
    /// directory, or it is on a read-only mount or a read-only root file system (older daemons answer
    /// 403 for a read-only volume).
    /// </exception>
    public Task CopyFileToContainerAsync(string idOrName, string hostFilePath, string containerDirectory,
        CopyToContainerOptions options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostFilePath);
        if (!File.Exists(hostFilePath))
        {
            throw new FileNotFoundException($"The host file '{hostFilePath}' does not exist.", hostFilePath);
        }

        var writer = new ContainerArchiveWriter(options);
        return PutArchiveAsync(idOrName, containerDirectory, options,
            (destination, token) => writer.WriteFileAsync(destination, hostFilePath, token), cancellationToken);
    }

    /// <summary>
    /// Copies a file or directory out of a container into a host folder, extracting as it reads.
    /// </summary>
    /// <param name="idOrName">The container id or name. It may be running or stopped.</param>
    /// <param name="containerPath">The file or directory to copy.</param>
    /// <param name="hostDirectory">The host folder to extract into; created when missing.</param>
    /// <param name="options">Root stripping, overwrite, modes, link handling and progress; <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>What was written and what was skipped.</returns>
    /// <exception cref="DockerContainerNotFoundException">No such container exists.</exception>
    /// <exception cref="DockerApiException">Status 404: <paramref name="containerPath"/> does not exist.</exception>
    /// <exception cref="DockerException">
    /// An archive entry would resolve outside <paramref name="hostDirectory"/> (an absolute name, a drive
    /// letter, <c>..</c>, or a path through a symbolic link that leads outside). Nothing is written for
    /// that entry or after it.
    /// </exception>
    /// <remarks>
    /// By default the archive's root folder is kept, so copying <c>/app</c> into <c>out</c> produces
    /// <c>out/app/...</c>; set <see cref="CopyFromContainerOptions.StripRootFolder"/> to place the
    /// contents of <c>/app</c> directly in <c>out</c>. A file being written when the copy fails or is
    /// cancelled is deleted rather than left half-written; files already finished stay.
    /// </remarks>
    public async Task<CopyFromContainerResult> CopyFromContainerToDirectoryAsync(string idOrName,
        string containerPath, string hostDirectory, CopyFromContainerOptions options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostDirectory);

        await using var archive = await CopyFromContainerAsync(idOrName, containerPath, cancellationToken)
            .ConfigureAwait(false);

        return await ContainerArchiveExtractor
            .ExtractAsync(archive, containerPath, hostDirectory, options, cancellationToken)
            .ConfigureAwait(false);
    }

    // ---------------------------------------------------------------------------------------
    // Archive helpers
    // ---------------------------------------------------------------------------------------

    private async Task PutArchiveAsync(string idOrName, string containerPath, CopyToContainerOptions options,
        Func<Stream, CancellationToken, Task> produce, CancellationToken cancellationToken)
    {
        using var response = await SendArchiveRequestAsync(HttpMethod.Put, idOrName, containerPath,
            new ArchiveContent(produce), HttpCompletionOption.ResponseContentRead, applyTimeout: false,
            cancellationToken, options).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendArchiveRequestAsync(HttpMethod method, string idOrName,
        string containerPath, HttpContent content, HttpCompletionOption completionOption, bool applyTimeout,
        CancellationToken cancellationToken, CopyToContainerOptions options = null)
    {
        string path;
        try
        {
            var reference = Reference(idOrName);
            ArgumentException.ThrowIfNullOrWhiteSpace(containerPath);

            var query = new QueryStringBuilder().Add("path", containerPath);
            if (options is not null)
            {
                query.AddIfTrue("noOverwriteDirNonDir", options.NoOverwriteDirNonDir)
                     .AddIfTrue("copyUIDGID", options.CopyUidGid);
            }

            path = query.AppendTo($"containers/{reference}/archive");
        }
        catch
        {
            content?.Dispose();
            throw;
        }

        try
        {
            return await _api.SendAsync(method, path, content, completionOption, applyTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (DockerApiException ex)
        {
            throw await TranslateArchiveFailureAsync(ex, method, idOrName, containerPath, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (DockerException ex) when (method == HttpMethod.Put && ex.InnerException is HttpRequestException)
        {
            throw await DiagnoseFailedUploadAsync(ex, idOrName, containerPath, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Works out why an upload failed at the transport level. The daemon validates the container and
    /// the destination before it reads the archive, and when it refuses it answers and closes the
    /// connection at once; an upload larger than the socket buffers is then cut off part way, and the
    /// client sees a broken pipe instead of the daemon's answer. The same checks are repeated here
    /// with requests that carry no body, so the caller gets the reason rather than the symptom.
    /// </summary>
    private async Task<DockerException> DiagnoseFailedUploadAsync(DockerException failure, string idOrName,
        string containerPath, CancellationToken cancellationToken)
    {
        ContainerPathStat stat;
        try
        {
            using var response = await _api.SendAsync(HttpMethod.Head,
                    new QueryStringBuilder().Add("path", containerPath)
                        .AppendTo($"containers/{Reference(idOrName)}/archive"),
                    content: null, HttpCompletionOption.ResponseContentRead, applyTimeout: true, cancellationToken)
                .ConfigureAwait(false);
            stat = ParsePathStat(response, containerPath);
        }
        catch (DockerApiException ex)
        {
            return await TranslateArchiveFailureAsync(ex, HttpMethod.Put, idOrName, containerPath, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (DockerException)
        {
            return failure;
        }

        if (!stat.IsDirectory)
        {
            return new DockerApiException(HttpStatusCode.BadRequest, string.Empty,
                $"The daemon refused to copy into '{containerPath}' in container '{idOrName}': the destination " +
                "is not a directory. Copying into a container needs an existing directory.");
        }

        try
        {
            var inspect = await InspectAsync(idOrName, cancellationToken).ConfigureAwait(false);
            var destination = containerPath.TrimEnd('/') + "/";
            var readOnlyMount = inspect.Mounts?.FirstOrDefault(mount => !mount.ReadWrite
                && !string.IsNullOrEmpty(mount.Destination)
                && destination.StartsWith(mount.Destination.TrimEnd('/') + "/", StringComparison.Ordinal));

            if (readOnlyMount is not null)
            {
                return new DockerApiException(HttpStatusCode.BadRequest, string.Empty,
                    $"The daemon refused to copy into '{containerPath}' in container '{idOrName}': the " +
                    $"destination is on the read-only mount '{readOnlyMount.Destination}'.");
            }

            if (inspect.HostConfig?.ReadonlyRootfs == true)
            {
                return new DockerApiException(HttpStatusCode.BadRequest, string.Empty,
                    $"The daemon refused to copy into '{containerPath}' in container '{idOrName}': the " +
                    "container's root file system is read-only.");
            }
        }
        catch (DockerException)
        {
            // Fall through to the original failure.
        }

        return failure;
    }

    /// <summary>
    /// Rewords an archive-endpoint failure so that it says which thing was wrong. The daemon answers
    /// 404 both for a missing container and for a missing path, and a HEAD response has no body to
    /// tell them apart, so the container is looked up when the body does not say.
    /// </summary>
    private async Task<DockerApiException> TranslateArchiveFailureAsync(DockerApiException failure,
        HttpMethod method, string idOrName, string containerPath, CancellationToken cancellationToken)
    {
        var daemonMessage = DockerApiException.TryExtractDaemonMessage(failure.ResponseBody);
        var detail = daemonMessage is null ? string.Empty : $" The daemon said: {daemonMessage}";
        var writing = method == HttpMethod.Put;

        switch (failure.StatusCode)
        {
            case HttpStatusCode.NotFound:
            {
                bool containerMissing;
                if (daemonMessage is not null)
                {
                    containerMissing = daemonMessage.Contains("No such container", StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    containerMissing = !await _api
                        .TryGetAsync($"containers/{Reference(idOrName)}/json", cancellationToken)
                        .ConfigureAwait(false);
                }

                if (containerMissing)
                {
                    return new DockerContainerNotFoundException(failure.StatusCode, failure.ResponseBody,
                        $"No such container: '{idOrName}'.{detail}");
                }

                return new DockerApiException(failure.StatusCode, failure.ResponseBody, writing
                    ? $"The destination path '{containerPath}' does not exist in container '{idOrName}'. " +
                      $"Copying into a container needs an existing directory; create it first.{detail}"
                    : $"The path '{containerPath}' does not exist in container '{idOrName}'.{detail}");
            }

            case HttpStatusCode.BadRequest when writing:
                return new DockerApiException(failure.StatusCode, failure.ResponseBody,
                    $"The daemon refused to copy into '{containerPath}' in container '{idOrName}': the destination " +
                    "must be an existing directory, on neither a read-only mount nor a read-only root file " +
                    $"system.{detail}");

            case HttpStatusCode.Forbidden when writing:
                return new DockerApiException(failure.StatusCode, failure.ResponseBody,
                    $"The daemon refused to copy into '{containerPath}' in container '{idOrName}': the destination " +
                    $"is on a read-only volume or bind mount.{detail}");

            default:
                return failure;
        }
    }

    private static ContainerPathStat ParsePathStat(HttpResponseMessage response, string containerPath)
    {
        if (!response.Headers.TryGetValues(PathStatHeader, out var values)
            || values.FirstOrDefault() is not { Length: > 0 } encoded)
        {
            throw new DockerException(
                $"The Docker daemon did not return a {PathStatHeader} header for '{containerPath}'.");
        }

        ContainerPathStatWire wire;
        try
        {
            wire = DockerJson.Deserialize<ContainerPathStatWire>(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            throw new DockerException(
                $"Could not parse the {PathStatHeader} header for '{containerPath}': {ex.Message}", ex);
        }

        if (wire is null)
        {
            throw new DockerException($"The {PathStatHeader} header for '{containerPath}' was empty.");
        }

        return new ContainerPathStat(wire.Name ?? string.Empty, wire.Size, wire.Mode,
            wire.Modified ?? DateTimeOffset.MinValue, wire.LinkTarget ?? string.Empty);
    }
}
