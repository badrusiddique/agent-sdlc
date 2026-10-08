using AgentHub.Data.Infrastructure;
using AgentHub.Data.Options;
using AgentHub.Data.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgentHub.Data;

public static class DependencyInjection
{
    public static IServiceCollection AddAgentHubData(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<TableStorageOptions>(configuration.GetSection(TableStorageOptions.SectionName));
        services.AddSingleton<ITableClientFactory, TableClientFactory>();
        services.AddSingleton<IActivityLogRepository, ActivityLogRepository>();
        services.AddSingleton<IEffectLedgerRepository, EffectLedgerRepository>();
        services.AddSingleton<IAgProcessedEventsRepository, AgProcessedEventsRepository>();
        services.AddHostedService<TableStorageInitializer>();
        return services;
    }
}
