using DeploySeal.Nop.Core;
using Nop.Core.Domain.Cms;
using Nop.Plugin.Widgets.DeploySeal.Components;
using Nop.Services.Cms;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Plugins;
using Nop.Services.Stores;
using Nop.Web.Framework.Infrastructure;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.Widgets.DeploySeal;

/// <summary>
/// DeploySeal widget plugin: renders the contract script tag in the storefront head.
/// </summary>
public class DeploySealPlugin : BasePlugin, IWidgetPlugin
{
    private readonly DeploySealSettings _deploySealSettings;
    private readonly ILocalizationService _localizationService;
    private readonly INopUrlHelper _nopUrlHelper;
    private readonly ISettingService _settingService;
    private readonly IStoreService _storeService;
    private readonly WidgetSettings _widgetSettings;

    public DeploySealPlugin(DeploySealSettings deploySealSettings,
        ILocalizationService localizationService,
        INopUrlHelper nopUrlHelper,
        ISettingService settingService,
        IStoreService storeService,
        WidgetSettings widgetSettings)
    {
        _deploySealSettings = deploySealSettings;
        _localizationService = localizationService;
        _nopUrlHelper = nopUrlHelper;
        _settingService = settingService;
        _storeService = storeService;
        _widgetSettings = widgetSettings;
    }

    /// <summary>
    /// The storefront head, always. The admin layout has no head zone, so opting in to admin
    /// rendering uses the first zone of the admin body instead.
    /// </summary>
    public Task<IList<string>> GetWidgetZonesAsync()
    {
        IList<string> zones = new List<string> { PublicWidgetZones.HeadHtmlTag };
        if (_deploySealSettings.RenderOnAdmin)
            zones.Add(AdminWidgetZones.HeaderBefore);

        return Task.FromResult(zones);
    }

    public override string GetConfigurationPageUrl()
    {
        return _nopUrlHelper.RouteUrl(DeploySealDefaults.ConfigurationRouteName);
    }

    public Type GetWidgetViewComponent(string widgetZone)
    {
        ArgumentNullException.ThrowIfNull(widgetZone);

        if (widgetZone.Equals(PublicWidgetZones.HeadHtmlTag) || widgetZone.Equals(AdminWidgetZones.HeaderBefore))
            return typeof(WidgetsDeploySealViewComponent);

        return null;
    }

