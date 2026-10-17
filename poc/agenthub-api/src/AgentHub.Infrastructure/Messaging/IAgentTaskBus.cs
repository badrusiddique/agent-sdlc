using AgentHub.Core.Models;

namespace AgentHub.Infrastructure.Messaging;

/// <summary>
/// Topic bus between agents. Development uses in-memory; production uses Azure Service Bus.
/// </summary>
public interface IAgentTaskBus
{
    Task PublishAsync(string topic, TaskMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribe a handler for a topic. Returns an unsubscribe/dispose action.
    /// Multiple workers may subscribe; each message is delivered to one subscriber (competing consumer).
    /// </summary>
    IAsyncDisposable Subscribe(
        string topic,
        string subscriptionName,
        Func<TaskMessage, string, int, CancellationToken, Task> handler);
}

public sealed class AgentBusOptions
{
    public const string SectionName = "Workers:AgentBus";

    /// <summary>
    /// When true (default in Development), use InMemoryAgentTaskBus so local Azurite E2E needs no SB emulator.
    /// When false and ServiceBus:ConnectionString is set, use Azure Service Bus topics.
    /// </summary>
    public bool UseInMemory { get; set; } = true;
}
