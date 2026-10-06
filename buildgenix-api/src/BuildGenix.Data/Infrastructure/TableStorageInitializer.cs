using BuildGenix.Data.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BuildGenix.Data.Infrastructure;

/// <summary>
/// Creates BuildGenix tables that Iteration 0a repositories touch.
/// Remaining catalog tables are added as their repositories land.
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
        var projects = scope.ServiceProvider.GetRequiredService<IProjectRepository>();
        var wm = scope.ServiceProvider.GetRequiredService<IWmProcessedEventsRepository>();

        await projects.EnsureTableAsync(cancellationToken).ConfigureAwait(false);
        await wm.EnsureTableAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "BuildGenix table storage ready. Catalog size={CatalogCount}",
            TableCatalog.Built.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
