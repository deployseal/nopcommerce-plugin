using System.Text;

namespace DeploySeal.Nop.Core;

/// <summary>
/// Renders the contract's script tag (§1) byte-exactly:
/// <c>&lt;script src="{host}/ds-widget.js" data-ds-site-key="…" data-ds-environment="…" data-ds-build="…" data-ds-installer="nopcommerce-plugin/…" async&gt;&lt;/script&gt;</c>.
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

    /// <summary>The <c>data-ds-installer</c> value for a plugin version (§2): <c>nopcommerce-plugin/1.3.0</c>.</summary>
    public static string Installer(string pluginVersion)
    {
        var value = (DeploySealContract.InstallerPrefix + pluginVersion.Trim()).ToLowerInvariant();
        return value.Length <= DeploySealContract.InstallerMaxLength ? value : value.Substring(0, DeploySealContract.InstallerMaxLength);
    }

    /// <summary>
    /// The tag, or an empty string when there is no site key (the widget cannot work without one,
    /// so nothing is rendered rather than a broken tag). <paramref name="pluginVersion"/> stamps the
    /// tag with <c>data-ds-installer</c>; null omits the attribute (tests and previews only — every
    /// rendered tag carries it).
    /// </summary>
    public static string Build(string? scriptHost, string? siteKey, string? environment, string? build, string? pluginVersion = null)
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

        var version = pluginVersion?.Trim();
        if (!string.IsNullOrEmpty(version))
            sb.Append(" data-ds-installer=\"").Append(Attr(Installer(version))).Append('"');

        sb.Append(" async></script>");
        return sb.ToString();
    }

    /// <summary>Minimal attribute-value encoding (the values are validated slugs/keys, but never trust them).</summary>
    private static string Attr(string value) =>
        value.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");
}
