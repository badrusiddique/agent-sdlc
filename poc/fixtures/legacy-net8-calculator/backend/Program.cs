var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/health", () => Results.Ok(new { status = "ok", runtime = "net8" }));
app.MapGet("/api/add", (double a, double b) =>
    Results.Ok(new { operation = "add", a, b, result = a + b }));
app.MapFallbackToFile("index.html");
app.Run();
