using Nop.Services.ScheduleTasks;

namespace Nop.Plugin.Widgets.DeploySeal.Services;

/// <summary>
/// The scheduled task (Administration → System → Schedule tasks, "Send platform inventory to
/// DeploySeal", every 6 hours by default) that posts the plugin inventory for every store with
/// "Send inventory" on. Registered by the plugin's install, removed by its uninstall, exactly
/// like nopCommerce's own Brevo synchronisation task.
/// </summary>
public class InventorySyncTask : IScheduleTask
{
    private readonly IInventoryService _inventoryService;

    public InventorySyncTask(IInventoryService inventoryService)
    {
        _inventoryService = inventoryService;
    }

    public async Task ExecuteAsync()
    {
        await _inventoryService.SendForAllStoresAsync();
    }
}
