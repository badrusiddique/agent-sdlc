using AgentHub.Core.Abstractions;
using AgentHub.Infrastructure.Llm;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentHub.WebApi.Tests;

public sealed class CompositeLlmClientTests
{
    [Theory]
    [InlineData(null, null, null)]
    [InlineData("openai", null, "openai")]
    [InlineData("ollama:llama3.2:1b", "ollama", "llama3.2:1b")]
    [InlineData("gateway:openai", "gateway", "openai")]
    [InlineData("GATEWAY:5.1", "gateway", "5.1")]
    public void ParseRoute_handles_prefixes(string? input, string? provider, string? model)
    {
        var (p, m) = CompositeLlmClient.ParseRoute(input);
        Assert.Equal(provider, p);
        Assert.Equal(model, m);
    }

    [Fact]
    public async Task Explicit_gateway_prefix_bypasses_primary()
    {
        var ollama = new RecordingLlm("ollama");
        var gateway = new RecordingLlm("gateway");
        var sut = new CompositeLlmClient(
            new Dictionary<string, ILlmClient>(StringComparer.OrdinalIgnoreCase)
            {
                ["ollama"] = ollama,
                ["gateway"] = gateway,
                ["fixture"] = new FixtureLlmClient()
            },
            primaryProvider: "ollama",
            fallbackProvider: "gateway",
            NullLogger<CompositeLlmClient>.Instance);

        var result = await sut.CompleteAsync(
            new LlmCompletionRequest("sys", "user", "gateway:openai", null));

        Assert.Equal("openai", result.ModelId);
        Assert.Equal(1, gateway.Calls);
        Assert.Equal(0, ollama.Calls);
        Assert.Equal("openai", gateway.LastModelId);
        Assert.Contains("gateway", result.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Primary_failure_uses_fallback()
    {
        var ollama = new RecordingLlm("ollama", fail: true);
        var gateway = new RecordingLlm("gateway");
        var sut = new CompositeLlmClient(
            new Dictionary<string, ILlmClient>(StringComparer.OrdinalIgnoreCase)
            {
                ["ollama"] = ollama,
                ["gateway"] = gateway,
                ["fixture"] = new FixtureLlmClient()
            },
            primaryProvider: "ollama",
            fallbackProvider: "gateway",
            NullLogger<CompositeLlmClient>.Instance);

        var result = await sut.CompleteAsync(
            new LlmCompletionRequest("sys", "user", null, null));

        Assert.Equal(1, ollama.Calls);
        Assert.Equal(1, gateway.Calls);
        Assert.Contains("gateway", result.Text, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class RecordingLlm : ILlmClient
    {
        private readonly bool _fail;

        public RecordingLlm(string name, bool fail = false)
        {
            ProviderName = name;
            _fail = fail;
        }

        public string ProviderName { get; }
        public int Calls { get; private set; }
        public string? LastModelId { get; private set; }

        public Task<LlmCompletionResult> CompleteAsync(
            LlmCompletionRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastModelId = request.ModelId;
            if (_fail)
            {
                throw new InvalidOperationException($"{ProviderName} down");
            }

            return Task.FromResult(
                new LlmCompletionResult($"ok-from-{ProviderName}", request.ModelId ?? ProviderName, 1, 1));
        }
    }
}
