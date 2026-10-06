using BuildGenix.Data.Repositories;
using BuildGenix.WebApi.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace BuildGenix.WebApi.Tests;

public sealed class BuildGenixApiFactory : WebApplicationFactory<Program>
{
    public InMemoryProjectRepository Projects { get; } = new();
    public InMemoryWorkItemRepository WorkItems { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureTestServices(services =>
        {
            var hosted = services.Where(d => d.ServiceType == typeof(IHostedService)).ToList();
            foreach (var descriptor in hosted)
            {
                services.Remove(descriptor);
            }

            services.RemoveAll<IProjectRepository>();
            services.RemoveAll<IWorkItemRepository>();
            services.AddSingleton<IProjectRepository>(Projects);
            services.AddSingleton<IWorkItemRepository>(WorkItems);
        });
    }
}
