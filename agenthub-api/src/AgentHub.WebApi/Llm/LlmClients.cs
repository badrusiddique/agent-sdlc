using AgentHub.Agents.Healthy.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentHub.WebApi.Llm;

public sealed class FixtureLlmClient : ILlmClient
{
    public string ProviderName => "Fixture";

    public Task<LlmCompletionResult> CompleteAsync(
        LlmCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        var model = string.IsNullOrWhiteSpace(request.ModelId) ? "fixture-healthy" : request.ModelId;
        var text =
            "Thought of the day: a healthy agent is one that publishes before it marks. " +
            "(Fixture LLM — deterministic.)";
        return Task.FromResult(new LlmCompletionResult(text, model, InputTokens: 12, OutputTokens: 28));
    }
}

public sealed class OllamaLlmClient : ILlmClient
{
    private readonly HttpClient _http;
    private readonly OllamaLlmOptions _options;

    public OllamaLlmClient(HttpClient http, IOptions<LlmOptions> options)
    {
        _http = http;
        _options = options.Value.Ollama;
    }

    public string ProviderName => "Ollama";

    public async Task<LlmCompletionResult> CompleteAsync(
        LlmCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        var model = string.IsNullOrWhiteSpace(request.ModelId) ? _options.DefaultModel : request.ModelId!;
        var payload = new
        {
            model,
            stream = false,
            messages = new object[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user", content = request.UserPrompt }
            }
        };

        using var response = await _http
            .PostAsJsonAsync("/api/chat", payload, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var doc = await response.Content
            .ReadFromJsonAsync<OllamaChatResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var text = doc?.Message?.Content?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("Ollama returned an empty message.");
        }

        return new LlmCompletionResult(text, model, InputTokens: 0, OutputTokens: 0);
    }

    private sealed class OllamaChatResponse
    {
        public OllamaMessage? Message { get; set; }
    }

    private sealed class OllamaMessage
    {
        public string? Content { get; set; }
    }
}

public sealed class GatewayLlmClient : ILlmClient
{
    private readonly HttpClient _http;
    private readonly GatewayLlmOptions _options;

    public GatewayLlmClient(HttpClient http, IOptions<LlmOptions> options)
    {
        _http = http;
        _options = options.Value.Gateway;
    }

    public string ProviderName => "Gateway";

    public async Task<LlmCompletionResult> CompleteAsync(
        LlmCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        var model = string.IsNullOrWhiteSpace(request.ModelId)
            ? _options.DefaultModelAlias
            : request.ModelId!;

        var key = _options.SubscriptionKey
                  ?? Environment.GetEnvironmentVariable("AIGW_SUBSCRIPTION_KEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException(
                "Gateway LLM requires Llm:Gateway:SubscriptionKey or AIGW_SUBSCRIPTION_KEY.");
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _options.ChatEndpoint);
        httpRequest.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Key", key);
        httpRequest.Headers.TryAddWithoutValidation("X-AIGW-Model", model);
        httpRequest.Content = JsonContent.Create(new
        {
            messages = new object[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user", content = request.UserPrompt }
            },
            max_tokens = 256
        });

        using var response = await _http.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"AI Gateway chat failed HTTP {(int)response.StatusCode}: {Truncate(raw, 500)}");
        }

        using var doc = System.Text.Json.JsonDocument.Parse(raw);
        var text = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString()
            ?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("AI Gateway returned an empty completion.");
        }

        return new LlmCompletionResult(text, model, InputTokens: 0, OutputTokens: 0);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}

public static class LlmServiceCollectionExtensions
{
    public static IServiceCollection AddAgentHubLlm(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<LlmOptions>(configuration.GetSection(LlmOptions.SectionName));

        services.AddHttpClient("ollama", (sp, client) =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LlmOptions>>().Value;
            client.BaseAddress = new Uri(opts.Ollama.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromMinutes(2);
        });

        services.AddHttpClient("aigw", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(2);
        });

        services.AddSingleton<ILlmClient>(sp =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LlmOptions>>().Value;
            return opts.Provider.Trim().ToLowerInvariant() switch
            {
                "ollama" => new OllamaLlmClient(
                    sp.GetRequiredService<IHttpClientFactory>().CreateClient("ollama"),
                    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LlmOptions>>()),
                "gateway" => new GatewayLlmClient(
                    sp.GetRequiredService<IHttpClientFactory>().CreateClient("aigw"),
                    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LlmOptions>>()),
                _ => new FixtureLlmClient()
            };
        });

        return services;
    }
}
