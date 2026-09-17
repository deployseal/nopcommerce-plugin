using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nop.Web.Framework;
using Nop.Web.Framework.Mvc.Routing;
using Nop.Web.Infrastructure;

namespace Nop.Plugin.Widgets.DeploySeal.Infrastructure;

/// <summary>Registers the configuration page route.</summary>
public class RouteProvider : BaseRouteProvider, IRouteProvider
{
    public void RegisterRoutes(IEndpointRouteBuilder endpointRouteBuilder)
    {
        endpointRouteBuilder.MapControllerRoute(
            name: DeploySealDefaults.ConfigurationRouteName,
            pattern: DeploySealDefaults.ConfigurationRoutePattern,
            defaults: new { controller = "WidgetsDeploySeal", action = "Configure", area = AreaNames.ADMIN });
    }

    public int Priority => 0;
}
