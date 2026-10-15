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
        services.Configure<IaDeployOptions>(configuration.GetSection(IaDeployOptions.SectionName));
        services.AddSingleton(sp =>
        {
            var git = sp.GetService<IOptions<IaGitOptions>>()?.Value ?? new IaGitOptions();
            var deploy = sp.GetService<IOptions<IaDeployOptions>>()?.Value ?? new IaDeployOptions();
            // Env overlays so local machines can point at Azure without editing committed JSON.
            OverlayGitFromEnv(git);
            OverlayDeployFromEnv(deploy);
            return new PipelineAgentHandler(
                sp.GetRequiredService<ILlmClient>(),
                sp.GetRequiredService<IArtifactBlobStore>(),
                sp.GetRequiredService<IEffectLedger>(),
                sp.GetRequiredService<IActivityLog>(),
                sp.GetRequiredService<IAgentEventPublisher>(),
                sp.GetRequiredService<ILogger<PipelineAgentHandler>>(),
                sp.GetRequiredService<IGuardrailPipeline>(),
                git,
                deploy);
        });
        services.AddSingleton<PipelineAgentDispatcher>();
        services.AddHostedService<PipelineAgentWorker>();

        return services;
    }

    private static void OverlayGitFromEnv(IaGitOptions git)
    {
        var provider = Environment.GetEnvironmentVariable("IA_GIT_PROVIDER");
        if (!string.IsNullOrWhiteSpace(provider))
        {
            git.Provider = provider.Trim();
        }

        var org = Environment.GetEnvironmentVariable("IA_GIT_ORGANIZATION");
        if (!string.IsNullOrWhiteSpace(org))
        {
            git.Organization = org.Trim();
        }

        var project = Environment.GetEnvironmentVariable("IA_GIT_PROJECT");
        if (!string.IsNullOrWhiteSpace(project))
        {
            git.Project = project.Trim();
        }

        var repo = Environment.GetEnvironmentVariable("IA_GIT_REPO");
        if (!string.IsNullOrWhiteSpace(repo))
        {
            git.Repo = repo.Trim();
        }

        var token = Environment.GetEnvironmentVariable("IA_GIT_TOKEN")
                    ?? Environment.GetEnvironmentVariable("AZDO_PAT");
        if (!string.IsNullOrWhiteSpace(token))
        {
            git.Token = token.Trim();
        }
    }

    private static void OverlayDeployFromEnv(IaDeployOptions deploy)
    {
        var provider = Environment.GetEnvironmentVariable("IA_DEPLOY_PROVIDER");
        if (!string.IsNullOrWhiteSpace(provider))
        {
            deploy.Provider = provider.Trim();
        }

        var app = Environment.GetEnvironmentVariable("IA_DEPLOY_APP_NAME");
        if (!string.IsNullOrWhiteSpace(app))
        {
            deploy.AppName = app.Trim();
        }

        var url = Environment.GetEnvironmentVariable("IA_DEPLOY_PUBLIC_URL");
        if (!string.IsNullOrWhiteSpace(url))
        {
            deploy.PublicBaseUrl = url.Trim();
        }

        var user = Environment.GetEnvironmentVariable("IA_DEPLOY_USER");
        if (!string.IsNullOrWhiteSpace(user))
        {
            deploy.PublishUser = user.Trim();
        }

        var pass = Environment.GetEnvironmentVariable("IA_DEPLOY_PASSWORD");
        if (!string.IsNullOrWhiteSpace(pass))
        {
            deploy.PublishPassword = pass.Trim();
        }
    }
}
