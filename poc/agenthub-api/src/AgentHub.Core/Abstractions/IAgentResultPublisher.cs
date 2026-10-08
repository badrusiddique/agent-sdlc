using AgentHub.Core.Models;

namespace AgentHub.Core.Abstractions;

public interface IAgentResultPublisher
{
    Task PublishResultAsync(AgentResult result, CancellationToken cancellationToken = default);
}
