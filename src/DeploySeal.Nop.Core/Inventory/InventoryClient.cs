using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace DeploySeal.Nop.Core.Inventory;

/// <summary>What one send attempt came back with. Never an exception: the plugin renders this.</summary>
public sealed class InventorySendResult
{
    /// <summary>True when the API accepted the snapshot (201 stored, or 200 already on record).</summary>
    public bool Ok { get; init; }

    /// <summary>True on 201 (a new observation); false on 200 (the same snapshot was already on record).</summary>
    public bool Created { get; init; }

    /// <summary>HTTP status when a response arrived; null when the request never completed.</summary>
    public int? StatusCode { get; init; }

    /// <summary>The snapshot id the API answered with.</summary>
    public string? SnapshotId { get; init; }

    public int? ItemCount { get; init; }

    public DateTime? CapturedAt { get; init; }

    /// <summary>What went wrong, in one line fit for the Configure page and the nop log.</summary>
    public string? Error { get; init; }

    /// <summary>The URL that was (or would have been) called.</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>Number of HTTP attempts made (0 when the inputs were incomplete, at most 2).</summary>
    public int Attempts { get; init; }

    public static InventorySendResult Failure(string url, string error, int? statusCode = null, int attempts = 0) => new()
    {
        Ok = false,
        Url = url,
        Error = error,
        StatusCode = statusCode,
        Attempts = attempts,
    };
}

/// <summary>
/// Sends an inventory snapshot to <c>POST {ApiBase}/api/v1/sites/{publicKey}/inventory</c> with
/// <c>Authorization: Bearer &lt;apiKey&gt;</c> (install contract §6). Server to server, never
/// from the browser. One retry on a transport failure (connection refused, timeout), none on an
/// HTTP error; nothing here ever throws into the request pipeline or the task runner.
/// The HttpClient's timeout is the caller's (the plugin registers 15 s).
/// </summary>
public sealed class InventoryClient
{
    /// <summary>Pause before the single retry.</summary>
    public static TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(750);

    private readonly HttpClient _http;

    public InventoryClient(HttpClient http)
    {
        _http = http;
    }

    /// <summary>{apiBase without trailing slash}/api/v1/sites/{publicKey}/inventory; the default host when the base is blank.</summary>
    public static string EndpointUrl(string? apiBase, string? publicKey)
    {
        var host = string.IsNullOrWhiteSpace(apiBase) ? DeploySealContract.DefaultApiBase : apiBase.Trim().TrimEnd('/');
        var key = publicKey?.Trim() ?? string.Empty;
        return host + "/api/v1/sites/" + Uri.EscapeDataString(key) + "/inventory";
    }

    public async Task<InventorySendResult> SendAsync(string? apiBase, string? apiKey, string? publicKey, InventorySnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var url = EndpointUrl(apiBase, publicKey);
        var key = apiKey?.Trim() ?? string.Empty;
        var site = publicKey?.Trim() ?? string.Empty;

        if (site.Length == 0)
            return InventorySendResult.Failure(url, "No site key is configured, so there is no environment to report the inventory for.");
        if (key.Length == 0)
            return InventorySendResult.Failure(url, "No DeploySeal API key is configured. Create one with the Write scope under Settings → Integrations → API keys and paste it into the plugin settings.");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return InventorySendResult.Failure(url, "The API base is not an absolute http(s) URL.");

        var body = snapshot.ToRequestJson();
        var attempts = 0;
        while (true)
        {
            attempts++;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, uri);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");

                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
                var text = response.Content is null ? string.Empty : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return Interpret(url, response.StatusCode, text, attempts);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return InventorySendResult.Failure(url, "The request was cancelled.", null, attempts);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is OperationCanceledException || ex is IOException)
            {
                // Transport failure (refused, DNS, TLS, timeout): one more try, then report.
                if (attempts >= 2)
                    return InventorySendResult.Failure(url, "Could not reach " + url + ": " + Describe(ex), null, attempts);

                try
                {
                    await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return InventorySendResult.Failure(url, "The request was cancelled.", null, attempts);
                }
            }
            catch (Exception ex)
            {
                return InventorySendResult.Failure(url, "Unexpected error while sending the inventory: " + Describe(ex), null, attempts);
            }
        }
    }

    /// <summary>Turns the API's answer into a result: 201/200 with the body's id/capturedAt/itemCount, anything else into one error line.</summary>
    public static InventorySendResult Interpret(string url, HttpStatusCode status, string body, int attempts)
    {
        var code = (int)status;
        if (code == 201 || code == 200)
        {
            string? id = null;
            int? count = null;
            DateTime? capturedAt = null;
            try
            {
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                        id = idEl.GetString();
                    if (root.TryGetProperty("itemCount", out var countEl) && countEl.ValueKind == JsonValueKind.Number && countEl.TryGetInt32(out var n))
                        count = n;
                    if (root.TryGetProperty("capturedAt", out var capEl) && capEl.ValueKind == JsonValueKind.String && capEl.TryGetDateTime(out var dt))
                        capturedAt = dt.ToUniversalTime();
                }
            }
            catch (JsonException)
            {
                // A 2xx with an unreadable body is still an acceptance; the details just stay null.
            }

            return new InventorySendResult
            {
                Ok = true,
                Created = code == 201,
                StatusCode = code,
                SnapshotId = id,
                ItemCount = count,
                CapturedAt = capturedAt,
                Url = url,
                Attempts = attempts,
            };
        }

        return InventorySendResult.Failure(url, DescribeProblem(code, body), code, attempts);
    }

    private static string DescribeProblem(int code, string body)
    {
        var prefix = "HTTP " + code.ToString(CultureInfo.InvariantCulture);
        switch (code)
        {
            case 401:
                return prefix + ": the API key was not accepted (unknown or revoked). Paste a current key.";
            case 403:
                return prefix + ": the API key does not carry the Write scope this call needs.";
            case 404:
                return prefix + ": no environment of the key's organisation has this site key. Check that the site key and the API key belong to the same DeploySeal organisation.";
            case 429:
                return prefix + ": rate limited; the scheduled task will try again later.";
        }

        var detail = ProblemDetail(body);
        return detail is null ? prefix + "." : prefix + ": " + detail;
    }

    /// <summary>First useful line of an application/problem+json body: detail (or title) plus the first field error.</summary>
    private static string? ProblemDetail(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Truncate(body);

            string? text = null;
            if (root.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
                text = detail.GetString();
            else if (root.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
                text = title.GetString();

            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                foreach (var field in errors.EnumerateObject())
                {
                    var first = field.Value.ValueKind == JsonValueKind.Array && field.Value.GetArrayLength() > 0
                        ? field.Value[0].GetString()
                        : field.Value.ToString();
                    text = (text is null ? string.Empty : text + " ") + field.Name + ": " + first;
                    break;
                }
            }

            return text is null ? Truncate(body) : Truncate(text);
        }
        catch (JsonException)
        {
            return Truncate(body);
        }
    }

    private static string Describe(Exception ex)
    {
        var inner = ex;
        while (inner.InnerException is not null)
            inner = inner.InnerException;
        return ex is TaskCanceledException ? "timed out" : inner.Message;
    }

    private static string Truncate(string text) => text.Length <= 300 ? text : text[..300] + "…";
}
