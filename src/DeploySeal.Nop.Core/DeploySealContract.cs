namespace DeploySeal.Nop.Core;

/// <summary>
/// Constants fixed by docs/INSTALL_CONTRACT.md (v1, 2026-09-17). Every install path
/// shares these numbers; change them there first.
/// </summary>
public static class DeploySealContract
{
    /// <summary>The only supported script host (§1).</summary>
    public const string DefaultScriptHost = "https://cdn.deployseal.com";

    /// <summary>Loader file name beside the ES module (§1).</summary>
    public const string ScriptFileName = "ds-widget.js";

    /// <summary>Environment label: ≤ 32 chars, [a-z0-9-] (§2).</summary>
    public const int EnvironmentLabelMaxLength = 32;

    /// <summary>Build marker: single token, no whitespace, ≤ 64 chars (§2, §4).</summary>
    public const int BuildMarkerMaxLength = 64;

    /// <summary>Shortest SHA the report will match as a prefix (§4).</summary>
    public const int MinShaLength = 7;

    /// <summary>Length of the short SHA appended to a version marker ("4.90.8+a1b2c3d").</summary>
    public const int ShortShaLength = 7;

    /// <summary>Public docs for the widget install.</summary>
    public const string DocsUrl = "https://deployseal.com/docs/widget";

    /// <summary>Where the installer creates environments and reads the Live / Stale / Not seen pill (§7.5).</summary>
    public const string AppUrl = "https://deployseal.com";
}
