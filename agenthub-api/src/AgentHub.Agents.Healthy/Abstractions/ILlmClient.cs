namespace AgentHub.Agents.Healthy.Abstractions;

public interface ILlmClient
{
    string ProviderName { get; }

    Task<LlmCompletionResult> CompleteAsync(
        LlmCompletionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record LlmCompletionRequest(
    string SystemPrompt,
    string UserPrompt,
    string? ModelId,
    string? ReasoningEffort);

public sealed record LlmCompletionResult(
    string Text,
    string ModelId,
    int InputTokens,
    int OutputTokens);
