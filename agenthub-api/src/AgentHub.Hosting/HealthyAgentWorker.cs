using System.Text.Json;
using AgentHub.Agents.Healthy;
using AgentHub.Core.Models;
using AgentHub.Infrastructure.Messaging;
using AgentHub.Infrastructure.Options;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentHub.Hosting;

/// <summary>
/// BackgroundService host for HEALTHY. Exceptions are logged; the host is never killed by handler faults.
/// </summary>
public sealed class HealthyAgentWorker : BackgroundService
{
    private readonly HealthyAgentDispatcher _dispatcher;
    private readonly ServiceBusOptions _serviceBus;
    private readonly HealthyWorkerOptions _worker;
    private readonly ILogger<HealthyAgentWorker> _logger;
    private ServiceBusClient? _client;
    private ServiceBusProcessor? _processor;

    public HealthyAgentWorker(
        HealthyAgentDispatcher dispatcher,
        IOptions<ServiceBusOptions> serviceBus,
        IOptions<HealthyWorkerOptions> worker,
        ILogger<HealthyAgentWorker> logger)
    {
        _dispatcher = dispatcher;
        _serviceBus = serviceBus.Value;
        _worker = worker.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_worker.Enabled)
        {
            _logger.LogInformation("HEALTHY worker disabled by Workers:Healthy:Enabled=false.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_serviceBus.ConnectionString))
        {
            _logger.LogError("HEALTHY worker cannot start: ServiceBus:ConnectionString missing.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await StartProcessorAsync(stoppingToken).ConfigureAwait(false);
                await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "HEALTHY worker faulted; host stays up. Restarting in 5s.");
                await DisposeProcessorAsync().ConfigureAwait(false);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }

    private async Task StartProcessorAsync(CancellationToken stoppingToken)
    {
        await DisposeProcessorAsync().ConfigureAwait(false);

        _client = new ServiceBusClient(_serviceBus.ConnectionString);
        var lockRenewal = TimeSpan.TryParse(_worker.MaxAutoLockRenewalDuration, out var parsed)
            ? parsed
            : TimeSpan.FromMinutes(2);

        _processor = _client.CreateProcessor(
            _worker.Topic,
            _worker.Subscription,
            new ServiceBusProcessorOptions
            {
                AutoCompleteMessages = false,
                MaxConcurrentCalls = Math.Max(1, _worker.MaxConcurrentCalls),
                PrefetchCount = 0,
                MaxAutoLockRenewalDuration = lockRenewal
            });

        _processor.ProcessMessageAsync += ProcessMessageAsync;
        _processor.ProcessErrorAsync += ProcessErrorAsync;

        await _processor.StartProcessingAsync(stoppingToken).ConfigureAwait(false);
        _logger.LogInformation(
            "HEALTHY worker listening on {Topic}/{Subscription} MaxDeliveryCount={MaxDelivery}",
            _worker.Topic,
            _worker.Subscription,
            _worker.MaxDeliveryCount);
    }

    private async Task DisposeProcessorAsync()
    {
        if (_processor is not null)
        {
            try
            {
                await _processor.StopProcessingAsync().ConfigureAwait(false);
            }
            catch
            {
                // best effort
            }

            await _processor.DisposeAsync().ConfigureAwait(false);
            _processor = null;
        }

        if (_client is not null)
        {
            await _client.DisposeAsync().ConfigureAwait(false);
            _client = null;
        }
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        var body = args.Message.Body.ToString();
        if (!TryResolveMessageId(args.Message, body, out var messageId, out var missingReason))
        {
            _logger.LogWarning(
                "HEALTHY message missing MessageId and LogicalMessageId; dead-lettering: {Reason}",
                missingReason);
            await args
                .DeadLetterMessageAsync(
                    args.Message,
                    deadLetterReason: "MissingMessageId",
                    deadLetterErrorDescription: Truncate(missingReason, 4096),
                    cancellationToken: args.CancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var deliveryCount = (int)args.Message.DeliveryCount;

        try
        {
            var outcome = await _dispatcher
                .ProcessAsync(
                    body,
                    messageId,
                    deliveryCount,
                    _worker.MaxDeliveryCount,
                    args.CancellationToken)
                .ConfigureAwait(false);

            switch (outcome.Kind)
            {
                case HealthyDispatchKind.DeadLetter:
                    _logger.LogWarning(
                        "HEALTHY dead-letter {MessageId}: {Reason}",
                        messageId,
                        outcome.DeadLetterReason);
                    await args
                        .DeadLetterMessageAsync(
                            args.Message,
                            deadLetterReason: "HealthyDeadLetter",
                            deadLetterErrorDescription: Truncate(outcome.DeadLetterReason, 4096),
                            cancellationToken: args.CancellationToken)
                        .ConfigureAwait(false);
                    break;

                case HealthyDispatchKind.Abandon:
                    _logger.LogWarning(
                        "HEALTHY abandon {MessageId}: {Reason}",
                        messageId,
                        outcome.DeadLetterReason);
                    await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken)
                        .ConfigureAwait(false);
                    break;

                case HealthyDispatchKind.AlreadyProcessed:
                case HealthyDispatchKind.Handled:
                    await args.CompleteMessageAsync(args.Message, args.CancellationToken)
                        .ConfigureAwait(false);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HEALTHY processing fault for {MessageId}; abandoning", messageId);
            try
            {
                await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception abandonEx)
            {
                _logger.LogError(abandonEx, "HEALTHY abandon failed for {MessageId}", messageId);
            }
        }
    }

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(
            args.Exception,
            "HEALTHY Service Bus processor error Source={ErrorSource} Entity={EntityPath}",
            args.ErrorSource,
            args.EntityPath);
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await DisposeProcessorAsync().ConfigureAwait(false);
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Dedup key must be stable across redeliveries: SB MessageId, else body LogicalMessageId.
    /// Never invent a fresh GUID (that breaks D-C1 on redelivery).
    /// </summary>
    internal static bool TryResolveMessageId(
        ServiceBusReceivedMessage message,
        string body,
        out string messageId,
        out string? reason)
    {
        if (!string.IsNullOrWhiteSpace(message.MessageId))
        {
            messageId = message.MessageId;
            reason = null;
            return true;
        }

        try
        {
            var task = JsonSerializer.Deserialize<TaskMessage>(
                body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (task is not null && !string.IsNullOrWhiteSpace(task.TaskId))
            {
                task.EnsureDefaults();
                messageId = task.LogicalMessageId;
                reason = null;
                return true;
            }
        }
        catch (JsonException)
        {
            // fall through
        }

        messageId = string.Empty;
        reason = "Service Bus MessageId empty and body has no TaskId/InputVersion for LogicalMessageId.";
        return false;
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
        {
            return value;
        }

        return value[..max];
    }
}
