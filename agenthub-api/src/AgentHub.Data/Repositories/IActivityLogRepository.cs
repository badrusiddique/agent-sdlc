using AgentHub.Data.Entities;

namespace AgentHub.Data.Repositories;

public interface IActivityLogRepository
{
    Task EnsureTableAsync(CancellationToken cancellationToken = default);

    Task AppendAsync(ActivityLogEntity entry, CancellationToken cancellationToken = default);
}
