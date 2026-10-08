using System.Net.Http.Json;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/api/thought", async (CancellationToken ct) =>
{
    var copyPath = Path.Combine(app.Environment.ContentRootPath, "ThoughtCopy.txt");
    var fallback = File.Exists(copyPath)
        ? (await File.ReadAllTextAsync(copyPath, ct)).Trim()
        : "Build something small that works.";

    var ollama = Environment.GetEnvironmentVariable("OLLAMA_BASE_URL") ?? "http://127.0.0.1:11434";
    try
    {
        using var http = new HttpClient { BaseAddress = new Uri(ollama.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(3) };
        using var response = await http.PostAsJsonAsync(
            "/api/chat",
            new
            {
                model = Environment.GetEnvironmentVariable("OLLAMA_MODEL") ?? "llama3.2",
                stream = false,
                messages = new object[]
                {
                    new { role = "user", content = "Reply with one short thought of the day." }
                }
            },
            ct);
        if (response.IsSuccessStatusCode)
        {
            var doc = await response.Content.ReadFromJsonAsync<OllamaChat>(cancellationToken: ct);
            var text = doc?.Message?.Content?.Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return Results.Ok(new { thought = text, source = "ollama" });
            }
        }
    }
    catch
    {
        // fall through to local copy
    }

    return Results.Ok(new { thought = fallback, source = "local" });
});

// {{EXTRA_ENDPOINTS}}

app.MapFallbackToFile("index.html");
app.Run();

internal sealed class OllamaChat
{
    public OllamaMessage? Message { get; set; }
}

internal sealed class OllamaMessage
{
    public string? Content { get; set; }
}

public partial class Program;
