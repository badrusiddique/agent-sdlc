namespace BuildGenix.WebApi.Auth;

/// <summary>Phase 0 dummy auth (D-CL-9): anyone with the URL; identity from X-User-Id.</summary>
public sealed class DummyUserIdMiddleware
{
    public const string HeaderName = "X-User-Id";
    public const string ItemKey = "BuildGenix.UserId";
    public const string DefaultUserId = "anonymous";

    private readonly RequestDelegate _next;

    public DummyUserIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var userId = context.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(userId))
        {
            userId = DefaultUserId;
        }

        context.Items[ItemKey] = userId.Trim();
        await _next(context).ConfigureAwait(false);
    }
}

public static class DummyUserIdExtensions
{
    public static string GetUserId(this HttpContext context) =>
        context.Items.TryGetValue(DummyUserIdMiddleware.ItemKey, out var value)
        && value is string userId
        && !string.IsNullOrWhiteSpace(userId)
            ? userId
            : DummyUserIdMiddleware.DefaultUserId;

    public static IApplicationBuilder UseDummyUserId(this IApplicationBuilder app) =>
        app.UseMiddleware<DummyUserIdMiddleware>();
}
