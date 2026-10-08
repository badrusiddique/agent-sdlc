using AgentHub.Agents.Pipeline;
using AgentHub.Core.Models;
using AgentHub.WebApi.Infrastructure;

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
}
