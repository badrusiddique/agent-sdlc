using AgentHub.Agents.Healthy;
using AgentHub.Core.Models;
using AgentHub.Infrastructure.Messaging;

namespace AgentHub.WebApi.Healthy;

public static class HealthyEndpoints
{
    public static IEndpointRouteBuilder MapHealthyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/connectivity")
            .WithTags("Connectivity");

        group.MapPost("/healthy", PublishHealthyAsync)
            .WithName("PublishHealthyTask")
            .WithSummary(
                "Publishes a TaskMessage to sdlc-tasks-healthy. The HEALTHY BackgroundService consumes it, calls LLM, writes blob, and publishes AgentResult to sdlc-results.")
            .Produces<HealthyPublishResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static async Task<IResult> PublishHealthyAsync(
        HealthyPublishRequest? request,
        IHealthyTaskPublisher publisher,
        CancellationToken cancellationToken)
    {
        request ??= new HealthyPublishRequest();

        var projectId = string.IsNullOrWhiteSpace(request.ProjectId)
            ? "proj-healthy"
            : request.ProjectId.Trim();
        var workItemId = string.IsNullOrWhiteSpace(request.WorkItemId)
            ? UlidLike()
            : request.WorkItemId.Trim();
        var taskId = string.IsNullOrWhiteSpace(request.TaskId)
            ? UlidLike()
            : request.TaskId.Trim();
        var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
            ? Guid.NewGuid().ToString("N")
            : request.CorrelationId.Trim();
        var inputVersion = request.InputVersion is > 0 ? request.InputVersion.Value : 1;

        var message = new TaskMessage
        {
            WorkItemId = workItemId,
            ProjectId = projectId,
            Stage = HealthyAgentConstants.StageCode,
            TaskId = taskId,
            CorrelationId = correlationId,
            CausationId = request.CausationId ?? "agenthub-api/connectivity/healthy",
            InputVersion = inputVersion,
            StateContextId = HealthyAgentConstants.StateContextId(projectId, workItemId),
            Config = new TaskMessageConfig
            {
                ModelId = request.ModelId,
                ReasoningEffort = request.ReasoningEffort ?? "low"
            }
        };

        message.EnsureDefaults();
        var errors = HealthyTaskValidation.Validate(message);
        if (errors.Count > 0)
        {
            return Results.Problem(
                detail: string.Join(" ", errors),
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid HEALTHY TaskMessage");
        }

        try
        {
            await publisher.PublishAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Failed to publish HEALTHY task");
        }

        return Results.Accepted(
            $"/api/connectivity/healthy/{message.LogicalMessageId}",
            new HealthyPublishResponse(
                message.LogicalMessageId,
                message.CorrelationId,
                message.WorkItemId,
                message.ProjectId,
                message.TaskId,
                message.InputVersion,
                message.StateContextId!,
                HealthyAgentConstants.TaskTopic,
                HealthyAgentConstants.ResultsTopic));
    }

    private static string UlidLike() => Guid.NewGuid().ToString("N")[..16].ToUpperInvariant();
}

public sealed class HealthyPublishRequest
{
    public string? ProjectId { get; set; }
    public string? WorkItemId { get; set; }
    public string? TaskId { get; set; }
    public string? CorrelationId { get; set; }
    public string? CausationId { get; set; }
    public int? InputVersion { get; set; }
    public string? ModelId { get; set; }
    public string? ReasoningEffort { get; set; }
}

public sealed record HealthyPublishResponse(
    string MessageId,
    string CorrelationId,
    string WorkItemId,
    string ProjectId,
    string TaskId,
    int InputVersion,
    string StateContextId,
    string Topic,
    string ResultsTopic);
