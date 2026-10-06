namespace AgentHub.WebApi.Llm;

public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    /// <summary>Fixture | Ollama | Gateway</summary>
    public string Provider { get; set; } = "Fixture";

    public OllamaLlmOptions Ollama { get; set; } = new();

    public GatewayLlmOptions Gateway { get; set; } = new();
}

public sealed class OllamaLlmOptions
{
    public string BaseUrl { get; set; } = "http://127.0.0.1:11434";
    public string DefaultModel { get; set; } = "llama3.2";
}

public sealed class GatewayLlmOptions
{
    public string ChatEndpoint { get; set; } =
        "https://cc-sb-ts-aigw-apim.azure-api.net/ai/chat/completions";

    public string DefaultModelAlias { get; set; } = "openai";

    /// <summary>Env var or Key Vault injection supplies the value; never commit secrets.</summary>
    public string? SubscriptionKey { get; set; }
}
