using AgentHub.Data.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgentHub.Data.Infrastructure;

/// <summary>
/// Creates AgentHub tables (ActivityLog, EffectLedger, AgProcessedEvents) at host start.
/// </summary>
public sealed class TableStorageInitializer : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TableStorageInitializer> _logger;

    public TableStorageInitializer(
        IServiceScopeFactory scopeFactory,
        ILogger<TableStorageInitializer> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var options = scope.ServiceProvider
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<Options.TableStorageOptions>>()
            .Value;
        if (options.Provider.Equals("Memory", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("AgentHub table storage skipped (Provider=Memory).");
            return;
        }

        var activity = scope.ServiceProvider.GetRequiredService<IActivityLogRepository>();
        var ledger = scope.ServiceProvider.GetRequiredService<IEffectLedgerRepository>();
        var dedup = scope.ServiceProvider.GetRequiredService<IAgProcessedEventsRepository>();

        await activity.EnsureTableAsync(cancellationToken).ConfigureAwait(false);
        await ledger.EnsureTableAsync(cancellationToken).ConfigureAwait(false);
        await dedup.EnsureTableAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "AgentHub table storage ready. Catalog size={CatalogCount}",
            TableCatalog.Built.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
