using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Docker;

/// <summary>
/// The HTTP plumbing shared by every operation class: one <see cref="HttpClient"/> bound to the
/// daemon transport, uniform error translation, per-call timeouts and JSON helpers.
/// </summary>
internal sealed class DockerApiClient : IDisposable
{
    private const string JsonMediaType = "application/json";

    private readonly HttpClient _http;
    private readonly SocketsHttpHandler _handler;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="DockerApiClient"/> class.
    /// </summary>
    /// <param name="options">The client options.</param>
    public DockerApiClient(DockerClientOptions options)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Endpoint = DockerEndpoint.Parse(DockerEndpoint.Resolve(options.Endpoint));
        _handler = DockerConnectionFactory.CreateHandler(Endpoint);
        _http = new HttpClient(_handler, disposeHandler: false)
        {
            BaseAddress = new Uri("http://localhost/"),
            // Per-call timeouts are applied with linked cancellation tokens so that streaming
            // endpoints (logs, stats, events, wait) are never cut off.
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    /// <summary>Gets the options this client was created with.</summary>
    public DockerClientOptions Options { get; }

    /// <summary>Gets the resolved daemon endpoint.</summary>
    public DockerEndpoint Endpoint { get; }

    // ---------------------------------------------------------------------------------------
    // JSON requests
    // ---------------------------------------------------------------------------------------

    /// <summary>Issues a GET and deserializes the JSON response body.</summary>
    public async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, path, content: null,
            HttpCompletionOption.ResponseContentRead, applyTimeout: true, cancellationToken).ConfigureAwait(false);
        return await ReadJsonAsync<T>(response, path, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Issues a GET and returns the raw response body as a string.</summary>
    public async Task<string> GetStringAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, path, content: null,
            HttpCompletionOption.ResponseContentRead, applyTimeout: true, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Issues a POST with an optional JSON body and deserializes the JSON response body.</summary>
    public async Task<T> PostAsync<T>(string path, object body, CancellationToken cancellationToken,
        bool applyTimeout = true)
    {
        using var response = await SendAsync(HttpMethod.Post, path, CreateJsonContent(body),
            HttpCompletionOption.ResponseContentRead, applyTimeout, cancellationToken).ConfigureAwait(false);
        return await ReadJsonAsync<T>(response, path, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Issues a POST with an optional JSON body and discards the response body.</summary>
    public async Task PostAsync(string path, object body, CancellationToken cancellationToken,
        bool applyTimeout = true)
    {
        using var response = await SendAsync(HttpMethod.Post, path, CreateJsonContent(body),
            HttpCompletionOption.ResponseContentRead, applyTimeout, cancellationToken).ConfigureAwait(false);
        _ = response;
    }

    /// <summary>Issues a DELETE and discards the response body.</summary>
    public async Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Delete, path, content: null,
            HttpCompletionOption.ResponseContentRead, applyTimeout: true, cancellationToken).ConfigureAwait(false);
        _ = response;
    }

    /// <summary>
    /// Issues a GET and returns whether the daemon answered with a success status code, without throwing
    /// when the daemon is unreachable.
    /// </summary>
    public async Task<bool> TryGetAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendAsync(HttpMethod.Get, path, content: null,
                HttpCompletionOption.ResponseContentRead, applyTimeout: true, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is DockerException or HttpRequestException or IOException or TimeoutException)
        {
            return false;
        }
    }

    // ---------------------------------------------------------------------------------------
    // Streaming requests
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Issues a GET and returns the response body as a stream. No timeout is applied; disposing the
    /// returned stream releases the underlying connection.
    /// </summary>
    public Task<Stream> GetStreamAsync(string path, CancellationToken cancellationToken) =>
        SendForStreamAsync(HttpMethod.Get, path, body: null, cancellationToken);

    /// <summary>
    /// Issues a POST with an optional JSON body and returns the response body as a stream. Used for the
    /// hijacked exec output stream. No timeout is applied.
    /// </summary>
    public Task<Stream> PostForStreamAsync(string path, object body, CancellationToken cancellationToken) =>
        SendForStreamAsync(HttpMethod.Post, path, body, cancellationToken);

