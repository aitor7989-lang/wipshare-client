using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace WipShare.Client.Upload;

public sealed record InitiateResponse(
    string Id,
    string UploadUrl,
    string? ThumbnailUploadUrl,
    string CompleteUrl,
    string ViewerUrl);

public sealed record CompleteResponse(string Status, string ViewerUrl);

/// <summary>
/// Thrown on any non-2xx from the WipShare API or a network failure.
/// <see cref="IsRetryable"/> distinguishes transient (429, 5xx, network) from
/// permanent (4xx like 401/400) so the queue knows whether to back off or fail.
/// </summary>
public sealed class UploadException : Exception
{
    public int StatusCode { get; }   // 0 == no HTTP response (network/timeout)
    public string? ErrorCode { get; } // error.code from the JSON envelope, if any

    public UploadException(int statusCode, string? errorCode, string message, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }

    public bool IsRetryable => StatusCode == 0 || StatusCode == 429 || StatusCode >= 500;
}

/// <summary>
/// Typed wrapper over the WipShare backend. One reused HttpClient. Bearer auth
/// is attached only to initiate/complete — never to the R2 PUT (the presigned
/// URL carries its own auth). The secret is never logged.
/// </summary>
public sealed class WipShareApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _secret;
    private readonly string _ownerToken;
    private readonly ILogger<WipShareApiClient> _logger;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonRead = new()
    {
        PropertyNameCaseInsensitive = true,
    };
    private static readonly JsonSerializerOptions JsonWrite = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <param name="baseUrl">The backend base URL (from Constants — never user-editable).</param>
    /// <param name="secret">The invite code / bearer token (from Credential Manager via AccessConfig).</param>
    /// <param name="ownerToken">Per-device owner token, sent on initiate for a future library.</param>
    public WipShareApiClient(string baseUrl, string secret, string ownerToken, ILoggerFactory loggerFactory)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromMinutes(5), // generous for a ~15 MB PUT on slow links
        };
        _secret = secret;
        _ownerToken = ownerToken;
        _logger = loggerFactory.CreateLogger<WipShareApiClient>();
    }

    public async Task<InitiateResponse> InitiateAsync(
        long sizeBytes, string mime, int width, int height, long? thumbSizeBytes, CancellationToken ct)
    {
        var payload = new InitiateRequest(sizeBytes, mime, width, height, thumbSizeBytes, _ownerToken);
        using var req = new HttpRequestMessage(HttpMethod.Post, "api/clips/initiate")
        {
            // Fully qualified: the project has a sibling WipShare.Client.Encoding
            // namespace that otherwise shadows System.Text.Encoding here.
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonWrite), System.Text.Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _secret);
        return await SendForDataAsync<InitiateResponse>(req, "initiate", ct).ConfigureAwait(false);
    }

    public async Task<CompleteResponse> CompleteAsync(string completeUrl, CancellationToken ct)
    {
        // completeUrl is a site-relative path like "/api/clips/{id}/complete".
        using var req = new HttpRequestMessage(HttpMethod.Post, completeUrl.TrimStart('/'));
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _secret);
        return await SendForDataAsync<CompleteResponse>(req, "complete", ct).ConfigureAwait(false);
    }

    /// <summary>
    /// PUTs a local file to a presigned R2 URL. No bearer token — the presign
    /// carries auth. Content-Type and Content-Length must match what initiate
    /// signed, so we stream the file with an explicit content type.
    /// </summary>
    /// <param name="onBytesSent">
    /// Optional cumulative-bytes-sent callback for upload progress. Invoked on the
    /// upload thread; marshal to the UI thread in the handler.
    /// </param>
    public async Task PutFileAsync(
        string presignedUrl, string filePath, string contentType, CancellationToken ct, Action<long>? onBytesSent = null)
    {
        try
        {
            await using var fs = File.OpenRead(filePath);
            Stream body = onBytesSent is null ? fs : new ProgressStream(fs, onBytesSent);
            using var content = new StreamContent(body);
            content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            content.Headers.ContentLength = fs.Length;
            using var resp = await _http.PutAsync(presignedUrl, content, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                throw new UploadException((int)resp.StatusCode, null,
                    $"R2 PUT failed: HTTP {(int)resp.StatusCode}");
            }
        }
        catch (UploadException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new UploadException(0, null, $"R2 PUT network error: {ex.Message}", ex);
        }
    }

    private async Task<T> SendForDataAsync<T>(HttpRequestMessage req, string label, CancellationToken ct)
    {
        HttpResponseMessage resp;
        string body;
        try
        {
            resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new UploadException(0, null, $"{label} network error: {ex.Message}", ex);
        }

        if (!resp.IsSuccessStatusCode)
        {
            var code = TryReadErrorCode(body);
            throw new UploadException((int)resp.StatusCode, code,
                $"{label} failed: HTTP {(int)resp.StatusCode}{(code is null ? "" : $" ({code})")}");
        }

        Envelope<T>? env;
        try
        {
            env = JsonSerializer.Deserialize<Envelope<T>>(body, JsonRead);
        }
        catch (Exception ex)
        {
            throw new UploadException((int)resp.StatusCode, null, $"{label} returned unparseable JSON", ex);
        }

        if (env is null || env.Data is null)
            throw new UploadException((int)resp.StatusCode, null, $"{label} response missing data");

        return env.Data;
    }

    private static string? TryReadErrorCode(string body)
    {
        try
        {
            var env = JsonSerializer.Deserialize<Envelope<object>>(body, JsonRead);
            return env?.Error?.Code;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
    }

    private sealed record InitiateRequest(
        [property: JsonPropertyName("size_bytes")] long SizeBytes,
        [property: JsonPropertyName("mime")] string Mime,
        [property: JsonPropertyName("width")] int Width,
        [property: JsonPropertyName("height")] int Height,
        [property: JsonPropertyName("thumb_size_bytes")] long? ThumbSizeBytes,
        [property: JsonPropertyName("owner_token")] string? OwnerToken);

    private sealed record Envelope<T>(
        [property: JsonPropertyName("data")] T? Data,
        [property: JsonPropertyName("error")] ErrorBody? Error);

    private sealed record ErrorBody(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("message")] string Message);
}
