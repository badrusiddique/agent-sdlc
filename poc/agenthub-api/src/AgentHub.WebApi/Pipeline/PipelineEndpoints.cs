using AgentHub.Agents.Pipeline;
using AgentHub.Core.Abstractions;
using AgentHub.Core.Models;
using AgentHub.Infrastructure.Llm;
using AgentHub.WebApi.Infrastructure;
using Microsoft.Extensions.Options;

namespace AgentHub.WebApi.Pipeline;

public static class PipelineEndpoints
{
    public static IEndpointRouteBuilder MapPipelineEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/pipeline")
            .WithTags("Pipeline");

        group.MapPost("/dispatch", DispatchAsync)
            .WithName("DispatchPipelineTask")
            .WithSummary("In-process dispatch of one pipeline stage task (POC / InProcessOnly).")
            .Produces<AgentResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapPost("/llm-smoke", LlmSmokeAsync)
            .WithName("PipelineLlmSmoke")
            .WithSummary("Smoke-test the configured LLM provider(s). Use provider=ollama|gateway|primary.")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return app;
    }

    private static async Task<IResult> DispatchAsync(
        TaskMessage? message,
        PipelineAgentDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        if (message is null)
        {
            return ApiProblems.ValidationFailed("TaskMessage body is required.");
        }

        message.EnsureDefaults();
        var errors = PipelineTaskValidation.Validate(message);
        if (errors.Count > 0)
        {
            return ApiProblems.ValidationFailed(string.Join(" ", errors));
        }

        var outcome = await dispatcher
            .ProcessMessageAsync(message, deliveryCount: 1, cancellationToken)
            .ConfigureAwait(false);

        return outcome.Kind switch
        {
            PipelineDispatchKind.Handled when outcome.Result is not null =>
                Results.Ok(outcome.Result),
            PipelineDispatchKind.AlreadyProcessed =>
                Results.Ok(outcome.Result ?? new AgentResult
                {
                    WorkItemId = message.WorkItemId,
                    TaskId = message.TaskId,
                    Stage = message.Stage,
                    Status = AgentResultStatus.Completed,
                    CorrelationId = message.CorrelationId,
                    InputVersion = message.InputVersion,
                    RunId = PipelineAgentConstants.RunId(message.TaskId, message.InputVersion, 1)
                }),
            _ => Results.Problem(
                detail: outcome.DeadLetterReason ?? "Pipeline dispatch failed.",
                statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private static async Task<IResult> LlmSmokeAsync(
        LlmSmokeRequest? body,
        ILlmClient llm,
        IOptions<LlmOptions> llmOptions,
        CancellationToken cancellationToken)
    {
        var provider = (body?.Provider ?? "primary").Trim().ToLowerInvariant();
        var opts = llmOptions.Value;
        string? modelId = body?.ModelId;
        modelId = provider switch
        {
            "ollama" => string.IsNullOrWhiteSpace(modelId)
                ? $"{CompositeLlmClient.OllamaPrefix}{opts.Ollama.DefaultModel}"
                : modelId.StartsWith(CompositeLlmClient.OllamaPrefix, StringComparison.OrdinalIgnoreCase)
                    ? modelId
                    : CompositeLlmClient.OllamaPrefix + modelId,
            "gateway" => string.IsNullOrWhiteSpace(modelId)
                ? $"{CompositeLlmClient.GatewayPrefix}{opts.Gateway.DefaultModelAlias}"
                : modelId.StartsWith(CompositeLlmClient.GatewayPrefix, StringComparison.OrdinalIgnoreCase)
                    ? modelId
                    : CompositeLlmClient.GatewayPrefix + modelId,
            "primary" => modelId,
            _ => null
        };

        if (modelId is null && provider is not "primary")
        {
            return ApiProblems.ValidationFailed("provider must be ollama, gateway, or primary.");
        }

        try
        {
            var result = await llm.CompleteAsync(
                    new LlmCompletionRequest(
                        SystemPrompt: "You are a connectivity probe. Reply with one short sentence.",
                        UserPrompt: body?.Prompt ?? "Say hello from the LLM provider under test.",
                        ModelId: modelId,
                        ReasoningEffort: null),
                    cancellationToken)
                .ConfigureAwait(false);

            return Results.Ok(new
            {
                ok = true,
                composite = llm.ProviderName,
                requestedProvider = provider,
                modelId = result.ModelId,
                text = result.Text
            });
        }
        catch (Exception ex)
        {
            return Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError,
                title: $"LLM smoke failed for provider={provider}");
        }
    }
}

public sealed class LlmSmokeRequest
{
    /// <summary>ollama | gateway | primary</summary>
    public string? Provider { get; set; }

    public string? ModelId { get; set; }

    public string? Prompt { get; set; }
}
