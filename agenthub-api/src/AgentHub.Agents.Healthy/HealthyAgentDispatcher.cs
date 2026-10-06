using System.Text.Json;
using AgentHub.Core.Abstractions;
using AgentHub.Core.Guardrails;
using AgentHub.Core.Models;
using Microsoft.Extensions.Logging;

namespace AgentHub.Agents.Healthy;

/// <summary>
/// Host-neutral dispatcher: dedup check → handler → publish result → mark (D-C1).
/// Host completes the Service Bus message after this returns Handled / AlreadyProcessed.
/// </summary>
public sealed class HealthyAgentDispatcher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HealthyAgentHandler _handler;
    private readonly IAgentResultPublisher _results;
    private readonly IMessageDedup _dedup;
    private readonly ILogger<HealthyAgentDispatcher> _logger;

    public HealthyAgentDispatcher(
        HealthyAgentHandler handler,
        IAgentResultPublisher results,
        IMessageDedup dedup,
        ILogger<HealthyAgentDispatcher> logger)
    {
        _handler = handler;
        _results = results;
        _dedup = dedup;
        _logger = logger;
    }

    public async Task<HealthyDispatchOutcome> ProcessAsync(
        string body,
        string serviceBusMessageId,
        int deliveryCount,
        int maxDeliveryCount = HealthyAgentConstants.DefaultMaxDeliveryCount,
        CancellationToken cancellationToken = default)
    {
        if (maxDeliveryCount < 1)
        {
            maxDeliveryCount = HealthyAgentConstants.DefaultMaxDeliveryCount;
        }

        TaskMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<TaskMessage>(body, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "HEALTHY malformed TaskMessage; dead-letter candidate");
            return HealthyDispatchOutcome.DeadLetter("Malformed TaskMessage JSON.");
        }

        if (message is null)
        {
            return HealthyDispatchOutcome.DeadLetter("Empty TaskMessage body.");
        }

        message.EnsureDefaults();
        var validation = HealthyTaskValidation.Validate(message);
        if (validation.Count > 0)
        {
            return HealthyDispatchOutcome.DeadLetter(string.Join(" ", validation));
        }

        var expectedId = message.LogicalMessageId;
        if (!string.Equals(serviceBusMessageId, expectedId, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "HEALTHY MessageId mismatch. ServiceBus={SbId} Expected={Expected}. Continuing with Service Bus id for dedup RK.",
                serviceBusMessageId,
                expectedId);
        }

        if (await _dedup.IsProcessedAsync(serviceBusMessageId, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogInformation(
                "HEALTHY message {MessageId} already processed; complete no-op",
                serviceBusMessageId);
            return HealthyDispatchOutcome.AlreadyProcessed();
        }

        AgentResult result;
        try
        {
            result = await _handler.RunAsync(message, deliveryCount, cancellationToken).ConfigureAwait(false);
        }
        catch (GuardrailBlockedException gex)
        {
            // Policy block will not succeed on retry — fail once, publish, mark, dead-letter.
            _logger.LogWarning(
                gex,
                "HEALTHY guardrail blocked {MessageId} CorrelationId={CorrelationId} codes={Codes}",
                serviceBusMessageId,
                message.CorrelationId,
                string.Join(',', gex.Verdict.ReasonCodes));

            result = new AgentResult
            {
                WorkItemId = message.WorkItemId,
                TaskId = message.TaskId,
                Stage = HealthyAgentConstants.StageCode,
                Status = AgentResultStatus.Failed,
                CorrelationId = message.CorrelationId,
                InputVersion = message.InputVersion,
                RunId = HealthyAgentConstants.RunId(message.TaskId, message.InputVersion, deliveryCount),
                StateContextId = message.StateContextId,
                ErrorMessage = Truncate(
                    $"GuardrailBlocked:{string.Join(',', gex.Verdict.ReasonCodes)}",
                    2000)
            };

            await _results.PublishResultAsync(result, cancellationToken).ConfigureAwait(false);
            await _dedup.TryMarkProcessedAsync(serviceBusMessageId, cancellationToken).ConfigureAwait(false);
            return HealthyDispatchOutcome.DeadLetter(
                result.ErrorMessage ?? "HEALTHY guardrail blocked.",
                result);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "HEALTHY handler failed for {MessageId} DeliveryCount={DeliveryCount}/{MaxDelivery}",
                serviceBusMessageId,
                deliveryCount,
                maxDeliveryCount);

            if (deliveryCount < maxDeliveryCount)
            {
                return HealthyDispatchOutcome.Abandon(
                    $"Handler fault (delivery {deliveryCount}/{maxDeliveryCount}): {ex.GetType().Name}");
            }

            result = new AgentResult
            {
                WorkItemId = message.WorkItemId,
                TaskId = message.TaskId,
                Stage = HealthyAgentConstants.StageCode,
                Status = AgentResultStatus.Failed,
                CorrelationId = message.CorrelationId,
                InputVersion = message.InputVersion,
                RunId = HealthyAgentConstants.RunId(message.TaskId, message.InputVersion, deliveryCount),
                StateContextId = message.StateContextId,
                ErrorMessage = Truncate(
                    $"HEALTHY failed after {deliveryCount} deliveries: {ex.GetType().Name}: {ex.Message}",
                    2000)
            };

            await _results.PublishResultAsync(result, cancellationToken).ConfigureAwait(false);
            await _dedup.TryMarkProcessedAsync(serviceBusMessageId, cancellationToken).ConfigureAwait(false);
            return HealthyDispatchOutcome.DeadLetter(
                result.ErrorMessage ?? "HEALTHY failed on last delivery.",
                result);
        }

        await _results.PublishResultAsync(result, cancellationToken).ConfigureAwait(false);
        await _dedup.TryMarkProcessedAsync(serviceBusMessageId, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "HEALTHY D-C1 order complete for {MessageId}: published Status={Status} then marked AgProcessedEvents",
            serviceBusMessageId,
            result.Status);

        return HealthyDispatchOutcome.Handled(result);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}

public sealed class HealthyDispatchOutcome
{
    private HealthyDispatchOutcome(
        HealthyDispatchKind kind,
        AgentResult? result,
        string? deadLetterReason)
    {
        Kind = kind;
        Result = result;
        DeadLetterReason = deadLetterReason;
    }

    public HealthyDispatchKind Kind { get; }
    public AgentResult? Result { get; }
    public string? DeadLetterReason { get; }

    public static HealthyDispatchOutcome Handled(AgentResult result) =>
        new(HealthyDispatchKind.Handled, result, null);

    public static HealthyDispatchOutcome AlreadyProcessed() =>
        new(HealthyDispatchKind.AlreadyProcessed, null, null);

    public static HealthyDispatchOutcome Abandon(string reason) =>
        new(HealthyDispatchKind.Abandon, null, reason);

    public static HealthyDispatchOutcome DeadLetter(string reason, AgentResult? result = null) =>
        new(HealthyDispatchKind.DeadLetter, result, reason);
}

public enum HealthyDispatchKind
{
    Handled,
    AlreadyProcessed,
    Abandon,
    DeadLetter
}
