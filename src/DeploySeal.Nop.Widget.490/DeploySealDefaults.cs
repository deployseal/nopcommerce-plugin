namespace Nop.Plugin.Widgets.DeploySeal;

/// <summary>
/// Plugin constants (the nopCommerce-facing half; contract values live in DeploySeal.Nop.Core.DeploySealContract).
/// </summary>
public static class DeploySealDefaults
{
    /// <summary>Plugin system name; must match plugin.json.</summary>
    public static string SystemName => "Widgets.DeploySeal";

    /// <summary>Plugin version; must match plugin.json.</summary>
    public static string PluginVersion => "1.2.0";

    /// <summary>Route name of the configuration page.</summary>
    public static string ConfigurationRouteName => "Plugin.Widgets.DeploySeal.Configure";

    /// <summary>Route pattern of the configuration page.</summary>
    public static string ConfigurationRoutePattern => "Admin/WidgetsDeploySeal/Configure";

    /// <summary>Prefix of every locale resource this plugin owns.</summary>
    public static string LocalePrefix => "Plugins.Widgets.DeploySeal";

    /// <summary>Display name of the scheduled inventory send (Administration â†’ System â†’ Schedule tasks).</summary>
    public static string InventoryTaskName => "Send platform inventory to DeploySeal";

    /// <summary>Type name nopCommerce resolves the task by; must match the class's full name.</summary>
    public static string InventoryTaskType => "Nop.Plugin.Widgets.DeploySeal.Services.InventorySyncTask";

    /// <summary>Path of the storefront view that emits the tag.</summary>
    public static string PublicInfoViewPath => "~/Plugins/Widgets.DeploySeal/Views/PublicInfo.cshtml";

    /// <summary>Path of the configuration view.</summary>
    public static string ConfigureViewPath => "~/Plugins/Widgets.DeploySeal/Views/Configure.cshtml";
}
