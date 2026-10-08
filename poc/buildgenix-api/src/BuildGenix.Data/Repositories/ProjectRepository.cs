using BuildGenix.Data.Entities;
using BuildGenix.Data.Infrastructure;
using Microsoft.Extensions.Logging;

namespace BuildGenix.Data.Repositories;

public sealed class ProjectRepository : IProjectRepository
{
    private readonly ITableClientFactory _factory;
    private readonly ILogger<ProjectRepository> _logger;

    public ProjectRepository(ITableClientFactory factory, ILogger<ProjectRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task EnsureTableAsync(CancellationToken cancellationToken = default)
    {
        var client = _factory.GetClient(TableNames.Projects);
        await client.CreateIfNotExistsAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Ensured table {Table}", _factory.ResolveTableName(TableNames.Projects));
    }

    public async Task<ProjectEntity?> GetAsync(string projectId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        var client = _factory.GetClient(TableNames.Projects);
        try
        {
            var response = await client
                .GetEntityAsync<ProjectEntity>(ProjectEntity.DefaultPartitionKey, projectId, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return response.Value;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<ProjectEntity>> ListAsync(CancellationToken cancellationToken = default)
    {
        var client = _factory.GetClient(TableNames.Projects);
        var items = new List<ProjectEntity>();
        await foreach (var entity in client.QueryAsync<ProjectEntity>(
                           e => e.PartitionKey == ProjectEntity.DefaultPartitionKey,
                           cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            if (entity.RowKey.StartsWith("~", StringComparison.Ordinal))
            {
                continue;
            }

            items.Add(entity);
        }

        return items.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task UpsertAsync(ProjectEntity project, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(project.RowKey);

        project.PartitionKey = ProjectEntity.DefaultPartitionKey;
        var client = _factory.GetClient(TableNames.Projects);
        await client.UpsertEntityAsync(project, cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
