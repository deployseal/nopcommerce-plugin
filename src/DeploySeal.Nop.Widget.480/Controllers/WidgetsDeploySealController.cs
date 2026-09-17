using DeploySeal.Nop.Core;
using DeploySeal.Nop.Core.Inventory;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Core;
using Nop.Core.Domain.Cms;
using Nop.Plugin.Widgets.DeploySeal.Models;
using Nop.Plugin.Widgets.DeploySeal.Services;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Security;
using Nop.Services.Stores;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Widgets.DeploySeal.Controllers;

[Area(AreaNames.ADMIN)]
[AuthorizeAdmin]
[AutoValidateAntiforgeryToken]
public class WidgetsDeploySealController : BasePluginController
{
    private readonly IBuildMarkerService _buildMarkerService;
    private readonly IInventoryService _inventoryService;
    private readonly ILocalizationService _localizationService;
    private readonly INotificationService _notificationService;
    private readonly ISettingService _settingService;
    private readonly IStoreContext _storeContext;
    private readonly IStoreService _storeService;
    private readonly WidgetSettings _widgetSettings;

    public WidgetsDeploySealController(
        IBuildMarkerService buildMarkerService,
        IInventoryService inventoryService,
        ILocalizationService localizationService,
        INotificationService notificationService,
        ISettingService settingService,
        IStoreContext storeContext,
        IStoreService storeService,
        WidgetSettings widgetSettings)
    {
        _buildMarkerService = buildMarkerService;
        _inventoryService = inventoryService;
        _localizationService = localizationService;
        _notificationService = notificationService;
        _settingService = settingService;
        _storeContext = storeContext;
        _storeService = storeService;
        _widgetSettings = widgetSettings;
    }

