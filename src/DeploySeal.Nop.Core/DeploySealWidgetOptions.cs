namespace DeploySeal.Nop.Core;

/// <summary>
/// The plugin's settings, as plain values. The nopCommerce settings class derives from this and
/// adds the ISettings marker; nopCommerce persists inherited public properties, so every field
/// here is overridable per store.
/// </summary>
public class DeploySealWidgetOptions
{
    /// <summary>Master switch. Nothing renders while false.</summary>
    public bool Enabled { get; set; }

    /// <summary>The environment's public key ("ls_…"). Per store: one key per environment per store (§3).</summary>
    public string? SiteKey { get; set; }

    /// <summary>Declared environment label (§2), slugged to [a-z0-9-] ≤ 32.</summary>
    public string? EnvironmentLabel { get; set; }

    /// <summary>Stored as an int by nopCommerce's setting service. See <see cref="BuildMarkerSource"/>.</summary>
    public int BuildMarkerSourceId { get; set; } = (int)Core.BuildMarkerSource.NopVersionPlusGitSha;

    /// <summary>Path to a file holding the deployed commit SHA, relative to the app root (or absolute).</summary>
    public string? GitShaFilePath { get; set; }

    /// <summary>Used when <see cref="Core.BuildMarkerSource.Manual"/> is selected.</summary>
    public string? ManualBuildMarker { get; set; }

    /// <summary>Advanced. Origin the loader is fetched from; the contract allows exactly one.</summary>
    public string? ScriptHost { get; set; } = DeploySealContract.DefaultScriptHost;

    /// <summary>Also load the widget inside the admin area. Off by default.</summary>
    public bool RenderOnAdmin { get; set; }

    /// <summary>
    /// Organisation API key with the Write scope (§6), used server to server for the inventory
    /// only. A secret: the Configure page never renders it back, and leaving the box empty on
    /// Save keeps the stored value.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Advanced. The DeploySeal API host; defaults to <see cref="DeploySealContract.DefaultApiBase"/>.</summary>
    public string? ApiBase { get; set; } = DeploySealContract.DefaultApiBase;

    /// <summary>Post the plugin inventory on the schedule (every 6 h by default). Off by default; "Send now" works regardless.</summary>
    public bool SendInventory { get; set; }

    /// <summary>Typed view of <see cref="BuildMarkerSourceId"/>; unknown ids read as the default.</summary>
    public BuildMarkerSource BuildMarkerSource
    {
        get => Enum.IsDefined(typeof(BuildMarkerSource), BuildMarkerSourceId)
            ? (BuildMarkerSource)BuildMarkerSourceId
            : Core.BuildMarkerSource.NopVersionPlusGitSha;
        set => BuildMarkerSourceId = (int)value;
    }
}
