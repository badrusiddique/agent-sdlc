namespace AgentHub.WebApi.Infrastructure;

public static class ApiProblems
{
    public static IResult ValidationFailed(string detail) =>
        Results.Problem(
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest,
            title: "Validation Failed",
            extensions: new Dictionary<string, object?> { ["code"] = "ValidationFailed" });

    public static IResult ServiceUnavailable(string detail) =>
        Results.Problem(
            detail: detail,
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Service Unavailable",
            extensions: new Dictionary<string, object?> { ["code"] = "ServiceUnavailable" });
}
