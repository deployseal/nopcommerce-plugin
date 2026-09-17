using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DeploySeal.Nop.Core.Inventory;

/// <summary>
/// A platform inventory ready to send: the canonical item list, its fingerprint and the exact
/// request body for <c>POST /api/v1/sites/{publicKey}/inventory</c>.
/// </summary>
public sealed class InventorySnapshot
{
    public InventorySnapshot(string platform, string platformVersion, string? buildMarker, DateTime capturedAtUtc,
        IReadOnlyList<InventoryItem> items, string itemsJson, string sha256, int droppedCount)
    {
        Platform = platform;
        PlatformVersion = platformVersion;
        BuildMarker = buildMarker;
        CapturedAtUtc = capturedAtUtc;
        Items = items;
        ItemsJson = itemsJson;
        Sha256 = sha256;
        DroppedCount = droppedCount;
    }

    /// <summary>Lower-case platform identifier ("nopcommerce").</summary>
    public string Platform { get; }

    /// <summary>The platform version, e.g. "4.90.8".</summary>
    public string PlatformVersion { get; }

    /// <summary>The build marker the plugin resolved, or null when none is emitted.</summary>
    public string? BuildMarker { get; }

    /// <summary>When the platform was read (UTC).</summary>
    public DateTime CapturedAtUtc { get; }

    /// <summary>The canonical item list: cleaned, de-duplicated, sorted, capped.</summary>
    public IReadOnlyList<InventoryItem> Items { get; }

    /// <summary>Canonical items JSON (what the server fingerprints).</summary>
    public string ItemsJson { get; }

    /// <summary>Lower-case hex SHA-256 of the UTF-8 bytes of <see cref="ItemsJson"/>.</summary>
    public string Sha256 { get; }

    /// <summary>Items dropped to stay under the contract's 500-item cap (or for having no system name).</summary>
    public int DroppedCount { get; }

    /// <summary>The exact JSON body to POST.</summary>
    public string ToRequestJson() => InventorySnapshotBuilder.RequestJson(this);
}

/// <summary>
/// Builds the inventory snapshot in the contract's canonical form (§6): items sorted by
/// systemName then version (ordinal), keys in the order systemName, name, version, enabled,
/// compact JSON with minimal escaping, SHA-256 over the UTF-8 bytes. The server canonicalises
/// what it receives and hashes that itself, so this fingerprint is informational for the
/// installer (shown on the Configure page) and for tests; it does not have to match byte for
/// byte for the request to be accepted.
/// </summary>
public static class InventorySnapshotBuilder
{
    /// <summary>The platform identifier every nopCommerce install reports.</summary>
    public const string Platform = "nopcommerce";

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false,
    };

    public static InventorySnapshot Build(string? platformVersion, string? buildMarker, DateTime capturedAtUtc, IEnumerable<InventoryItem> items)
    {
        var raw = items.ToList();
        var canonical = Canonicalize(raw);
        var itemsJson = ItemsJson(canonical);
        var version = Clamp(BuildMarkerResolver.Sanitize(platformVersion), DeploySealContract.PlatformVersionMaxLength);
        if (version.Length == 0)
            version = "unknown";
        var marker = BuildMarkerResolver.Clamp(BuildMarkerResolver.Sanitize(buildMarker));

        return new InventorySnapshot(
            Platform,
            version,
            marker.Length == 0 ? null : marker,
            capturedAtUtc.Kind == DateTimeKind.Utc ? capturedAtUtc : capturedAtUtc.ToUniversalTime(),
            canonical,
            itemsJson,
            Sha256Hex(itemsJson),
            raw.Count - canonical.Count);
    }

    /// <summary>
    /// Trims and clamps every field to the contract's limits, drops items without a system
    /// name, keeps the first of any duplicate system name, sorts by systemName then version
    /// (ordinal) and caps the list at <see cref="DeploySealContract.InventoryMaxItems"/>.
    /// </summary>
    public static IReadOnlyList<InventoryItem> Canonicalize(IEnumerable<InventoryItem> items)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var cleaned = new List<InventoryItem>();
        foreach (var item in items)
        {
            if (item is null)
                continue;

            var systemName = Clamp(item.SystemName?.Trim() ?? string.Empty, DeploySealContract.InventorySystemNameMaxLength);
            if (systemName.Length == 0 || !seen.Add(systemName))
                continue;

            var name = Clamp(item.Name?.Trim() ?? string.Empty, DeploySealContract.InventoryNameMaxLength);
            if (name.Length == 0)
                name = systemName;

            var version = Clamp(item.Version?.Trim() ?? string.Empty, DeploySealContract.InventoryVersionMaxLength);
            if (version.Length == 0)
                version = "0";

            cleaned.Add(new InventoryItem(systemName, name, version, item.Enabled));
        }

        return cleaned
            .OrderBy(i => i.SystemName, StringComparer.Ordinal)
            .ThenBy(i => i.Version, StringComparer.Ordinal)
            .Take(DeploySealContract.InventoryMaxItems)
            .ToList();
    }

    /// <summary>Canonical items JSON of an already canonical list.</summary>
    public static string ItemsJson(IReadOnlyList<InventoryItem> canonical)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            WriteItems(writer, canonical);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>The POST body: platform, platformVersion, buildMarker, capturedAt, items.</summary>
    public static string RequestJson(InventorySnapshot snapshot)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("platform", snapshot.Platform);
            writer.WriteString("platformVersion", snapshot.PlatformVersion);
            if (snapshot.BuildMarker is null)
                writer.WriteNull("buildMarker");
            else
                writer.WriteString("buildMarker", snapshot.BuildMarker);
            writer.WriteString("capturedAt", FormatUtc(snapshot.CapturedAtUtc));
            writer.WritePropertyName("items");
            WriteItems(writer, snapshot.Items);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static string Sha256Hex(string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
            sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    /// <summary>ISO 8601 UTC with millisecond precision and a Z suffix.</summary>
    public static string FormatUtc(DateTime utc) =>
        utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static void WriteItems(Utf8JsonWriter writer, IReadOnlyList<InventoryItem> items)
    {
        writer.WriteStartArray();
        foreach (var item in items)
        {
            writer.WriteStartObject();
            writer.WriteString("systemName", item.SystemName);
            writer.WriteString("name", item.Name);
            writer.WriteString("version", item.Version);
            writer.WriteBoolean("enabled", item.Enabled);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static string Clamp(string value, int max) => value.Length <= max ? value : value[..max];
}
