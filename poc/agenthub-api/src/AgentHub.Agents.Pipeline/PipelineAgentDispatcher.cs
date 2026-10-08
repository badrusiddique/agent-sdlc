using System.Text.Json;
using AgentHub.Core.Abstractions;
using AgentHub.Core.Guardrails;
using AgentHub.Core.Models;
using Microsoft.Extensions.Logging;

namespace AgentHub.Agents.Pipeline;

/// <summary>
/// Host-neutral dispatcher: dedup check → handler → publish result → mark (D-C1).
/// Same order as HealthyAgentDispatcher.
/// </summary>
public sealed class PipelineAgentDispatcher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly PipelineAgentHandler _handler;
    private readonly IAgentResultPublisher _results;
    private readonly IMessageDedup _dedup;
    private readonly ILogger<PipelineAgentDispatcher> _logger;

    public PipelineAgentDispatcher(
        PipelineAgentHandler handler,
        IAgentResultPublisher results,
        IMessageDedup dedup,
        ILogger<PipelineAgentDispatcher> logger)
    {
        _handler = handler;
        _results = results;
        _dedup = dedup;
        _logger = logger;
    }

    public async Task<PipelineDispatchOutcome> ProcessAsync(
        string body,
        string serviceBusMessageId,
        int deliveryCount,
        int maxDeliveryCount = PipelineAgentConstants.DefaultMaxDeliveryCount,
        CancellationToken cancellationToken = default)
    {
        if (maxDeliveryCount < 1)
        {
            maxDeliveryCount = PipelineAgentConstants.DefaultMaxDeliveryCount;
        }

        TaskMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<TaskMessage>(body, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "PIPELINE malformed TaskMessage; dead-letter candidate");
            return PipelineDispatchOutcome.DeadLetter("Malformed TaskMessage JSON.");
        }

        if (message is null)
        {
            return PipelineDispatchOutcome.DeadLetter("Empty TaskMessage body.");
        }

        message.EnsureDefaults();
        var validation = PipelineTaskValidation.Validate(message);
        if (validation.Count > 0)
        {
            return PipelineDispatchOutcome.DeadLetter(string.Join(" ", validation));
        }

        if (await _dedup.IsProcessedAsync(serviceBusMessageId, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogInformation(
                "PIPELINE message {MessageId} already processed; complete no-op",
                serviceBusMessageId);
            return PipelineDispatchOutcome.AlreadyProcessed();
        }

        AgentResult result;
        try
        {
            result = await _handler.RunAsync(message, deliveryCount, cancellationToken).ConfigureAwait(false);
        }
        catch (GuardrailBlockedException gex)
        {
            result = Fail(message, deliveryCount, $"GuardrailBlocked:{string.Join(',', gex.Verdict.ReasonCodes)}");
            await _results.PublishResultAsync(result, cancellationToken).ConfigureAwait(false);
            await _dedup.TryMarkProcessedAsync(serviceBusMessageId, cancellationToken).ConfigureAwait(false);
            return PipelineDispatchOutcome.DeadLetter(result.ErrorMessage ?? "PIPELINE guardrail blocked.", result);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "PIPELINE handler failed for {MessageId} DeliveryCount={DeliveryCount}/{MaxDelivery}",
                serviceBusMessageId,
                deliveryCount,
                maxDeliveryCount);

            if (deliveryCount < maxDeliveryCount)
            {
                return PipelineDispatchOutcome.Abandon(
                    $"Handler fault (delivery {deliveryCount}/{maxDeliveryCount}): {ex.GetType().Name}");
            }

            result = Fail(
                message,
                deliveryCount,
                $"PIPELINE failed after {deliveryCount} deliveries: {ex.GetType().Name}: {ex.Message}");
            await _results.PublishResultAsync(result, cancellationToken).ConfigureAwait(false);
            await _dedup.TryMarkProcessedAsync(serviceBusMessageId, cancellationToken).ConfigureAwait(false);
            return PipelineDispatchOutcome.DeadLetter(result.ErrorMessage ?? "PIPELINE failed on last delivery.", result);
        }

        await _results.PublishResultAsync(result, cancellationToken).ConfigureAwait(false);
        await _dedup.TryMarkProcessedAsync(serviceBusMessageId, cancellationToken).ConfigureAwait(false);
        return PipelineDispatchOutcome.Handled(result);
    }

    /// <summary>In-process path used by the POC orchestrator when Service Bus is not available.</summary>
    public Task<PipelineDispatchOutcome> ProcessMessageAsync(
        TaskMessage message,
        int deliveryCount = 1,
        CancellationToken cancellationToken = default)
    {
        message.EnsureDefaults();
        var id = message.LogicalMessageId;
        var body = JsonSerializer.Serialize(message, JsonOptions);
        return ProcessAsync(body, id, deliveryCount, cancellationToken: cancellationToken);
    }

    private static AgentResult Fail(TaskMessage message, int deliveryCount, string error) =>
        new()
        {
            WorkItemId = message.WorkItemId,
            TaskId = message.TaskId,
            Stage = message.Stage,
            Status = AgentResultStatus.Failed,
            CorrelationId = message.CorrelationId,
            InputVersion = message.InputVersion,
            RunId = PipelineAgentConstants.RunId(message.TaskId, message.InputVersion, deliveryCount),
            StateContextId = message.StateContextId,
            ErrorMessage = error.Length <= 2000 ? error : error[..2000]
        };
}

public sealed class PipelineDispatchOutcome
{
    private PipelineDispatchOutcome(
        PipelineDispatchKind kind,
        AgentResult? result,
        string? deadLetterReason)
    {
        Kind = kind;
        Result = result;
        DeadLetterReason = deadLetterReason;
    }

    public PipelineDispatchKind Kind { get; }
    public AgentResult? Result { get; }
    public string? DeadLetterReason { get; }

    public static PipelineDispatchOutcome Handled(AgentResult result) =>
        new(PipelineDispatchKind.Handled, result, null);

    public static PipelineDispatchOutcome AlreadyProcessed() =>
        new(PipelineDispatchKind.AlreadyProcessed, null, null);

    public static PipelineDispatchOutcome Abandon(string reason) =>
        new(PipelineDispatchKind.Abandon, null, reason);

    public static PipelineDispatchOutcome DeadLetter(string reason, AgentResult? result = null) =>
        new(PipelineDispatchKind.DeadLetter, result, reason);
}

public enum PipelineDispatchKind
{
    Handled,
    AlreadyProcessed,
    Abandon,
    DeadLetter
}
