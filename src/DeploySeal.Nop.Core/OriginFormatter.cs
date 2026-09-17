namespace DeploySeal.Nop.Core;

/// <summary>
/// Turns a nopCommerce store's address into the exact <c>Origin</c> values a browser will send,
/// which is what the installer has to register as allowed origins (contract §3: exact match,
/// scheme://host[:port], no normalisation beyond lower-casing and one trailing slash).
/// </summary>
public static class OriginFormatter
{
    /// <summary>
    /// Formats one URL as an origin: lower-cased scheme and host, port kept only when it is not
    /// the scheme default, path/query/fragment and trailing slash dropped. Returns null when the
    /// value is not an absolute http(s) URL. A bare "host[:port]" is treated as http.
    /// </summary>
    public static string? FromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var candidate = url.Trim();
        if (!candidate.Contains("://", StringComparison.Ordinal))
            candidate = "http://" + candidate;

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
            return null;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return null;

        var host = uri.IdnHost.ToLowerInvariant();
        if (uri.HostNameType == UriHostNameType.IPv6)
            host = "[" + host.Trim('[', ']') + "]";

        var port = uri.IsDefaultPort ? string.Empty : ":" + uri.Port;
        return $"{uri.Scheme}://{host}{port}";
    }

    /// <summary>
    /// Every origin a store can answer on, in the order the installer should read them:
    /// the store URL first; its https twin when SSL is enabled but the URL is http (nopCommerce
    /// serves both in that case); then each entry of the store's HTTP_HOST list (comma separated)
    /// in the store URL's scheme (plus https when SSL is enabled). Distinct, with nulls dropped.
    /// </summary>
    public static IReadOnlyList<string> ForStore(string? storeUrl, bool sslEnabled, string? hosts)
    {
        var origins = new List<string>();

        void Add(string? origin)
        {
            if (origin is not null && !origins.Contains(origin, StringComparer.Ordinal))
                origins.Add(origin);
        }

        var primary = FromUrl(storeUrl);
        Add(primary);

        if (primary is not null && sslEnabled && primary.StartsWith("http://", StringComparison.Ordinal))
            Add("https://" + primary["http://".Length..]);

        if (!string.IsNullOrWhiteSpace(hosts))
        {
            var scheme = primary is not null && primary.StartsWith("https://", StringComparison.Ordinal) ? "https" : "http";
            foreach (var host in hosts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var value = host.Contains("://", StringComparison.Ordinal) ? host : $"{scheme}://{host}";
                Add(FromUrl(value));
                if (sslEnabled && scheme == "http" && !host.Contains("://", StringComparison.Ordinal))
                    Add(FromUrl("https://" + host));
            }
        }

        return origins;
    }
}
