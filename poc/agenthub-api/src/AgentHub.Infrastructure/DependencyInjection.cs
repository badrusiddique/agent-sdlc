using AgentHub.Core.Abstractions;
using AgentHub.Infrastructure.Blob;
using AgentHub.Infrastructure.Llm;
using AgentHub.Infrastructure.Messaging;
using AgentHub.Infrastructure.Options;
using AgentHub.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgentHub.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAgentHubInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ServiceBusOptions>(configuration.GetSection(ServiceBusOptions.SectionName));
        services.Configure<BlobStorageOptions>(configuration.GetSection(BlobStorageOptions.SectionName));
        services.Configure<HealthyWorkerOptions>(configuration.GetSection(HealthyWorkerOptions.SectionName));
        services.Configure<PipelineWorkerOptions>(configuration.GetSection(PipelineWorkerOptions.SectionName));
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
            var http = sp.GetRequiredService<IHttpClientFactory>();
            var llmOpts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LlmOptions>>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CompositeLlmClient>>();

            var clients = new Dictionary<string, ILlmClient>(StringComparer.OrdinalIgnoreCase)
            {
                ["fixture"] = new FixtureLlmClient(),
                ["ollama"] = new OllamaLlmClient(http.CreateClient("ollama"), llmOpts),
                ["gateway"] = new GatewayLlmClient(http.CreateClient("aigw"), llmOpts)
            };

            var primary = string.IsNullOrWhiteSpace(opts.Provider) ? "fixture" : opts.Provider.Trim();
            var fallback = string.IsNullOrWhiteSpace(opts.FallbackProvider)
                ? null
                : opts.FallbackProvider.Trim();

            // Single-provider mode (unchanged behavior) when no fallback is configured.
            if (fallback is null)
            {
                return primary.ToLowerInvariant() switch
                {
                    "ollama" => clients["ollama"],
                    "gateway" => clients["gateway"],
                    _ => clients["fixture"]
                };
            }

            // Alongside mode: both (or all) providers registered; route + fallback.
            return new CompositeLlmClient(clients, primary, fallback, logger);
        });

        services.AddSingleton<IArtifactBlobStore>(sp =>
        {
            var blob = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<BlobStorageOptions>>().Value;
            if (blob.Provider.Equals("File", StringComparison.OrdinalIgnoreCase))
            {
                return new FileArtifactBlobStore(
                    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<BlobStorageOptions>>(),
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<FileArtifactBlobStore>>());
            }

            return new AzureArtifactBlobStore(
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<BlobStorageOptions>>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AzureArtifactBlobStore>>());
        });

        var tableProvider = configuration.GetSection("TableStorage")["Provider"] ?? "Azure";
        if (tableProvider.Equals("Memory", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IEffectLedger, Persistence.InMemoryEffectLedger>();
            services.AddSingleton<IActivityLog, Persistence.InMemoryActivityLog>();
            services.AddSingleton<IMessageDedup, Persistence.InMemoryMessageDedup>();
        }
        else
        {
            services.AddSingleton<IEffectLedger, EffectLedgerAdapter>();
            services.AddSingleton<IActivityLog, ActivityLogAdapter>();
            services.AddSingleton<IMessageDedup, MessageDedupAdapter>();
        }
        services.AddSingleton<IHealthyTaskPublisher, ServiceBusHealthyTaskPublisher>();
        services.AddSingleton<IAgentResultPublisher, PipelineAwareAgentResultPublisher>();
        services.AddSingleton<IAgentEventPublisher, LoggingAgentEventPublisher>();
        services.AddSingleton<IPipelineTaskPublisher>(sp =>
        {
            var pipeline = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PipelineWorkerOptions>>().Value;
            if (pipeline.InProcessOnly)
            {
                return new NoOpPipelineTaskPublisher();
            }

            return new ServiceBusPipelineTaskPublisher(
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ServiceBusOptions>>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PipelineWorkerOptions>>());
        });

        return services;
    }
}
