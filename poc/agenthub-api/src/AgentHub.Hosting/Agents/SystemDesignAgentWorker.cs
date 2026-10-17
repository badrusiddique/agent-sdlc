using AgentHub.Agents.Pipeline;
using AgentHub.Core.Messaging;
using AgentHub.Infrastructure.Messaging;
using AgentHub.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentHub.Hosting.Agents;

public sealed class SystemDesignAgentWorker : AgentTopicWorkerBase
{
    public SystemDesignAgentWorker(
        IAgentTaskBus bus,
        PipelineAgentDispatcher dispatcher,
        IHttpClientFactory httpClientFactory,
        IOptions<PipelineWorkerOptions> worker,
        ILogger<SystemDesignAgentWorker> logger)
        : base(bus, dispatcher, httpClientFactory, worker, logger)
    {
    }

    protected override string Topic => AgentTopics.SystemDesign;
    protected override string Subscription => AgentTopics.SystemDesignSubscription;
    protected override string AgentName => "SystemDesign";
}
