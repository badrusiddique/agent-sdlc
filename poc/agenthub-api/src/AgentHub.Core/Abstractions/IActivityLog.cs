namespace AgentHub.Core.Abstractions;

/// <summary>Host-neutral ActivityLog port. Implementations live in Infrastructure over AgentHub.Data.</summary>
public interface IActivityLog
{
    Task AppendAsync(
        string workItemId,
        string taskId,
        string stage,
        string level,
        string message,
        string? correlationId,
        CancellationToken cancellationToken = default);
}