    [CheckPermission(StandardPermission.Configuration.MANAGE_WIDGETS)]
    public async Task<IActionResult> Configure()
    {
        var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
        var settings = await _settingService.LoadSettingAsync<DeploySealSettings>(storeScope);

        var model = new ConfigurationModel
        {
            ActiveStoreScopeConfiguration = storeScope,
            Enabled = settings.Enabled,
            SiteKey = settings.SiteKey,
            EnvironmentLabel = settings.EnvironmentLabel,
            BuildMarkerSourceId = settings.BuildMarkerSourceId,
            GitShaFilePath = settings.GitShaFilePath,
            ManualBuildMarker = settings.ManualBuildMarker,
            ScriptHost = settings.ScriptHost,
            RenderOnAdmin = settings.RenderOnAdmin,
            // The API key is write-only: the page shows whether one is stored, never the value.
            ApiKey = null,
            ApiKeyIsSet = !string.IsNullOrWhiteSpace(settings.ApiKey),
            ApiBase = settings.ApiBase,
            SendInventory = settings.SendInventory,
        };

        if (storeScope > 0)
        {
            model.Enabled_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.Enabled, storeScope);
            model.SiteKey_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.SiteKey, storeScope);
            model.EnvironmentLabel_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.EnvironmentLabel, storeScope);
            model.BuildMarkerSourceId_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.BuildMarkerSourceId, storeScope);
            model.GitShaFilePath_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.GitShaFilePath, storeScope);
            model.ManualBuildMarker_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.ManualBuildMarker, storeScope);
            model.ScriptHost_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.ScriptHost, storeScope);
            model.RenderOnAdmin_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.RenderOnAdmin, storeScope);
            model.ApiKey_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.ApiKey, storeScope);
            model.ApiBase_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.ApiBase, storeScope);
            model.SendInventory_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.SendInventory, storeScope);
        }

        await PrepareInstallerFactsAsync(model, settings, storeScope);

        return View(DeploySealDefaults.ConfigureViewPath, model);
    }

    [HttpPost]
    [CheckPermission(StandardPermission.Configuration.MANAGE_WIDGETS)]
    public async Task<IActionResult> Configure(ConfigurationModel model)
    {
        var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
        var settings = await _settingService.LoadSettingAsync<DeploySealSettings>(storeScope);

        settings.Enabled = model.Enabled;
        settings.SiteKey = model.SiteKey?.Trim();
        // Always store a contract-valid label. An empty label falls back to the store-derived
        // default so the declared label is never blank; the page shows which one applies.
        var label = EnvironmentLabel.Slugify(model.EnvironmentLabel);
        if (label.Length == 0)
        {
            var stores = await _storeService.GetAllStoresAsync();
            var scopeStore = stores.FirstOrDefault(s => s.Id == storeScope) ?? stores.FirstOrDefault();
            label = EnvironmentLabel.DeriveFromStoreUrl(scopeStore?.Url);
        }
        settings.EnvironmentLabel = label;
        settings.BuildMarkerSourceId = Enum.IsDefined(typeof(BuildMarkerSource), model.BuildMarkerSourceId)
            ? model.BuildMarkerSourceId
            : (int)BuildMarkerSource.NopVersionPlusGitSha;
        settings.GitShaFilePath = model.GitShaFilePath?.Trim();
        settings.ManualBuildMarker = BuildMarkerResolver.Clamp(BuildMarkerResolver.Sanitize(model.ManualBuildMarker));
        settings.ScriptHost = string.IsNullOrWhiteSpace(model.ScriptHost)
            ? DeploySealContract.DefaultScriptHost
            : model.ScriptHost.Trim().TrimEnd('/');
        settings.RenderOnAdmin = model.RenderOnAdmin;
        // Secret: an empty box keeps the stored key; the tick box removes it; anything else replaces it.
        if (model.ClearApiKey)
            settings.ApiKey = string.Empty;
        else if (!string.IsNullOrWhiteSpace(model.ApiKey))
            settings.ApiKey = model.ApiKey.Trim();
        settings.ApiBase = string.IsNullOrWhiteSpace(model.ApiBase)
            ? DeploySealContract.DefaultApiBase
            : model.ApiBase.Trim().TrimEnd('/');
        settings.SendInventory = model.SendInventory;

        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.Enabled, model.Enabled_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.SiteKey, model.SiteKey_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.EnvironmentLabel, model.EnvironmentLabel_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.BuildMarkerSourceId, model.BuildMarkerSourceId_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.GitShaFilePath, model.GitShaFilePath_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.ManualBuildMarker, model.ManualBuildMarker_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.ScriptHost, model.ScriptHost_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.RenderOnAdmin, model.RenderOnAdmin_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.ApiKey, model.ApiKey_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.ApiBase, model.ApiBase_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.SendInventory, model.SendInventory_OverrideForStore, storeScope, false);

        await _settingService.ClearCacheAsync();

        _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Admin.Plugins.Saved"));

        return await Configure();
    }


    /// <summary>"Send inventory now": one send for the active store scope, result as a notification.</summary>
    [HttpPost]
    [CheckPermission(StandardPermission.Configuration.MANAGE_WIDGETS)]
    public async Task<IActionResult> SendInventory()
    {
        var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
        var settings = await _settingService.LoadSettingAsync<DeploySealSettings>(storeScope);

        await NotifyInventoryResultAsync(await _inventoryService.SendAsync(settings));

        return RedirectToAction("Configure");
    }

    private async Task NotifyInventoryResultAsync(InventorySendResult result)
    {
        var p = DeploySealDefaults.LocalePrefix;
        if (result.Ok)
        {
            var message = string.Format(
                await _localizationService.GetResourceAsync($"{p}.Inventory.Sent"),
                result.StatusCode,
                result.ItemCount?.ToString() ?? "?",
                result.SnapshotId ?? "?",
                result.Created ? "new observation" : "already on record");
            _notificationService.SuccessNotification(message);
        }
        else
        {
            _notificationService.ErrorNotification(string.Format(await _localizationService.GetResourceAsync($"{p}.Inventory.Failed"), result.Error));
        }
    }

    /// <summary>Contract §7: origins, key, exact marker and its source, declared label, links.</summary>
    private async Task PrepareInstallerFactsAsync(ConfigurationModel model, DeploySealSettings settings, int storeScope)
    {
        model.IsWidgetActive = _widgetSettings.ActiveWidgetSystemNames?.Contains(DeploySealDefaults.SystemName) == true;
        model.NopVersion = _buildMarkerService.PlatformVersion;
        model.PluginVersion = DeploySealDefaults.PluginVersion;
        model.DocsUrl = DeploySealContract.DocsUrl;
        model.AppUrl = DeploySealContract.AppUrl;

        foreach (BuildMarkerSource source in Enum.GetValues<BuildMarkerSource>())
        {
            model.AvailableBuildMarkerSources.Add(new SelectListItem
            {
                Value = ((int)source).ToString(),
                Text = await _localizationService.GetResourceAsync($"{DeploySealDefaults.LocalePrefix}.BuildMarkerSource.{source}"),
                Selected = (int)source == model.BuildMarkerSourceId,
            });
        }

        // Origins: every store, with the one in scope flagged (all stores are in scope at scope 0).
        var stores = await _storeService.GetAllStoresAsync();
        foreach (var store in stores)
        {
            model.Stores.Add(new StoreOriginsModel
            {
                StoreId = store.Id,
                StoreName = store.Name,
                StoreUrl = store.Url,
                SslEnabled = store.SslEnabled,
                IsInScope = storeScope == 0 || store.Id == storeScope,
                Origins = OriginFormatter.ForStore(store.Url, store.SslEnabled, store.Hosts).ToList(),
            });
        }

        var scopeStore = stores.FirstOrDefault(s => s.Id == storeScope) ?? stores.FirstOrDefault();
        model.DerivedEnvironmentLabel = EnvironmentLabel.DeriveFromStoreUrl(scopeStore?.Url);
        var configuredLabel = EnvironmentLabel.Slugify(settings.EnvironmentLabel);
        model.EffectiveEnvironmentLabel = configuredLabel.Length > 0 ? configuredLabel : string.Empty;

        var marker = _buildMarkerService.Resolve(settings);
        model.BuildMarker = marker.Marker;
        model.BuildMarkerSourceDescription = marker.SourceDescription;
        model.BuildMarkerWarning = marker.Warning;
        model.GitShaResolvedPath = _buildMarkerService.ResolveShaFilePath(settings.GitShaFilePath);
        model.GitShaFound = _buildMarkerService.ReadGitSha(settings.GitShaFilePath) is not null;

        model.SnippetPreview = SnippetBuilder.Build(settings.ScriptHost, settings.SiteKey, model.EffectiveEnvironmentLabel, marker.Marker);

        var key = settings.SiteKey?.Trim() ?? string.Empty;
        if (key.Length > 0 && !(key.StartsWith("ls_", StringComparison.Ordinal) || key.StartsWith("ds_", StringComparison.Ordinal)))
            model.SiteKeyWarning = await _localizationService.GetResourceAsync($"{DeploySealDefaults.LocalePrefix}.SiteKey.FormatWarning");

        // Inventory (contract §6): what the next send would carry and where it goes.
        var snapshot = await _inventoryService.CollectAsync(settings);
        model.InventoryItemCount = snapshot.Items.Count;
        model.InventoryInstalledCount = snapshot.Items.Count(i => i.Enabled);
        model.InventorySha256 = snapshot.Sha256;
        model.InventoryEndpoint = InventoryClient.EndpointUrl(settings.ApiBase, settings.SiteKey);
        model.CanSendInventory = key.Length > 0 && !string.IsNullOrWhiteSpace(settings.ApiKey);
    }
}
