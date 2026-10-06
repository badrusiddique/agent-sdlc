using AgentHub.Agents.Healthy.Models;

namespace AgentHub.Agents.Healthy.Abstractions;

public interface IAgentResultPublisher
{
    Task PublishResultAsync(AgentResult result, CancellationToken cancellationToken = default);
}
