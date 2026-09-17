using DeploySeal.Nop.Core;
using DeploySeal.Nop.Core.Inventory;
using Nop.Services.Configuration;
using Nop.Services.Logging;
using Nop.Services.Plugins;
using Nop.Services.Stores;

namespace Nop.Plugin.Widgets.DeploySeal.Services;

/// <summary>
/// The nopCommerce half of the inventory feature: the plugin list comes from
/// <see cref="IPluginService"/> (installed and not installed, so a disabled plugin is on the
/// record as <c>enabled: false</c>), the platform version from <c>NopVersion.FULL_VERSION</c>,
/// the build marker from the same resolver the storefront tag uses.
/// </summary>
public class InventoryService : IInventoryService
{
    private readonly IBuildMarkerService _buildMarkerService;
    private readonly InventoryClient _client;
    private readonly ILogger _logger;
    private readonly IPluginService _pluginService;
    private readonly ISettingService _settingService;
    private readonly IStoreService _storeService;

    public InventoryService(
        IBuildMarkerService buildMarkerService,
        InventoryClient client,
        ILogger logger,
        IPluginService pluginService,
        ISettingService settingService,
        IStoreService storeService)
    {
        _buildMarkerService = buildMarkerService;
        _client = client;
        _logger = logger;
        _pluginService = pluginService;
        _settingService = settingService;
        _storeService = storeService;
    }

    /// <inheritdoc />
    public async Task<InventorySnapshot> CollectAsync(DeploySealWidgetOptions settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var descriptors = await _pluginService.GetPluginDescriptorsAsync<IPlugin>(LoadPluginsMode.All);
        var items = descriptors.Select(d => new InventoryItem(d.SystemName, d.FriendlyName, d.Version, d.Installed));
        var marker = _buildMarkerService.Resolve(settings);

        return InventorySnapshotBuilder.Build(_buildMarkerService.PlatformVersion, marker.Marker, DateTime.UtcNow, items);
    }

    /// <inheritdoc />
    public async Task<InventorySendResult> SendAsync(DeploySealWidgetOptions settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        InventorySendResult result;
        try
        {
            var snapshot = await CollectAsync(settings);
            result = await _client.SendAsync(settings.ApiBase, settings.ApiKey, settings.SiteKey, snapshot);
        }
        catch (Exception ex)
        {
            // Collecting can only fail if nopCommerce itself is unwell; still never let it escape.
            result = InventorySendResult.Failure(InventoryClient.EndpointUrl(settings.ApiBase, settings.SiteKey), "Could not read the plugin list: " + ex.Message);
        }

        if (result.Ok)
            await _logger.InformationAsync($"DeploySeal inventory sent (HTTP {result.StatusCode}): {result.ItemCount} plugins, snapshot {result.SnapshotId} → {result.Url}");
        else
            await _logger.WarningAsync($"DeploySeal inventory not sent: {result.Error} ({result.Url})");

        return result;
    }

    /// <inheritdoc />
    public async Task<IList<InventorySendResult>> SendForAllStoresAsync()
    {
        var results = new List<InventorySendResult>();
        var sent = new HashSet<string>(StringComparer.Ordinal);

        foreach (var store in await _storeService.GetAllStoresAsync())
        {
            var settings = await _settingService.LoadSettingAsync<DeploySealSettings>(store.Id);
            if (!settings.SendInventory || string.IsNullOrWhiteSpace(settings.ApiKey) || string.IsNullOrWhiteSpace(settings.SiteKey))
                continue;

            // Stores that share one environment (same key) are one inventory: send it once.
            var scope = (settings.ApiBase ?? string.Empty).Trim() + "|" + settings.ApiKey.Trim() + "|" + settings.SiteKey.Trim();
            if (!sent.Add(scope))
                continue;

            results.Add(await SendAsync(settings));
        }

        return results;
    }
}
