using BuildGenix.Data.Entities;

namespace BuildGenix.Data.Repositories;

public interface IProjectRepository
{
    Task EnsureTableAsync(CancellationToken cancellationToken = default);

    Task<ProjectEntity?> GetAsync(string projectId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectEntity>> ListAsync(CancellationToken cancellationToken = default);

    Task UpsertAsync(ProjectEntity project, CancellationToken cancellationToken = default);
}
