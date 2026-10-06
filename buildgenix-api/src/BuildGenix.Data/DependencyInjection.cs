using BuildGenix.Data.Infrastructure;
using BuildGenix.Data.Options;
using BuildGenix.Data.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BuildGenix.Data;

public static class DependencyInjection
{
    public static IServiceCollection AddBuildGenixData(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<TableStorageOptions>(configuration.GetSection(TableStorageOptions.SectionName));
        services.AddSingleton<ITableClientFactory, TableClientFactory>();
        services.AddSingleton<IProjectRepository, ProjectRepository>();
        services.AddSingleton<IWmProcessedEventsRepository, WmProcessedEventsRepository>();
        services.AddHostedService<TableStorageInitializer>();
        return services;
    }
}
