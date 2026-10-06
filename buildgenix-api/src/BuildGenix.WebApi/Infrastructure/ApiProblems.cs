namespace BuildGenix.WebApi.Infrastructure;

public static class ApiProblems
{
    public static IResult NotFound(string detail) =>
        Results.Problem(detail: detail, statusCode: StatusCodes.Status404NotFound, title: "Not Found",
            extensions: new Dictionary<string, object?> { ["code"] = "NotFound" });

    public static IResult ValidationFailed(string detail) =>
        Results.Problem(detail: detail, statusCode: StatusCodes.Status400BadRequest, title: "Validation Failed",
            extensions: new Dictionary<string, object?> { ["code"] = "ValidationFailed" });

    public static IResult ActiveConflict(string detail, string? activeWorkItemId = null, int? activeDisplayNumber = null)
    {
        var extensions = new Dictionary<string, object?> { ["code"] = "ActiveConflict" };
        if (!string.IsNullOrWhiteSpace(activeWorkItemId))
        {
            extensions["activeWorkItemId"] = activeWorkItemId;
        }

        if (activeDisplayNumber is not null)
        {
            extensions["activeDisplayNumber"] = activeDisplayNumber.Value;
        }

        return Results.Problem(
            detail: detail,
            statusCode: StatusCodes.Status409Conflict,
            title: "Active Conflict",
            extensions: extensions);
    }

    public static IResult Duplicate(string detail) =>
        Results.Problem(detail: detail, statusCode: StatusCodes.Status409Conflict, title: "Duplicate",
            extensions: new Dictionary<string, object?> { ["code"] = "Duplicate" });

    public static IResult NotReady(string detail) =>
        Results.Problem(
            detail: detail,
            statusCode: StatusCodes.Status501NotImplemented,
            title: "Not Implemented",
            extensions: new Dictionary<string, object?> { ["code"] = "NotImplemented" });
}
