namespace BuildGenix.WebApi.Heartbeat;

public static class HeartbeatEndpoints
{
    public static IEndpointRouteBuilder MapHeartbeatEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/connectivity")
            .WithTags("Connectivity");

        group.MapPost("/heartbeat", PublishHeartbeatAsync)
            .WithName("PublishHeartbeat")
            .WithSummary("Publishes a ping to bg-heartbeat-ping; the same API consumer appends a marker and publishes bg-heartbeat-pong.")
            .Produces<HeartbeatPublishResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static async Task<IResult> PublishHeartbeatAsync(
        IHeartbeatPublisher publisher,
        CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        var message = new HeartbeatMessage(
            CorrelationId: correlationId,
            SentAtUtc: DateTimeOffset.UtcNow,
            Payload: "ping");

        try
        {
            await publisher.PublishPingAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Failed to publish heartbeat ping");
        }

        return Results.Accepted(
            $"/api/connectivity/heartbeat/{correlationId}",
            new HeartbeatPublishResponse(correlationId, message.SentAtUtc, "bg-heartbeat-ping"));
    }
}

public sealed record HeartbeatPublishResponse(
    string CorrelationId,
    DateTimeOffset SentAtUtc,
    string Topic);
