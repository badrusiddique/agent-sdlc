using System.Text.Json;
using AgentHub.Agents.Pipeline;
using AgentHub.Core.Models;
using AgentHub.Infrastructure.Messaging;
using AgentHub.Infrastructure.Options;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentHub.Hosting;

/// <summary>BackgroundService host for the POC pipeline walker (Healthy-shaped).</summary>
public sealed class PipelineAgentWorker : BackgroundService
{
    private readonly PipelineAgentDispatcher _dispatcher;
    private readonly ServiceBusOptions _serviceBus;
    private readonly PipelineWorkerOptions _worker;
    private readonly ILogger<PipelineAgentWorker> _logger;
    private ServiceBusClient? _client;
    private ServiceBusProcessor? _processor;

    public PipelineAgentWorker(
        PipelineAgentDispatcher dispatcher,
        IOptions<ServiceBusOptions> serviceBus,
        IOptions<PipelineWorkerOptions> worker,
        ILogger<PipelineAgentWorker> logger)
    {
        _dispatcher = dispatcher;
        _serviceBus = serviceBus.Value;
        _worker = worker.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_worker.Enabled || _worker.InProcessOnly)
        {
            _logger.LogInformation(
                "PIPELINE worker idle (Enabled={Enabled}, InProcessOnly={InProcessOnly}).",
                _worker.Enabled,
                _worker.InProcessOnly);
            return;
        }

        if (string.IsNullOrWhiteSpace(_serviceBus.ConnectionString))
        {
            _logger.LogError("PIPELINE worker cannot start: ServiceBus:ConnectionString missing.");
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
                _logger.LogError(ex, "PIPELINE worker faulted; host stays up. Restarting in 5s.");
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
            : TimeSpan.FromMinutes(30);

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
        _processor.ProcessErrorAsync += args =>
        {
            _logger.LogError(args.Exception, "PIPELINE Service Bus processor error");
            return Task.CompletedTask;
        };

        await _processor.StartProcessingAsync(stoppingToken).ConfigureAwait(false);
        _logger.LogInformation(
            "PIPELINE worker listening on {Topic}/{Subscription}",
            _worker.Topic,
            _worker.Subscription);
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        var body = args.Message.Body.ToString();
        var messageId = args.Message.MessageId;
        if (string.IsNullOrWhiteSpace(messageId))
        {
            try
            {
                var task = JsonSerializer.Deserialize<TaskMessage>(
                    body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                messageId = task?.LogicalMessageId ?? string.Empty;
            }
            catch
            {
                messageId = string.Empty;
            }
        }

        if (string.IsNullOrWhiteSpace(messageId))
        {
            await args.DeadLetterMessageAsync(args.Message, "MissingMessageId", cancellationToken: args.CancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var outcome = await _dispatcher
            .ProcessAsync(body, messageId, (int)args.Message.DeliveryCount, _worker.MaxDeliveryCount, args.CancellationToken)
            .ConfigureAwait(false);

        switch (outcome.Kind)
        {
            case PipelineDispatchKind.DeadLetter:
                await args.DeadLetterMessageAsync(
                        args.Message,
                        "PipelineDeadLetter",
                        outcome.DeadLetterReason,
                        args.CancellationToken)
                    .ConfigureAwait(false);
                break;
            case PipelineDispatchKind.Abandon:
                await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken)
                    .ConfigureAwait(false);
                break;
            default:
                await args.CompleteMessageAsync(args.Message, args.CancellationToken).ConfigureAwait(false);
                break;
        }
    }

    private async Task DisposeProcessorAsync()
    {
        if (_processor is not null)
        {
            try { await _processor.StopProcessingAsync().ConfigureAwait(false); }
            catch { /* best effort */ }
            await _processor.DisposeAsync().ConfigureAwait(false);
            _processor = null;
        }

        if (_client is not null)
        {
            await _client.DisposeAsync().ConfigureAwait(false);
            _client = null;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await DisposeProcessorAsync().ConfigureAwait(false);
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}
