using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using DeploySeal.Nop.Core.Inventory;
using Nop.Core.Infrastructure;
using Nop.Plugin.Widgets.DeploySeal.Services;
using Nop.Web.Framework.Infrastructure.Extensions;

namespace Nop.Plugin.Widgets.DeploySeal.Infrastructure;

/// <summary>Registers the plugin's services with nopCommerce's DI container.</summary>
public class NopStartup : INopStartup
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Scoped: the build marker is resolved (and the SHA file read) at most once per request.
        services.AddScoped<IBuildMarkerService, BuildMarkerService>();

        // Inventory (contract §6): a typed HttpClient with a hard timeout, through the store's
        // proxy settings like every other outbound nopCommerce client; the service on top is
        // scoped so a request and a task run each get their own.
        services.AddHttpClient<InventoryClient>(client => client.Timeout = TimeSpan.FromSeconds(15)).WithProxy();
        services.AddScoped<IInventoryService, InventoryService>();
    }

    public void Configure(IApplicationBuilder application)
    {
    }

    public int Order => 3000;
}
