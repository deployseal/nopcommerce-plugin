using DeploySeal.Nop.Core;
using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Widgets.DeploySeal.Services;
using Nop.Web.Framework;
using Nop.Web.Framework.Components;
using Nop.Web.Framework.Infrastructure;

namespace Nop.Plugin.Widgets.DeploySeal.Components;

/// <summary>
/// Emits the contract's script tag in the storefront &lt;head&gt; (and, only when opted in, at the
/// top of the admin body, since the admin layout has no head widget zone).
/// </summary>
public class WidgetsDeploySealViewComponent : NopViewComponent
{
    private readonly DeploySealSettings _settings;
    private readonly IBuildMarkerService _buildMarkerService;

    public WidgetsDeploySealViewComponent(DeploySealSettings settings, IBuildMarkerService buildMarkerService)
    {
        _settings = settings;
        _buildMarkerService = buildMarkerService;
    }

    public IViewComponentResult Invoke(string widgetZone, object additionalData)
    {
        if (!_settings.Enabled || string.IsNullOrWhiteSpace(_settings.SiteKey))
            return Content(string.Empty);

        var isAdminZone = widgetZone == AdminWidgetZones.HeaderBefore;
        var isAdminRequest = string.Equals(
            HttpContext.Request.RouteValues.TryGetValue("area", out var area) ? area?.ToString() : null,
            AreaNames.ADMIN, StringComparison.OrdinalIgnoreCase);

        if ((isAdminZone || isAdminRequest) && !_settings.RenderOnAdmin)
            return Content(string.Empty);

        var marker = _buildMarkerService.Resolve(_settings);
        var tag = SnippetBuilder.Build(
            _settings.ScriptHost,
            _settings.SiteKey,
            EnvironmentLabel.Slugify(_settings.EnvironmentLabel),
            marker.Marker,
            DeploySealDefaults.PluginVersion);

        return string.IsNullOrEmpty(tag)
            ? Content(string.Empty)
            : View(DeploySealDefaults.PublicInfoViewPath, tag);
    }
}
