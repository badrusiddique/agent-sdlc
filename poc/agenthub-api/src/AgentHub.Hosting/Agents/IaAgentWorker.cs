using AgentHub.Agents.Pipeline;
using AgentHub.Core.Messaging;
using AgentHub.Infrastructure.Messaging;
using AgentHub.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentHub.Hosting.Agents;

public sealed class IaAgentWorker : AgentTopicWorkerBase
{
    public IaAgentWorker(
        IAgentTaskBus bus,
        PipelineAgentDispatcher dispatcher,
        IHttpClientFactory httpClientFactory,
        IOptions<PipelineWorkerOptions> worker,
        ILogger<IaAgentWorker> logger)
        : base(bus, dispatcher, httpClientFactory, worker, logger)
    {
    }

    protected override string Topic => AgentTopics.Ia;
    protected override string Subscription => AgentTopics.IaSubscription;
    protected override string AgentName => "IA";
}
