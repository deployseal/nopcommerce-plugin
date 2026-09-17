using DeploySeal.Nop.Core;
using DeploySeal.Nop.Core.Inventory;

namespace Nop.Plugin.Widgets.DeploySeal.Services;

/// <summary>
/// Collects the store's platform inventory (every plugin nopCommerce knows about, installed or
/// not) and posts it to DeploySeal (install contract §6). Scoped per request / task run.
/// </summary>
public interface IInventoryService
{
    /// <summary>Reads the plugin list and composes the snapshot for these settings (build marker included). Does not send.</summary>
    Task<InventorySnapshot> CollectAsync(DeploySealWidgetOptions settings);

    /// <summary>Collects and sends once for these settings. Needs a site key and an API key; never throws.</summary>
    Task<InventorySendResult> SendAsync(DeploySealWidgetOptions settings);

    /// <summary>
    /// What the scheduled task runs: every store whose effective settings have "Send inventory"
    /// on plus a site key and an API key, one send per distinct (API base, API key, site key).
    /// </summary>
    Task<IList<InventorySendResult>> SendForAllStoresAsync();
}
