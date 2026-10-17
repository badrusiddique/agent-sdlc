using AgentHub.Agents.Pipeline;
using AgentHub.Core.Messaging;
using AgentHub.Infrastructure.Messaging;
using AgentHub.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentHub.Hosting.Agents;

public sealed class DeployAgentWorker : AgentTopicWorkerBase
{
    public DeployAgentWorker(
        IAgentTaskBus bus,
        PipelineAgentDispatcher dispatcher,
        IHttpClientFactory httpClientFactory,
        IOptions<PipelineWorkerOptions> worker,
        ILogger<DeployAgentWorker> logger)
        : base(bus, dispatcher, httpClientFactory, worker, logger)
    {
    }

    protected override string Topic => AgentTopics.Deploy;
    protected override string Subscription => AgentTopics.DeploySubscription;
    protected override string AgentName => "Deploy";
}
