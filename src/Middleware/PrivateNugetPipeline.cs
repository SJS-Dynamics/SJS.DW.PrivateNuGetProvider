using Dynamicweb.Host.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SJS.DW.PrivateNugetProvider.Models;

namespace SJS.DW.PrivateNugetProvider.Middleware;

public class PrivateNugetPipeline : IPipeline
{
    public void RegisterServices(IServiceCollection services, IMvcCoreBuilder mvcBuilder)
    {
        var configuration = services.BuildServiceProvider().GetRequiredService<IConfiguration>();
        
        // Pull PrivateNugetFeeds from configuration
        var nugetFeeds = configuration.GetSection("PrivateNugetFeeds").Get<List<NugetFeedModel>>() ?? new List<NugetFeedModel>();
        
        // Assign to static config
        NugetFeedsConfig.Feeds = nugetFeeds;
    }

    public void RegisterApplicationComponents(IApplicationBuilder app)
    {
        // No application components to register
    }

    public void RunInitializers()
    {
        // No initializers to run
    }

    public int Rank => 100;
}