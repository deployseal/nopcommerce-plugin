using System.Text;

namespace DeploySeal.Nop.Core;

/// <summary>
/// Renders the contract's script tag (§1) byte-exactly:
/// <c>&lt;script src="{host}/ds-widget.js" data-ds-site-key="…" data-ds-environment="…" data-ds-build="…" async&gt;&lt;/script&gt;</c>.
/// Recommended attributes are omitted, never emitted empty.
/// </summary>
public static class SnippetBuilder
{
    /// <summary>Loader URL for a host: one trailing slash tolerated, default host when blank.</summary>
    public static string ScriptUrl(string? scriptHost)
    {
        var host = string.IsNullOrWhiteSpace(scriptHost) ? DeploySealContract.DefaultScriptHost : scriptHost.Trim();
        return host.TrimEnd('/') + "/" + DeploySealContract.ScriptFileName;
    }

    /// <summary>
    /// The tag, or an empty string when there is no site key (the widget cannot work without one,
    /// so nothing is rendered rather than a broken tag).
    /// </summary>
    public static string Build(string? scriptHost, string? siteKey, string? environment, string? build)
    {
        var key = siteKey?.Trim() ?? string.Empty;
        if (key.Length == 0)
            return string.Empty;

        var sb = new StringBuilder(160);
        sb.Append("<script src=\"").Append(Attr(ScriptUrl(scriptHost))).Append('"');
        sb.Append(" data-ds-site-key=\"").Append(Attr(key)).Append('"');

        var env = environment?.Trim();
        if (!string.IsNullOrEmpty(env))
            sb.Append(" data-ds-environment=\"").Append(Attr(env)).Append('"');

        var marker = build?.Trim();
        if (!string.IsNullOrEmpty(marker))
            sb.Append(" data-ds-build=\"").Append(Attr(marker)).Append('"');

        sb.Append(" async></script>");
        return sb.ToString();
    }

    /// <summary>Minimal attribute-value encoding (the values are validated slugs/keys, but never trust them).</summary>
    private static string Attr(string value) =>
        value.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");
}
