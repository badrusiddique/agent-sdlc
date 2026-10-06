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

        services.AddSingleton<IArtifactBlobStore, AzureArtifactBlobStore>();
        services.AddSingleton<IEffectLedger, EffectLedgerAdapter>();
        services.AddSingleton<IActivityLog, ActivityLogAdapter>();
        services.AddSingleton<IMessageDedup, MessageDedupAdapter>();
        services.AddSingleton<IHealthyTaskPublisher, ServiceBusHealthyTaskPublisher>();
        services.AddSingleton<IAgentResultPublisher, ServiceBusAgentResultPublisher>();

        return services;
    }
}
