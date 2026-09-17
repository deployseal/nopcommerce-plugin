using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Infrastructure;
using Nop.Plugin.Widgets.DeploySeal.Services;

namespace Nop.Plugin.Widgets.DeploySeal.Infrastructure;

/// <summary>Registers the plugin's services with nopCommerce's DI container.</summary>
public class NopStartup : INopStartup
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Scoped: the build marker is resolved (and the SHA file read) at most once per request.
        services.AddScoped<IBuildMarkerService, BuildMarkerService>();
    }

    public void Configure(IApplicationBuilder application)
    {
    }

    public int Order => 3000;
}
