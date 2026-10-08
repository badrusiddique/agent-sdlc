using AgentHub.Core.Models;

namespace AgentHub.Infrastructure.Messaging;

public interface IHealthyTaskPublisher
{
    Task PublishAsync(TaskMessage message, CancellationToken cancellationToken = default);
}
