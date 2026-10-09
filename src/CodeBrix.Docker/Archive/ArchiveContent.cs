using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Docker;

/// <summary>
/// A request body of unknown length that a callback writes straight onto the connection, sent with
/// chunked transfer encoding and the <c>application/x-tar</c> media type. Nothing is buffered: the
/// archive is produced while it is being sent.
/// </summary>
internal sealed class ArchiveContent : HttpContent
{
    private const string TarMediaType = "application/x-tar";

    private readonly Func<Stream, CancellationToken, Task> _produce;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArchiveContent"/> class.
    /// </summary>
    /// <param name="produce">Writes the whole archive to the stream it is given.</param>
    public ArchiveContent(Func<Stream, CancellationToken, Task> produce)
    {
        _produce = produce ?? throw new ArgumentNullException(nameof(produce));
        Headers.ContentType = new MediaTypeHeaderValue(TarMediaType);
    }

    /// <inheritdoc />
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext context) =>
        _produce(stream, CancellationToken.None);

    /// <inheritdoc />
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext context,
        CancellationToken cancellationToken) =>
        _produce(stream, cancellationToken);

    /// <inheritdoc />
    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }
}
