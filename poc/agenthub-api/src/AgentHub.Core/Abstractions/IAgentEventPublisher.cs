namespace AgentHub.Core.Abstractions;

public interface IAgentEventPublisher
{
    Task PublishEventAsync(AgentPipelineEvent pipelineEvent, CancellationToken cancellationToken = default);
}

public sealed class AgentPipelineEvent
{
    public string EventId { get; set; } = string.Empty;
    public string WorkItemId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
    public string Kind { get; set; } = "LogLine";
    public string Message { get; set; } = string.Empty;
    public string Level { get; set; } = "info";
    public string? CorrelationId { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Status { get; set; }
}
