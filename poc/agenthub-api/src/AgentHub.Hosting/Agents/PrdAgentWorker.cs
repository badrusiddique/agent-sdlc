using AgentHub.Agents.Pipeline;
using AgentHub.Core.Messaging;
using AgentHub.Infrastructure.Messaging;
using AgentHub.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentHub.Hosting.Agents;

public sealed class PrdAgentWorker : AgentTopicWorkerBase
{
    public PrdAgentWorker(
        IAgentTaskBus bus,
        PipelineAgentDispatcher dispatcher,
        IHttpClientFactory httpClientFactory,
        IOptions<PipelineWorkerOptions> worker,
        ILogger<PrdAgentWorker> logger)
        : base(bus, dispatcher, httpClientFactory, worker, logger)
    {
    }

    protected override string Topic => AgentTopics.Prd;
    protected override string Subscription => AgentTopics.PrdSubscription;
    protected override string AgentName => "PRD";
}
