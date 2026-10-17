using System.Net.Http.Json;
using System.Text.Json;
using AgentHub.Agents.Pipeline;
using AgentHub.Core.Messaging;
using AgentHub.Core.Models;
using AgentHub.Infrastructure.Messaging;
using AgentHub.Infrastructure.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentHub.Hosting.Agents;

/// <summary>
/// One BackgroundService per agent topic. Processes TaskMessages, REST-callbacks BuildGenix,
/// then publishes the next stage to the next agent topic (no orchestrator loop).
/// </summary>
public abstract class AgentTopicWorkerBase : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IAgentTaskBus _bus;
    private readonly PipelineAgentDispatcher _dispatcher;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PipelineWorkerOptions _worker;
    private readonly ILogger _logger;
    private IAsyncDisposable? _subscription;

    protected AgentTopicWorkerBase(
        IAgentTaskBus bus,
        PipelineAgentDispatcher dispatcher,
        IHttpClientFactory httpClientFactory,
        IOptions<PipelineWorkerOptions> worker,
        ILogger logger)
    {
        _bus = bus;
        _dispatcher = dispatcher;
        _httpClientFactory = httpClientFactory;
        _worker = worker.Value;
        _logger = logger;
    }

    protected abstract string Topic { get; }
    protected abstract string Subscription { get; }
    protected abstract string AgentName { get; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_worker.Enabled)
        {
            _logger.LogInformation("{Agent} worker disabled.", AgentName);
            return;
        }

        // Topic agents run whenever Enabled — InProcessOnly only affects the legacy single PipelineAgentWorker.
        _subscription = _bus.Subscribe(Topic, Subscription, HandleAsync);
        _logger.LogInformation(
            "{Agent} listening on topic={Topic} subscription={Subscription}",
            AgentName,
            Topic,
            Subscription);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shut down
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_subscription is not null)
        {
            await _subscription.DisposeAsync().ConfigureAwait(false);
        }

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleAsync(
        TaskMessage message,
        string messageId,
        int deliveryCount,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "{Agent} received stage={Stage} wi={WorkItemId} msgId={MessageId}",
            AgentName,
            message.Stage,
            message.WorkItemId,
            messageId);

        // Seed RouteBack probe on first TA_GENERATE when requested.
        if (string.Equals(message.Stage, PipelineAgentConstants.StageTaGenerate, StringComparison.OrdinalIgnoreCase)
            && message.Context.SimulateTaRouteback
            && message.Context.RouteBackCount == 0
            && !message.Artifacts.ContainsKey("taRoutebackProbe"))
        {
            message.Artifacts["taRoutebackProbe"] = "1";
        }

        if (string.Equals(message.Stage, PipelineAgentConstants.StageIaPushDeploy, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(message.Context.DeploymentAction))
        {
            message.Context.DeploymentAction = "Deploy";
        }

        var body = JsonSerializer.Serialize(message, JsonOptions);
        var outcome = await _dispatcher
            .ProcessAsync(body, messageId, deliveryCount, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var result = outcome.Result;
        if (result is null)
        {
            _logger.LogWarning(
                "{Agent} no result for {MessageId}: kind={Kind} reason={Reason}",
                AgentName,
                messageId,
                outcome.Kind,
                outcome.DeadLetterReason);
            return;
        }

        await CallbackBuildGenixAsync(message, result, cancellationToken).ConfigureAwait(false);

        if (!string.Equals(result.Status, "Completed", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "{Agent} stage {Stage} status={Status} — chain stops.",
                AgentName,
                message.Stage,
                result.Status);
            return;
        }

        if (!message.Context.AutoApprove)
        {
            _logger.LogInformation(
                "{Agent} AutoApprove=false after {Stage}; waiting for BuildGenix HITL to enqueue next.",
                AgentName,
                message.Stage);
            return;
        }

        // RouteBack: TA → IA (new revision), not Failed.
        if (string.Equals(message.Stage, PipelineAgentConstants.StageTaVerify, StringComparison.OrdinalIgnoreCase)
            && StageProgression.IsRouteBack(result))
        {
            var count = message.Context.RouteBackCount + 1;
            if (count > 2)
            {
                _logger.LogError("{Agent} RouteBack exhausted after {Count} attempts.", AgentName, count);
                await CallbackBuildGenixAsync(
                        message,
                        FailTerminal(message, $"TA RouteBack exhausted after {count} attempts."),
                        cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            var rb = StageProgression.BuildRouteBackMessage(message, result);
            rb.Context.RouteBackCount = count;
            await _bus.PublishAsync(AgentTopics.Ia, rb, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "{Agent} RouteBack → IA_CODEGEN rev=v{Rev} attempt={Attempt}",
                AgentName,
                rb.OutputRevision,
                count);
            return;
        }

        var nextStage = StageProgression.NextStageAfterSuccess(message.Stage);
        if (nextStage is null)
        {
            _logger.LogInformation("{Agent} terminal stage {Stage} complete.", AgentName, message.Stage);
            return;
        }

        var next = StageProgression.BuildNextMessage(message, result, nextStage);
        var topic = AgentTopics.TopicForStage(nextStage);
        await _bus.PublishAsync(topic, next, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "{Agent} chained {From} → {To} topic={Topic}",
            AgentName,
            message.Stage,
            nextStage,
            topic);
    }

    private async Task CallbackBuildGenixAsync(
        TaskMessage message,
        AgentResult result,
        CancellationToken cancellationToken)
    {
        var baseUrl = Environment.GetEnvironmentVariable("BUILGENIX_CALLBACK_BASE_URL")
                      ?? Environment.GetEnvironmentVariable("BUILDGENIX_CALLBACK_BASE_URL")
                      ?? "http://127.0.0.1:43181";
        try
        {
            var client = _httpClientFactory.CreateClient("buildgenix-callback");
            client.BaseAddress ??= new Uri(baseUrl.TrimEnd('/') + "/");
            using var response = await client
                .PostAsJsonAsync(
                    "api/poc/pipeline/agent-callback",
                    new
                    {
                        workItemId = message.WorkItemId,
                        projectId = message.ProjectId,
                        stage = message.Stage,
                        taskId = message.TaskId,
                        status = result.Status,
                        errorMessage = result.ErrorMessage,
                        artifacts = result.Artifacts,
                        outputs = result.Outputs,
                        steps = result.Steps,
                        outputRevision = message.OutputRevision,
                        routeBack = StageProgression.IsRouteBack(result),
                        agentName = AgentName
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogWarning(
                    "{Agent} BuildGenix callback HTTP {Status}: {Body}",
                    AgentName,
                    (int)response.StatusCode,
                    body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Agent} BuildGenix callback failed (non-fatal for chain).", AgentName);
        }
    }

    private static AgentResult FailTerminal(TaskMessage message, string error) =>
        new()
        {
            WorkItemId = message.WorkItemId,
            Stage = message.Stage,
            TaskId = message.TaskId,
            Status = AgentResultStatus.Failed,
            ErrorMessage = error
        };
}
