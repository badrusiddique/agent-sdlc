using BuildGenix.Data.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BuildGenix.Data.Infrastructure;

/// <summary>
/// Creates every table listed in <see cref="TableCatalog.Built"/> at host start.
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
        var writer = scope.ServiceProvider.GetRequiredService<ITableEntityWriter>();

        foreach (var tableName in TableCatalog.Built)
        {
            await writer.EnsureTableAsync(tableName, cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("Ensured table {TableName}", tableName);
        }

        _logger.LogInformation(
            "BuildGenix table storage ready. Ensured {EnsuredCount}/{CatalogCount} catalog tables.",
            TableCatalog.Built.Count,
            TableCatalog.Built.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