    public override async Task InstallAsync()
    {
        // Contract §2: a sensible declared label out of the box, guessed from the default store's host.
        var stores = await _storeService.GetAllStoresAsync();
        var settings = new DeploySealSettings
        {
            Enabled = false,
            SiteKey = string.Empty,
            EnvironmentLabel = EnvironmentLabel.DeriveFromStoreUrl(stores.FirstOrDefault()?.Url),
            // Contract §4 rule 4: version plus SHA when one can be read, else version. The resolver
            // degrades at render time, so the default source is the "plus SHA" one.
            BuildMarkerSource = BuildMarkerSource.NopVersionPlusGitSha,
            GitShaFilePath = string.Empty,
            ManualBuildMarker = string.Empty,
            ScriptHost = DeploySealContract.DefaultScriptHost,
            RenderOnAdmin = false,
        };
        await _settingService.SaveSettingAsync(settings);

        if (!_widgetSettings.ActiveWidgetSystemNames.Contains(DeploySealDefaults.SystemName))
        {
            _widgetSettings.ActiveWidgetSystemNames.Add(DeploySealDefaults.SystemName);
            await _settingService.SaveSettingAsync(_widgetSettings);
        }

        var p = DeploySealDefaults.LocalePrefix;
        await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>
        {
            [$"{p}.Instructions"] =
                "<p>DeploySeal loads a small tester widget on your storefront so testers can pin issues on the page and your readiness report can prove which environment and build were tested.<br /><br />" +
                "Follow these steps:<br /><ol>" +
                "<li>In DeploySeal, open your site and <b>create an environment</b> for this store (for example <i>Staging</i> or <i>Production</i>).</li>" +
                "<li>Copy the environment's <b>public key</b> (it starts with <code>ls_</code>) into the <b>Site key</b> box below. Use one key per environment per store, never one key for everything.</li>" +
                "<li>In that environment, <b>register the exact origin(s)</b> listed under \"Origins to register\" below. Matching is exact: scheme, host and port must all agree.</li>" +
                "<li>Tick <b>Enabled</b>, click <b>Save</b>, then open the storefront and confirm the environment shows as <i>Live</i> in DeploySeal.</li>" +
                "</ol><a href=\"" + DeploySealContract.DocsUrl + "\" target=\"_blank\" rel=\"noopener\">Widget documentation</a></p>",

            [$"{p}.Enabled"] = "Enabled",
            [$"{p}.Enabled.Hint"] = "Render the DeploySeal widget tag on the storefront. Nothing is emitted while this is off.",
            [$"{p}.SiteKey"] = "Site key",
            [$"{p}.SiteKey.Hint"] = "The public key of the DeploySeal environment this store is. It is not a secret: it only works from the environment's registered origins.",
            [$"{p}.SiteKey.FormatWarning"] = "DeploySeal keys start with \"ls_\" (legacy keys with \"ds_\"). Check that you copied the environment's public key, not something else.",
            [$"{p}.EnvironmentLabel"] = "Environment label",
            [$"{p}.EnvironmentLabel.Hint"] = "The label this store declares itself as (lower-case letters, digits and hyphens, up to 32 characters). It is a declaration, not an identifier: the site key decides where evidence lands. Leave empty to use the value guessed from the store URL.",
            [$"{p}.BuildMarkerSource"] = "Build marker source",
            [$"{p}.BuildMarkerSource.Hint"] = "Where the \"which code is running\" marker comes from. Prefer a git SHA when your deploy can write one.",
            [$"{p}.BuildMarkerSource.NopVersion"] = "nopCommerce version (e.g. 4.90.8)",
            [$"{p}.BuildMarkerSource.NopVersionPlusGitSha"] = "nopCommerce version + git SHA when available (e.g. 4.90.8+a1b2c3d)",
            [$"{p}.BuildMarkerSource.GitSha"] = "Git SHA only (from the file below)",
            [$"{p}.BuildMarkerSource.Manual"] = "Manual (typed below)",
            [$"{p}.GitShaFilePath"] = "Git SHA file path",
            [$"{p}.GitShaFilePath.Hint"] = "A file that holds the deployed commit SHA (7–40 hex characters on its first line), for example written by your deploy pipeline. Relative paths are resolved from the application root; \"App_Data/build-sha.txt\" is a good place.",
            [$"{p}.ManualBuildMarker"] = "Manual build marker",
            [$"{p}.ManualBuildMarker.Hint"] = "Used when the source is \"Manual\". One token, no spaces, up to 64 characters. Change it on every deployment.",
            [$"{p}.ScriptHost"] = "Script host (advanced)",
            [$"{p}.ScriptHost.Hint"] = "Origin the widget loader is fetched from. Leave at https://cdn.deployseal.com unless DeploySeal support tells you otherwise.",
            [$"{p}.RenderOnAdmin"] = "Also load in the admin area",
            [$"{p}.RenderOnAdmin.Hint"] = "Off by default. Turn on only if testers need to pin issues inside the administration area too.",

            [$"{p}.Facts.Title"] = "What this store will send",
            [$"{p}.Facts.Intro"] = "Compare these values with the environment in DeploySeal. Origins must match exactly; the build marker must be copied verbatim into the campaign's release identifier.",
            [$"{p}.Facts.WidgetInactive"] = "This widget is installed but not active in nopCommerce. Enable it under Configuration → Widgets, or nothing will render.",
            [$"{p}.Facts.Origins"] = "Origins to register",
            [$"{p}.Facts.Origins.Hint"] = "Taken from each store's URL, SSL setting and HTTP_HOST list under Configuration → Stores. If shoppers reach the store on another scheme, host or port, register that too.",
            [$"{p}.Facts.Origins.Store"] = "Store",
            [$"{p}.Facts.Origins.InScope"] = "in scope",
            [$"{p}.Facts.Origins.None"] = "The store URL is not an absolute http(s) URL, so no origin could be derived. Fix it under Configuration → Stores.",
            [$"{p}.Facts.EnvironmentLabel"] = "Declared environment label",
            [$"{p}.Facts.EnvironmentLabel.Derived"] = "Guessed from the store URL",
            [$"{p}.Facts.BuildMarker"] = "Build marker emitted right now",
            [$"{p}.Facts.BuildMarker.None"] = "(none — the data-ds-build attribute will be omitted)",
            [$"{p}.Facts.BuildMarker.Source"] = "Source",
            [$"{p}.Facts.GitSha.Resolved"] = "SHA file",
            [$"{p}.Facts.GitSha.Found"] = "found and readable",
            [$"{p}.Facts.GitSha.Missing"] = "missing or not a valid SHA",
            [$"{p}.Facts.Versions"] = "Versions",
            [$"{p}.Facts.Snippet"] = "Tag that will be rendered",
            [$"{p}.Facts.Snippet.None"] = "Nothing will be rendered until a site key is entered.",
            [$"{p}.Facts.Snippet.Disabled"] = "The plugin is not enabled, so this tag is not rendered yet.",
            [$"{p}.Facts.Links"] = "Open DeploySeal to see this environment's Live / Stale / Not seen state",
            [$"{p}.Facts.Docs"] = "Widget documentation",
        });

        await base.InstallAsync();
    }

    public override async Task UninstallAsync()
    {
        if (_widgetSettings.ActiveWidgetSystemNames.Contains(DeploySealDefaults.SystemName))
        {
            _widgetSettings.ActiveWidgetSystemNames.Remove(DeploySealDefaults.SystemName);
            await _settingService.SaveSettingAsync(_widgetSettings);
        }

        await _settingService.DeleteSettingAsync<DeploySealSettings>();
        await _localizationService.DeleteLocaleResourcesAsync(DeploySealDefaults.LocalePrefix);

        await base.UninstallAsync();
    }

    /// <summary>Show it in Configuration → Widgets like any other widget.</summary>
    public bool HideInWidgetList => false;
}
