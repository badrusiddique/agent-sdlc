var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/api/add", (double a, double b) => Results.Ok(new { result = a + b }));

// {{EXTRA_ENDPOINTS}}

app.MapFallbackToFile("index.html");
app.Run();

public partial class Program;
