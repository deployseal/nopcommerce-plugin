using DeploySeal.Nop.Core;
using Nop.Core.Domain.ScheduleTasks;
using Nop.Core;
using Nop.Core.Domain.Cms;
using Nop.Plugin.Widgets.DeploySeal.Components;
using Nop.Services.Cms;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Plugins;
using Nop.Services.ScheduleTasks;
using Nop.Services.Stores;
using Nop.Web.Framework.Infrastructure;

namespace Nop.Plugin.Widgets.DeploySeal;

/// <summary>
/// DeploySeal widget plugin: renders the contract script tag in the storefront head.
/// </summary>
public class DeploySealPlugin : BasePlugin, IWidgetPlugin
{
    private readonly DeploySealSettings _deploySealSettings;
    private readonly ILocalizationService _localizationService;
    private readonly IScheduleTaskService _scheduleTaskService;
    private readonly ISettingService _settingService;
    private readonly IStoreService _storeService;
    private readonly IWebHelper _webHelper;
    private readonly WidgetSettings _widgetSettings;

    public DeploySealPlugin(DeploySealSettings deploySealSettings,
        ILocalizationService localizationService,
        IScheduleTaskService scheduleTaskService,
        ISettingService settingService,
        IStoreService storeService,
        IWebHelper webHelper,
        WidgetSettings widgetSettings)
    {
        _deploySealSettings = deploySealSettings;
        _localizationService = localizationService;
        _scheduleTaskService = scheduleTaskService;
        _settingService = settingService;
        _storeService = storeService;
        _webHelper = webHelper;
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

    /// <summary>
    /// 4.60 has no INopUrlHelper / named plugin routes; like its Google Analytics plugin, the URL
    /// is the store location plus the default admin-area route to the controller action.
    /// </summary>
    public override string GetConfigurationPageUrl()
    {
        return _webHelper.GetStoreLocation() + DeploySealDefaults.ConfigurationRoutePattern;
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
            ApiKey = string.Empty,
            ApiBase = DeploySealContract.DefaultApiBase,
            SendInventory = false,
        };
        await _settingService.SaveSettingAsync(settings);

        await EnsureInventoryTaskAsync();

        if (!_widgetSettings.ActiveWidgetSystemNames.Contains(DeploySealDefaults.SystemName))
        {
            _widgetSettings.ActiveWidgetSystemNames.Add(DeploySealDefaults.SystemName);
            await _settingService.SaveSettingAsync(_widgetSettings);
        }

        var p = DeploySealDefaults.LocalePrefix;
        await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>
        {
            [$"{p}.Instructions"] =
                "<p>DeploySeal loads a small tester widget on your storefront so testers can pin issues on the page and your readiness report can prove which environment and build were tested. Only two settings below are required — <b>Site key</b> and <b>Enabled</b> — everything else already has a sensible default.<br /><br />" +
                "<ol>" +
                "<li>In DeploySeal, create an <b>environment</b> for this store and register the origin(s) shown below.</li>" +
                "<li><b>Paste</b> the environment's public key into <b>Site key</b>.</li>" +
                "<li>Tick <b>Enabled</b> and <b>Save</b>.</li>" +
                "<li><b>Confirm</b>: open the storefront, then check the environment shows as <i>Live</i> in DeploySeal.</li>" +
                "</ol><a href=\"" + DeploySealContract.DocsUrl + "\" target=\"_blank\" rel=\"noopener\">Widget documentation</a></p>",

            [$"{p}.Essentials.Title"] = "Essentials",
            [$"{p}.Advanced.Title"] = "Advanced",

            [$"{p}.Enabled"] = "Enabled",
            [$"{p}.Enabled.Hint"] = "Render the DeploySeal widget tag on the storefront. Nothing is emitted while this is off.",
            [$"{p}.SiteKey"] = "Site key",
            [$"{p}.SiteKey.Hint"] = "The environment's public key, starts with \"ls_\" (legacy keys start with \"ds_\"). Not a secret: it only works from the environment's registered origins.",
            [$"{p}.SiteKey.FormatWarning"] = "DeploySeal keys start with \"ls_\" (legacy keys with \"ds_\"). Check that you copied the environment's public key, not something else.",
            [$"{p}.EnvironmentLabel"] = "Environment label",
            [$"{p}.EnvironmentLabel.Hint"] = "Usually fine as guessed from the store URL. Change it only if this store should declare a different label (lower-case letters, digits and hyphens, up to 32 characters); it is a declaration, not an identifier — the site key decides where evidence lands.",
            [$"{p}.BuildMarkerSource"] = "Build marker source",
            [$"{p}.BuildMarkerSource.Hint"] = "Where the \"which code is running\" marker comes from. Prefer a git SHA when your deploy can write one.",
            [$"{p}.BuildMarkerSource.NopVersion"] = "nopCommerce version (e.g. 4.60.6)",
            [$"{p}.BuildMarkerSource.NopVersionPlusGitSha"] = "nopCommerce version + git SHA when available (e.g. 4.60.6+a1b2c3d)",
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

            [$"{p}.ApiKey"] = "DeploySeal API key",
            [$"{p}.ApiKey.Hint"] = "An organisation API key with the Write scope (DeploySeal → Settings → Integrations → API keys). Only used to send the plugin inventory, server to server. Leave empty to keep the stored key.",
            [$"{p}.ApiKey.Stored"] = "An API key is stored. Leave the box empty to keep it, or tick the box to remove it.",
            [$"{p}.ApiKey.Clear"] = "Remove the stored API key",
            [$"{p}.ApiBase"] = "API base (advanced)",
            [$"{p}.ApiBase.Hint"] = "The DeploySeal API host the inventory is posted to. Leave at https://api.deployseal.com unless you self-host DeploySeal.",
            [$"{p}.SendInventory"] = "Send inventory on a schedule",
            [$"{p}.SendInventory.Hint"] = "Post the list of installed plugins and their versions to DeploySeal every 6 hours (Administration → System → Schedule tasks, \"Send platform inventory to DeploySeal\"), so the readiness report can print exactly which plugins were installed when a campaign was tested. Needs the site key and the API key.",

            [$"{p}.Inventory.Title"] = "Platform inventory",
            [$"{p}.Inventory.Intro"] = "DeploySeal can record which plugins, at which versions, were installed when a release was tested — release evidence only the platform itself can provide. What is sent: every plugin nopCommerce knows about (system name, display name, version, installed or not), the nopCommerce version and the build marker above. Nothing else.",
            [$"{p}.Inventory.Count"] = "Plugins that will be reported",
            [$"{p}.Inventory.Installed"] = "installed",
            [$"{p}.Inventory.Fingerprint"] = "Fingerprint",
            [$"{p}.Inventory.Endpoint"] = "Sent to",
            [$"{p}.Inventory.NotReady"] = "Enter the site key and a DeploySeal API key (Write scope) above and save before sending.",
            [$"{p}.Inventory.SendNow"] = "Send inventory now",
            [$"{p}.Inventory.Schedule"] = "The scheduled task sends the same snapshot every 6 hours while \"Send inventory on a schedule\" is on. Resending an unchanged list does not grow the record: the API answers 200 with the snapshot it already holds.",
            [$"{p}.Inventory.Sent"] = "Inventory sent: HTTP {0} — {1} plugins recorded as snapshot {2} ({3}).",
            [$"{p}.Inventory.Failed"] = "Inventory not sent: {0}",

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
            [$"{p}.Facts.BuildMarker.SeeAdvanced"] = "No SHA — see Advanced.",
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

    /// <summary>Plugin upgrades: settings and locale strings added since the installed version, and the inventory task.</summary>
    public override async Task UpdateAsync(string currentVersion, string targetVersion)
    {
        await EnsureInventoryTaskAsync();
        await base.UpdateAsync(currentVersion, targetVersion);
    }

    /// <summary>The scheduled inventory send, registered the way nopCommerce's own plugins register theirs.</summary>
    private async Task EnsureInventoryTaskAsync()
    {
        if (await _scheduleTaskService.GetTaskByTypeAsync(DeploySealDefaults.InventoryTaskType) != null)
            return;

        await _scheduleTaskService.InsertTaskAsync(new ScheduleTask
        {
            Enabled = true,
            LastEnabledUtc = DateTime.UtcNow,
            StopOnError = false,
            Seconds = DeploySealContract.InventorySendPeriodHours * 60 * 60,
            Name = DeploySealDefaults.InventoryTaskName,
            Type = DeploySealDefaults.InventoryTaskType,
        });
    }

    public override async Task UninstallAsync()
    {
        var task = await _scheduleTaskService.GetTaskByTypeAsync(DeploySealDefaults.InventoryTaskType);
        if (task != null)
            await _scheduleTaskService.DeleteTaskAsync(task);

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
