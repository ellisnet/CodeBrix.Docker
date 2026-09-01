using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Docker;

/// <summary>
/// Performs the one HTTP request that turns a daemon connection into a raw bidirectional pipe.
/// </summary>
/// <remarks>
/// <para>
/// The Engine API hijacks the connection for <c>POST /exec/{id}/start</c> and
/// <c>POST /containers/{id}/attach</c>: the request carries <c>Connection: Upgrade</c> and
/// <c>Upgrade: tcp</c>, the daemon answers <c>101 UPGRADED</c>, and everything after the blank line
/// is the container's standard streams. <see cref="System.Net.Http.HttpClient"/> cannot hand that
/// socket back in a form that allows standard input to be closed on its own, so the request is
/// written by hand over the same transport <see cref="DockerConnectionFactory"/> dials for ordinary
/// calls.
/// </para>
/// <para>
/// A daemon that refuses the request answers normally instead — a JSON error body with a status code —
/// and that answer is translated into the usual CodeBrix.Docker exception rather than being left to
/// surface later as an unreadable stream.
/// </para>
/// </remarks>
internal static class DockerHijackConnection
{
    private const int ReadChunkSize = 8192;
    private const int MaxHeaderBytes = 65536;
    private const int MaxErrorBodyBytes = 65536;
    private const string HostHeaderValue = "localhost";
    private const string UserAgentHeaderValue = "CodeBrix.Docker";

