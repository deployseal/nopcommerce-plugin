using DeploySeal.Nop.Core;
using Nop.Core.Configuration;

namespace Nop.Plugin.Widgets.DeploySeal;

/// <summary>
/// nopCommerce settings for the plugin. All values (and their meaning) come from the shared
/// <see cref="DeploySealWidgetOptions"/>; this class only adds the ISettings marker so the
/// setting service persists them (inherited public properties included) and DI injects them
/// per store.
/// </summary>
public class DeploySealSettings : DeploySealWidgetOptions, ISettings
{
}
