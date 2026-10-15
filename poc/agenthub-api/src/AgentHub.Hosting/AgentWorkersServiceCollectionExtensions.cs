using AgentHub.Agents.Healthy;
using AgentHub.Agents.Pipeline;
using AgentHub.Agents.Pipeline.Ia;
using AgentHub.Core.Abstractions;
using AgentHub.Core.Guardrails;
using AgentHub.Infrastructure.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentHub.Hosting;

public static class AgentWorkersServiceCollectionExtensions
{
    /// <summary>
    /// Registers Phase 0 BackgroundService workers (D-AS-1): HEALTHY harness + POC Pipeline.
    /// </summary>
    public static IServiceCollection AddAgentWorkers(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<HealthyWorkerOptions>(
            configuration.GetSection(HealthyWorkerOptions.SectionName));
        services.Configure<PipelineWorkerOptions>(
            configuration.GetSection(PipelineWorkerOptions.SectionName));

        services.AddSingleton<IGuardrailPipeline, LocalGuardrailPipeline>();

        services.AddSingleton(sp =>
        {
            var pauseMs = sp.GetRequiredService<IOptions<HealthyWorkerOptions>>().Value.DeliberatePauseMs;
            return new HealthyAgentHandler(
                sp.GetRequiredService<ILlmClient>(),
                sp.GetRequiredService<IArtifactBlobStore>(),
                sp.GetRequiredService<IEffectLedger>(),
                sp.GetRequiredService<IActivityLog>(),
                sp.GetRequiredService<ILogger<HealthyAgentHandler>>(),
                TimeSpan.FromMilliseconds(Math.Max(0, pauseMs)),
                sp.GetRequiredService<IGuardrailPipeline>());
        });
        services.AddSingleton<HealthyAgentDispatcher>();
        services.AddHostedService<HealthyAgentWorker>();

        services.Configure<IaGitOptions>(configuration.GetSection(IaGitOptions.SectionName));
        services.AddSingleton(sp =>
        {
            var git = sp.GetService<IOptions<IaGitOptions>>()?.Value ?? new IaGitOptions();
            return new PipelineAgentHandler(
                sp.GetRequiredService<ILlmClient>(),
                sp.GetRequiredService<IArtifactBlobStore>(),
                sp.GetRequiredService<IEffectLedger>(),
                sp.GetRequiredService<IActivityLog>(),
                sp.GetRequiredService<IAgentEventPublisher>(),
                sp.GetRequiredService<ILogger<PipelineAgentHandler>>(),
                sp.GetRequiredService<IGuardrailPipeline>(),
                git);
        });
        services.AddSingleton<PipelineAgentDispatcher>();
        services.AddHostedService<PipelineAgentWorker>();

        return services;
    }
}
