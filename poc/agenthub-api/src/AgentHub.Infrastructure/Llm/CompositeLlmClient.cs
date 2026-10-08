using AgentHub.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace AgentHub.Infrastructure.Llm;

/// <summary>
/// Routes LLM calls across Ollama and AI Gateway (and Fixture) that are registered
/// alongside each other. Explicit ModelId prefixes win; otherwise primary then fallback.
/// </summary>
public sealed class CompositeLlmClient : ILlmClient
{
    public const string OllamaPrefix = "ollama:";
    public const string GatewayPrefix = "gateway:";

    private readonly IReadOnlyDictionary<string, ILlmClient> _clients;
    private readonly string _primary;
    private readonly string? _fallback;
    private readonly ILogger<CompositeLlmClient> _logger;

    public CompositeLlmClient(
        IReadOnlyDictionary<string, ILlmClient> clients,
        string primaryProvider,
        string? fallbackProvider,
        ILogger<CompositeLlmClient> logger)
    {
        _clients = clients;
        _primary = Normalize(primaryProvider);
        _fallback = string.IsNullOrWhiteSpace(fallbackProvider) ? null : Normalize(fallbackProvider);
        _logger = logger;

        if (!_clients.ContainsKey(_primary))
        {
            throw new InvalidOperationException($"Primary LLM provider '{_primary}' is not registered.");
        }

        if (_fallback is not null && !_clients.ContainsKey(_fallback))
        {
            throw new InvalidOperationException($"Fallback LLM provider '{_fallback}' is not registered.");
        }
    }

    public string ProviderName =>
        _fallback is null ? _primary : $"{_primary}+{_fallback}";

    public async Task<LlmCompletionResult> CompleteAsync(
        LlmCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        var (forcedProvider, strippedModel) = ParseRoute(request.ModelId);
        var effectiveRequest = strippedModel == request.ModelId
            ? request
            : request with { ModelId = strippedModel };

        if (forcedProvider is not null)
        {
            if (!_clients.TryGetValue(forcedProvider, out var forced))
            {
                throw new InvalidOperationException(
                    $"ModelId requested provider '{forcedProvider}' but it is not configured.");
            }

            return await forced.CompleteAsync(effectiveRequest, cancellationToken).ConfigureAwait(false);
        }

        var primary = _clients[_primary];
        try
        {
            return await primary.CompleteAsync(effectiveRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (_fallback is not null && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                ex,
                "Primary LLM provider {Primary} failed; trying fallback {Fallback}.",
                _primary,
                _fallback);
            return await _clients[_fallback].CompleteAsync(effectiveRequest, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    internal static (string? Provider, string? ModelId) ParseRoute(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return (null, modelId);
        }

        if (modelId.StartsWith(OllamaPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return ("ollama", modelId[OllamaPrefix.Length..]);
        }

        if (modelId.StartsWith(GatewayPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return ("gateway", modelId[GatewayPrefix.Length..]);
        }

        return (null, modelId);
    }

    private static string Normalize(string provider) => provider.Trim().ToLowerInvariant();
}
