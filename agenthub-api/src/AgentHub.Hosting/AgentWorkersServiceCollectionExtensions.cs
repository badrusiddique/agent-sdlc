using AgentHub.Agents.Healthy;
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
    /// Registers Phase 0 BackgroundService workers (D-AS-1). Currently HEALTHY harness only.
    /// </summary>
    public static IServiceCollection AddAgentWorkers(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<HealthyWorkerOptions>(
            configuration.GetSection(HealthyWorkerOptions.SectionName));

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
        return services;
    }
}
