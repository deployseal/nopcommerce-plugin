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
