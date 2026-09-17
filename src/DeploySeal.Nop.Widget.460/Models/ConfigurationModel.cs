using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Plugin.Widgets.DeploySeal.Models;

/// <summary>One store's exact origins, as the installer must register them.</summary>
public record StoreOriginsModel
{
    public int StoreId { get; set; }
    public string StoreName { get; set; } = string.Empty;
    public string StoreUrl { get; set; } = string.Empty;
    public bool SslEnabled { get; set; }
    public bool IsInScope { get; set; }
    public IList<string> Origins { get; set; } = new List<string>();
}

public record ConfigurationModel : BaseNopModel
{
    public int ActiveStoreScopeConfiguration { get; set; }

    #region Settings

    [NopResourceDisplayName("Plugins.Widgets.DeploySeal.Enabled")]
    public bool Enabled { get; set; }
    public bool Enabled_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Widgets.DeploySeal.SiteKey")]
    public string? SiteKey { get; set; }
    public bool SiteKey_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Widgets.DeploySeal.EnvironmentLabel")]
    public string? EnvironmentLabel { get; set; }
    public bool EnvironmentLabel_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Widgets.DeploySeal.BuildMarkerSource")]
    public int BuildMarkerSourceId { get; set; }
    public bool BuildMarkerSourceId_OverrideForStore { get; set; }
    public IList<SelectListItem> AvailableBuildMarkerSources { get; set; } = new List<SelectListItem>();

    [NopResourceDisplayName("Plugins.Widgets.DeploySeal.GitShaFilePath")]
    public string? GitShaFilePath { get; set; }
    public bool GitShaFilePath_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Widgets.DeploySeal.ManualBuildMarker")]
    public string? ManualBuildMarker { get; set; }
    public bool ManualBuildMarker_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Widgets.DeploySeal.ScriptHost")]
    public string? ScriptHost { get; set; }
    public bool ScriptHost_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Widgets.DeploySeal.RenderOnAdmin")]
    public bool RenderOnAdmin { get; set; }
    public bool RenderOnAdmin_OverrideForStore { get; set; }

    /// <summary>Write-only: never populated from the stored value. Empty on Save = keep the stored key.</summary>
    [NopResourceDisplayName("Plugins.Widgets.DeploySeal.ApiKey")]
    public string? ApiKey { get; set; }
    public bool ApiKey_OverrideForStore { get; set; }

    /// <summary>A key is stored for this scope (the page shows a mask and the "remove" box).</summary>
    public bool ApiKeyIsSet { get; set; }

    /// <summary>Tick to delete the stored key on Save.</summary>
    [NopResourceDisplayName("Plugins.Widgets.DeploySeal.ApiKey.Clear")]
    public bool ClearApiKey { get; set; }

    [NopResourceDisplayName("Plugins.Widgets.DeploySeal.ApiBase")]
    public string? ApiBase { get; set; }
    public bool ApiBase_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Widgets.DeploySeal.SendInventory")]
    public bool SendInventory { get; set; }
    public bool SendInventory_OverrideForStore { get; set; }

    #endregion

    #region Inventory facts (contract §6)

    /// <summary>How many plugins the next send would report (installed and not installed).</summary>
    public int InventoryItemCount { get; set; }

    /// <summary>How many of those are installed.</summary>
    public int InventoryInstalledCount { get; set; }

    /// <summary>Fingerprint of the current canonical list, for comparison with the report.</summary>
    public string InventorySha256 { get; set; } = string.Empty;

    /// <summary>Exactly where the snapshot will be posted.</summary>
    public string InventoryEndpoint { get; set; } = string.Empty;

    /// <summary>Site key and API key are both present, so "Send now" can work.</summary>
    public bool CanSendInventory { get; set; }

    #endregion

    #region What the installer must see (contract §7)

    /// <summary>The widget is in WidgetSettings.ActiveWidgetSystemNames (nopCommerce's own on/off for widgets).</summary>
    public bool IsWidgetActive { get; set; }

    public IList<StoreOriginsModel> Stores { get; set; } = new List<StoreOriginsModel>();

    /// <summary>The label that will actually be declared (slugged), or the derived default when the setting is empty.</summary>
    public string EffectiveEnvironmentLabel { get; set; } = string.Empty;

    /// <summary>What the plugin would use if the label were left empty.</summary>
    public string DerivedEnvironmentLabel { get; set; } = string.Empty;

    public string BuildMarker { get; set; } = string.Empty;
    public string BuildMarkerSourceDescription { get; set; } = string.Empty;
    public string BuildMarkerWarning { get; set; }
    public string GitShaResolvedPath { get; set; }
    public bool GitShaFound { get; set; }
    public string NopVersion { get; set; } = string.Empty;
    public string PluginVersion { get; set; } = string.Empty;

    /// <summary>Exactly what the storefront will emit right now, for these settings.</summary>
    public string SnippetPreview { get; set; } = string.Empty;

    public string SiteKeyWarning { get; set; }
    public string DocsUrl { get; set; } = string.Empty;
    public string AppUrl { get; set; } = string.Empty;

    #endregion
}
