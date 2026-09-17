using System.Globalization;
using System.Text;

namespace DeploySeal.Nop.Core;

/// <summary>
/// The declared environment label (contract §2): lower-case, [a-z0-9-], ≤ 32 characters.
/// </summary>
public static class EnvironmentLabel
{
    /// <summary>Host-name fragments that mark a non-production store.</summary>
    private static readonly string[] NonProductionHints = { "staging", "uat", "test", "dev", "qa", "sandbox", "preprod", "localhost" };

    /// <summary>
    /// Normalises any text into a contract-valid label. Letters are lower-cased and diacritics
    /// stripped; every other run of characters becomes a single hyphen; leading/trailing hyphens
    /// go; the result is cut at 32 characters (and re-trimmed so it never ends on a hyphen).
    /// Returns an empty string when nothing survives.
    /// </summary>
    public static string Slugify(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        var pendingHyphen = false;

        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;

            var isAllowed = ch is >= 'a' and <= 'z' || ch is >= '0' and <= '9';
            if (isAllowed)
            {
                if (pendingHyphen && sb.Length > 0)
                    sb.Append('-');
                pendingHyphen = false;
                sb.Append(ch);
            }
            else
            {
                pendingHyphen = true;
            }
        }

        var slug = sb.ToString();
        if (slug.Length > DeploySealContract.EnvironmentLabelMaxLength)
            slug = slug[..DeploySealContract.EnvironmentLabelMaxLength].TrimEnd('-');

        return slug;
    }

    /// <summary>True when the value is already a valid label (what <see cref="Slugify"/> would return unchanged).</summary>
    public static bool IsValid(string? value) =>
        !string.IsNullOrEmpty(value) && Slugify(value) == value;

    /// <summary>
    /// Default label for a store, derived from its URL host: "staging" when the host carries a
    /// non-production hint (staging, uat, test, dev, qa, sandbox, preprod, localhost), else
    /// "production". A plugin can only guess here; the installer is shown the value and can change it.
    /// </summary>
    public static string DeriveFromStoreUrl(string? storeUrl)
    {
        var host = TryGetHost(storeUrl);
        if (host is null)
            return "production";

        return NonProductionHints.Any(hint => host.Contains(hint, StringComparison.OrdinalIgnoreCase))
            ? "staging"
            : "production";
    }

    private static string? TryGetHost(string? storeUrl)
    {
        if (string.IsNullOrWhiteSpace(storeUrl))
            return null;

        var candidate = storeUrl.Trim();
        if (!candidate.Contains("://", StringComparison.Ordinal))
            candidate = "http://" + candidate;

        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ? uri.Host : null;
    }
}