    /// <summary>
    /// Issues a hijack request and returns the raw connection the daemon upgraded to.
    /// </summary>
    /// <param name="endpoint">The parsed daemon endpoint.</param>
    /// <param name="options">
    /// The client options. <see cref="DockerClientOptions.DefaultTimeout"/> bounds the connect, request
    /// and response-header phases together; <see cref="Timeout.InfiniteTimeSpan"/> or a non-positive
    /// value waits indefinitely, and the timeout never applies to the hijacked stream itself.
    /// </param>
    /// <param name="path">The unversioned Engine API path, for example <c>exec/{id}/start</c>.</param>
    /// <param name="jsonBody">The JSON request body, or <see langword="null"/> for no body.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The upgraded connection.</returns>
    /// <exception cref="DockerApiException">The daemon rejected the request.</exception>
    /// <exception cref="DockerException">The daemon answered something other than an upgrade.</exception>
    /// <exception cref="TimeoutException">The handshake did not complete in time.</exception>
    public static async Task<HijackedStream> PostAsync(DockerEndpoint endpoint, DockerClientOptions options,
        string path, string jsonBody, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var handshakeTimeout = options.DefaultTimeout;

        using var timeoutSource = handshakeTimeout > TimeSpan.Zero
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : null;
        timeoutSource?.CancelAfter(handshakeTimeout);
        var token = timeoutSource?.Token ?? cancellationToken;

        try
        {
            return await HandshakeAsync(endpoint, options, path, jsonBody, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutSource is not null)
        {
            throw new TimeoutException(
                $"The Docker API request 'POST {path}' timed out after {handshakeTimeout} while opening a " +
                "hijacked stream.");
        }
    }

    private static async Task<HijackedStream> HandshakeAsync(DockerEndpoint endpoint, DockerClientOptions options,
        string path, string jsonBody, CancellationToken cancellationToken)
    {
        var stream = await DockerConnectionFactory
            .ConnectAsync(endpoint, options, writeClosable: true, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await stream.WriteAsync(BuildRequest(path, jsonBody), cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

            var (buffer, headerEnd, filled) = await ReadHeadersAsync(stream, path, cancellationToken)
                .ConfigureAwait(false);
            var header = Encoding.ASCII.GetString(buffer, 0, headerEnd);
            var (statusCode, headers) = ParseHead(header, path);

            if (statusCode is not (HttpStatusCode.SwitchingProtocols or HttpStatusCode.OK))
            {
                var body = await ReadErrorBodyAsync(stream, buffer, headerEnd, filled, headers, cancellationToken)
                    .ConfigureAwait(false);
                throw DockerApiClient.CreateApiException(statusCode, body, path);
            }

            headers.TryGetValue("content-type", out var contentType);
            var hijacked = new HijackedStream(stream, contentType, buffer, headerEnd, filled);
            stream = null;
            return hijacked;
        }
        finally
        {
            if (stream is not null)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Builds the request bytes. Written by hand because the point of the exercise is to keep the
    /// socket afterwards.
    /// </summary>
    private static byte[] BuildRequest(string path, string jsonBody)
    {
        byte[] payload = jsonBody is null ? [] : Encoding.UTF8.GetBytes(jsonBody);
        var target = path.StartsWith('/') ? path : "/" + path;

        var head = new StringBuilder()
            .Append("POST ").Append(target).Append(" HTTP/1.1\r\n")
            .Append("Host: ").Append(HostHeaderValue).Append("\r\n")
            .Append("User-Agent: ").Append(UserAgentHeaderValue).Append("\r\n")
            .Append("Accept: */*\r\n")
            .Append("Content-Type: application/json\r\n")
            .Append("Content-Length: ").Append(payload.Length.ToString(CultureInfo.InvariantCulture)).Append("\r\n")
            .Append("Connection: Upgrade\r\n")
            .Append("Upgrade: tcp\r\n")
            .Append("\r\n")
            .ToString();

        var headBytes = Encoding.ASCII.GetBytes(head);
        if (payload.Length == 0)
        {
            return headBytes;
        }

        var request = new byte[headBytes.Length + payload.Length];
        headBytes.CopyTo(request, 0);
        payload.CopyTo(request, headBytes.Length);
        return request;
    }

    /// <summary>
    /// Reads until the blank line that ends the response head, keeping any body bytes that arrived in
    /// the same read.
    /// </summary>
    /// <returns>
    /// The buffer, the index one past the blank line, and the index one past the last byte read.
    /// </returns>
    private static async Task<(byte[] Buffer, int HeaderEnd, int Filled)> ReadHeadersAsync(Stream stream, string path,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[ReadChunkSize];
        var filled = 0;
        var scanned = 0;

        while (true)
        {
            if (filled == buffer.Length)
            {
                if (buffer.Length >= MaxHeaderBytes)
                {
                    throw new DockerException(
                        $"The Docker daemon sent more than {MaxHeaderBytes} bytes of response headers for " +
                        $"'POST {path}'.");
                }

                Array.Resize(ref buffer, Math.Min(buffer.Length * 2, MaxHeaderBytes));
            }

            var read = await stream.ReadAsync(buffer.AsMemory(filled), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new DockerException(
                    $"The Docker daemon closed the connection before answering 'POST {path}'.");
            }

            filled += read;

            for (var i = Math.Max(scanned, 3); i < filled; i++)
            {
                if (buffer[i] == (byte)'\n' && buffer[i - 1] == (byte)'\r'
                    && buffer[i - 2] == (byte)'\n' && buffer[i - 3] == (byte)'\r')
                {
                    return (buffer, i + 1, filled);
                }
            }

            // The blank line can straddle two reads, so keep the last three bytes in the scan window.
            scanned = Math.Max(filled - 3, 0);
        }
    }

    /// <summary>Splits the response head into its status code and its headers, lower-cased by name.</summary>
    private static (HttpStatusCode StatusCode, Dictionary<string, string> Headers) ParseHead(string head, string path)
    {
        var lines = head.Split("\r\n", StringSplitOptions.None);
        var statusLine = lines.Length > 0 ? lines[0] : string.Empty;

        var firstSpace = statusLine.IndexOf(' ');
        var secondSpace = firstSpace < 0 ? -1 : statusLine.IndexOf(' ', firstSpace + 1);
        var codeText = firstSpace < 0
            ? string.Empty
            : secondSpace < 0
                ? statusLine[(firstSpace + 1)..]
                : statusLine[(firstSpace + 1)..secondSpace];

        if (!int.TryParse(codeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
        {
            throw new DockerException(
                $"The Docker daemon answered 'POST {path}' with an unreadable status line: '{statusLine}'.");
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0)
            {
                break;
            }

            var colon = line.IndexOf(':');
            if (colon > 0)
            {
                headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
            }
        }

        return ((HttpStatusCode)code, headers);
    }

    /// <summary>Reads the error body of a refused hijack, so the exception carries the daemon's message.</summary>
    private static async Task<string> ReadErrorBodyAsync(Stream stream, byte[] buffer, int headerEnd, int filled,
        Dictionary<string, string> headers, CancellationToken cancellationToken)
    {
        using var body = new MemoryStream();
        body.Write(buffer, headerEnd, filled - headerEnd);

        var expected = -1;
        if (headers.TryGetValue("content-length", out var lengthText)
            && int.TryParse(lengthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            expected = parsed;
        }

        var chunk = new byte[ReadChunkSize];
        while (body.Length < MaxErrorBodyBytes && (expected < 0 || body.Length < expected))
        {
            int read;
            try
            {
                read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
                break;
            }

            if (read == 0)
            {
                break;
            }

            body.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(body.GetBuffer(), 0, (int)body.Length);
    }
}