    private async Task<Stream> SendForStreamAsync(HttpMethod method, string path, object body,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync(method, path, CreateJsonContent(body),
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
    /// Issues a GET against a JSON-lines endpoint (events, progress, stats streams) and yields each
    /// decoded object as it arrives.
    /// </summary>
    public async IAsyncEnumerable<T> GetJsonLinesAsync<T>(string path,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var stream = await GetStreamAsync(path, cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);

        while (!cancellationToken.IsCancellationRequested)
        {
            string line;
            try
            {
                line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (IOException) when (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }
            catch (ObjectDisposedException)
            {
                yield break;
            }

            if (line is null)
            {
                yield break;
            }

            if (line.Length == 0 || line.AsSpan().IsWhiteSpace())
            {
                continue;
            }

            T value;
            try
            {
                value = DockerJson.Deserialize<T>(line);
            }
            catch (JsonException)
            {
                continue;
            }

            if (value is not null)
            {
                yield return value;
            }
        }
    }

    // ---------------------------------------------------------------------------------------
    // Core send
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Sends a request, translating daemon errors into the CodeBrix.Docker exception hierarchy.
    /// </summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The unversioned Engine API path, for example <c>containers/json?all=true</c>.</param>
    /// <param name="content">The request body, if any. Ownership transfers to this method.</param>
    /// <param name="completionOption">Whether to buffer the whole response.</param>
    /// <param name="applyTimeout">Whether <see cref="DockerClientOptions.DefaultTimeout"/> applies.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>The successful response. The caller owns it.</returns>
    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent content,
        HttpCompletionOption completionOption, bool applyTimeout, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var timeoutSource = applyTimeout && Options.DefaultTimeout > TimeSpan.Zero
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : null;
        timeoutSource?.CancelAfter(Options.DefaultTimeout);
        var effectiveToken = timeoutSource?.Token ?? cancellationToken;

        using var request = new HttpRequestMessage(method, path) { Content = content };

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, completionOption, effectiveToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutSource is not null)
        {
            throw new TimeoutException(
                $"The Docker API request '{method} {path}' timed out after {Options.DefaultTimeout}.");
        }
        catch (HttpRequestException ex)
        {
            throw new DockerException(
                $"The Docker API request '{method} {path}' failed: {ex.Message} " +
                $"(endpoint '{Endpoint.Original}').", ex);
        }

        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotModified)
        {
            return response;
        }

        using (response)
        {
            string body;
            try
            {
                body = await response.Content.ReadAsStringAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                body = string.Empty;
            }

            throw CreateApiException(response.StatusCode, body, path);
        }
    }

    /// <summary>
    /// Builds the most specific exception available for a daemon error response.
    /// </summary>
    internal static DockerApiException CreateApiException(HttpStatusCode statusCode, string body, string path)
    {
        if (statusCode != HttpStatusCode.NotFound)
        {
            return new DockerApiException(statusCode, body);
        }

        var normalized = path.TrimStart('/');

        // A 404 from the create endpoint means the image is missing, not the container.
        if (normalized.StartsWith("containers/create", StringComparison.OrdinalIgnoreCase))
        {
            return new DockerImageNotFoundException(statusCode, body);
        }

        if (normalized.StartsWith("containers/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("exec/", StringComparison.OrdinalIgnoreCase))
        {
            return new DockerContainerNotFoundException(statusCode, body);
        }

        if (normalized.StartsWith("images/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("build", StringComparison.OrdinalIgnoreCase))
        {
            return new DockerImageNotFoundException(statusCode, body);
        }

        return new DockerApiException(statusCode, body);
    }

    private static HttpContent CreateJsonContent(object body) =>
        body is null ? null : new StringContent(DockerJson.Serialize(body), Encoding.UTF8, JsonMediaType);

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, string path,
        CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new DockerException($"The Docker API returned an empty body for '{path}'.");
        }

        T value;
        try
        {
            value = DockerJson.Deserialize<T>(json);
        }
        catch (JsonException ex)
        {
            throw new DockerException($"Could not parse the Docker API response for '{path}': {ex.Message}", ex);
        }

        return value ?? throw new DockerException($"The Docker API returned a null body for '{path}'.");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _http.Dispose();
        _handler.Dispose();
    }
}
