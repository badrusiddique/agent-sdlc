using AgentHub.Agents.Pipeline;
using AgentHub.Core.Messaging;
using AgentHub.Infrastructure.Messaging;
using AgentHub.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentHub.Hosting.Agents;

public sealed class TaAgentWorker : AgentTopicWorkerBase
{
    public TaAgentWorker(
        IAgentTaskBus bus,
        PipelineAgentDispatcher dispatcher,
        IHttpClientFactory httpClientFactory,
        IOptions<PipelineWorkerOptions> worker,
        ILogger<TaAgentWorker> logger)
        : base(bus, dispatcher, httpClientFactory, worker, logger)
    {
    }

    protected override string Topic => AgentTopics.Ta;
    protected override string Subscription => AgentTopics.TaSubscription;
    protected override string AgentName => "TA";
}
