using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgentHub.Agents.Pipeline.Ia;

/// <summary>
/// Builds a work-item app at runtime from the user request / stack — not by copying a
/// fixed calculator template. Shapes are selected from the request text so hello world,
/// LLM hello, todo, calculator, or a generic API can all be generated on demand.
/// </summary>
public static class RequestDrivenCodegen
{
    public enum AppKind
    {
        Calculator,
        Todo,
        Hello,
        HelloLlm,
        Generic
    }

    public static AppKind Classify(string? userRequest, string? stackProfile, string? projectName = null)
    {
        var text = $"{userRequest} {projectName}".ToLowerInvariant();
        var stack = stackProfile ?? "";

        var wantsLlm = stack.Equals("fe-be-llm", StringComparison.OrdinalIgnoreCase)
            || text.Contains("ollama", StringComparison.Ordinal)
            || text.Contains("thought of the day", StringComparison.Ordinal)
            || (text.Contains("thought", StringComparison.Ordinal) && text.Contains("hello", StringComparison.Ordinal))
            || (text.Contains("llm", StringComparison.Ordinal)
                && !Regex.IsMatch(text, @"\b(without|no|sans)\s+(an\s+)?llm\b"));
        if (wantsLlm)
        {
            return AppKind.HelloLlm;
        }

        if (text.Contains("todo", StringComparison.Ordinal)
            || text.Contains("to-do", StringComparison.Ordinal)
            || text.Contains("task list", StringComparison.Ordinal))
        {
            return AppKind.Todo;
        }

        if (text.Contains("calculator", StringComparison.Ordinal)
            || text.Contains("subtract", StringComparison.Ordinal)
            || Regex.IsMatch(text, @"\badd(s|ing)?\b.*\b(two|2)\b.*\bnumbers?\b"))
        {
            return AppKind.Calculator;
        }

        if (text.Contains("hello", StringComparison.Ordinal)
            || text.Contains("hello world", StringComparison.Ordinal))
        {
            return AppKind.Hello;
        }

        return AppKind.Generic;
    }

    public static string Generate(
        string workspace,
        AppKind kind,
        string projectId,
        string workItemId,
        string userRequest,
        string? llmHintsJson = null)
    {
        if (Directory.Exists(workspace))
        {
            Directory.Delete(workspace, recursive: true);
        }

        Directory.CreateDirectory(workspace);
        var headline = ExtractHeadline(llmHintsJson, kind, userRequest);
        var apiName = SanitizeIdent(kind.ToString()) + "Api";

        WriteAll(workspace, "backend/" + apiName + ".csproj", Csproj());
        WriteAll(workspace, "backend/Program.cs", ProgramCs(kind, headline));
        if (kind == AppKind.Calculator)
        {
            WriteAll(workspace, "backend/Services/CalculatorMath.cs", CalculatorMathCs());
        }

        if (kind == AppKind.Todo)
        {
            WriteAll(workspace, "backend/TodoStore.cs", TodoStoreCs());
        }

        if (kind == AppKind.HelloLlm)
        {
            WriteAll(workspace, "backend/ThoughtCopy.txt", "A thought for today.");
        }

        WriteAll(workspace, "frontend/index.html", FrontendHtml(kind, headline));
        WriteAll(workspace, "backend/wwwroot/index.html", FrontendHtml(kind, headline));
        WriteAll(workspace, "docker-compose.yml", DockerCompose(apiName));
        WriteAll(workspace, "README.md", Readme(kind, projectId, workItemId, userRequest));

        WriteTests(workspace, kind, apiName);

        return JsonSerializer.Serialize(
            new
            {
                mode = "request-driven",
                appKind = kind.ToString(),
                apiName,
                headline,
                userRequest
            });
    }

    private static void WriteTests(string workspace, AppKind kind, string apiName)
    {
        var testsDir = Path.Combine(workspace, "tests", apiName + ".Tests");
        Directory.CreateDirectory(testsDir);
        WriteAll(
            workspace,
            $"tests/{apiName}.Tests/{apiName}.Tests.csproj",
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <IsPackable>false</IsPackable>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.0" />
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
                <PackageReference Include="xunit" Version="2.9.3" />
                <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
              </ItemGroup>
              <ItemGroup>
                <ProjectReference Include="..\..\backend\{apiName}.csproj" />
              </ItemGroup>
            </Project>
            """);

        WriteAll(
            workspace,
            $"tests/{apiName}.Tests/ApiRoundTripTests.cs",
            ApiTestsCs(kind));
    }

    private static string ApiTestsCs(AppKind kind) =>
        kind switch
        {
            AppKind.Calculator => """
                using System.Net.Http.Json;
                using Microsoft.AspNetCore.Mvc.Testing;
                using Xunit;

                public class ApiRoundTripTests : IClassFixture<WebApplicationFactory<Program>>
                {
                    private readonly HttpClient _client;
                    public ApiRoundTripTests(WebApplicationFactory<Program> factory) => _client = factory.CreateClient();

                    [Fact]
                    public async Task Health_ok()
                    {
                        var res = await _client.GetAsync("/health");
                        res.EnsureSuccessStatusCode();
                    }

                    [Fact]
                    public async Task Add_works()
                    {
                        var doc = await _client.GetFromJsonAsync<Dictionary<string, object>>("/api/add?a=2&b=3");
                        Assert.NotNull(doc);
                        Assert.Equal(5d, Convert.ToDouble(doc!["result"].ToString()));
                    }
                }
                """,
            AppKind.Todo => """
                using System.Net.Http.Json;
                using Microsoft.AspNetCore.Mvc.Testing;
                using Xunit;

                public class ApiRoundTripTests : IClassFixture<WebApplicationFactory<Program>>
                {
                    private readonly HttpClient _client;
                    public ApiRoundTripTests(WebApplicationFactory<Program> factory) => _client = factory.CreateClient();

                    [Fact]
                    public async Task Health_ok()
                    {
                        var res = await _client.GetAsync("/health");
                        res.EnsureSuccessStatusCode();
                    }

                    [Fact]
                    public async Task Todo_roundtrip()
                    {
                        var created = await _client.PostAsJsonAsync("/api/todos", new { title = "ship" });
                        created.EnsureSuccessStatusCode();
                        var list = await _client.GetFromJsonAsync<List<Dictionary<string, object>>>("/api/todos");
                        Assert.NotNull(list);
                        Assert.Contains(list!, t => t["title"]?.ToString() == "ship");
                    }
                }
                """,
            AppKind.HelloLlm => """
                using System.Net.Http.Json;
                using Microsoft.AspNetCore.Mvc.Testing;
                using Xunit;

                public class ApiRoundTripTests : IClassFixture<WebApplicationFactory<Program>>
                {
                    private readonly HttpClient _client;
                    public ApiRoundTripTests(WebApplicationFactory<Program> factory) => _client = factory.CreateClient();

                    [Fact]
                    public async Task Health_ok()
                    {
                        var res = await _client.GetAsync("/health");
                        res.EnsureSuccessStatusCode();
                    }

                    [Fact]
                    public async Task Thought_ok()
                    {
                        var doc = await _client.GetFromJsonAsync<Dictionary<string, object>>("/api/thought");
                        Assert.NotNull(doc);
                        Assert.True(doc!.ContainsKey("thought"));
                    }
                }
                """,
            _ => """
                using System.Net.Http.Json;
                using Microsoft.AspNetCore.Mvc.Testing;
                using Xunit;

                public class ApiRoundTripTests : IClassFixture<WebApplicationFactory<Program>>
                {
                    private readonly HttpClient _client;
                    public ApiRoundTripTests(WebApplicationFactory<Program> factory) => _client = factory.CreateClient();

                    [Fact]
                    public async Task Health_ok()
                    {
                        var res = await _client.GetAsync("/health");
                        res.EnsureSuccessStatusCode();
                    }

                    [Fact]
                    public async Task Hello_ok()
                    {
                        var doc = await _client.GetFromJsonAsync<Dictionary<string, object>>("/api/hello");
                        Assert.NotNull(doc);
                        Assert.Equal("ok", doc!["status"]?.ToString());
                    }
                }
                """
        };

    private static string ProgramCs(AppKind kind, string headline) =>
        kind switch
        {
            AppKind.Calculator => """
                using CalculatorApi.Services;

                var builder = WebApplication.CreateBuilder(args);
                var app = builder.Build();
                app.UseDefaultFiles();
                app.UseStaticFiles();
                app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
                app.MapGet("/api/add", (double a, double b) =>
                    Results.Ok(new { operation = "add", a, b, result = CalculatorMath.Add(a, b) }));
                // {{EXTRA_ENDPOINTS}}
                app.MapFallbackToFile("index.html");
                app.Run();
                public partial class Program;
                """,
            AppKind.Todo => """
                var builder = WebApplication.CreateBuilder(args);
                var app = builder.Build();
                var store = new TodoStore();
                app.UseDefaultFiles();
                app.UseStaticFiles();
                app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
                app.MapGet("/api/todos", () => Results.Ok(store.List()));
                app.MapPost("/api/todos", (TodoCreate body) =>
                {
                    var item = store.Add(body.Title ?? "untitled");
                    return Results.Created($"/api/todos/{item.Id}", item);
                });
                app.MapFallbackToFile("index.html");
                app.Run();
                public sealed record TodoCreate(string? Title);
                public partial class Program;
                """,
            AppKind.HelloLlm => $$"""
                var builder = WebApplication.CreateBuilder(args);
                var app = builder.Build();
                app.UseDefaultFiles();
                app.UseStaticFiles();
                app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
                app.MapGet("/api/hello", () => Results.Ok(new { status = "ok", message = "{{Escape(headline)}}" }));
                app.MapGet("/api/thought", () =>
                {
                    var path = Path.Combine(app.Environment.ContentRootPath, "ThoughtCopy.txt");
                    var thought = File.Exists(path) ? File.ReadAllText(path).Trim() : "A thought for today.";
                    return Results.Ok(new { thought, source = "fallback" });
                });
                app.MapFallbackToFile("index.html");
                app.Run();
                public partial class Program;
                """,
            _ => $$"""
                var builder = WebApplication.CreateBuilder(args);
                var app = builder.Build();
                app.UseDefaultFiles();
                app.UseStaticFiles();
                app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
                app.MapGet("/api/hello", () => Results.Ok(new { status = "ok", message = "{{Escape(headline)}}" }));
                app.MapFallbackToFile("index.html");
                app.Run();
                public partial class Program;
                """
        };

    private static string CalculatorMathCs() =>
        """
        namespace CalculatorApi.Services;

        public static class CalculatorMath
        {
            public static double Add(double a, double b) => a + b;
            public static double Subtract(double a, double b) => a - b;
        }
        """;

    private static string TodoStoreCs() =>
        """
        public sealed class TodoStore
        {
            private readonly List<TodoItem> _items = new();
            private int _seq;

            public IReadOnlyList<TodoItem> List() => _items;

            public TodoItem Add(string title)
            {
                var item = new TodoItem(++_seq, title, false);
                _items.Add(item);
                return item;
            }
        }

        public sealed record TodoItem(int Id, string Title, bool Done);
        """;

    private static string Csproj() =>
        """
        <Project Sdk="Microsoft.NET.Sdk.Web">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
          </PropertyGroup>
        </Project>
        """;

    private static string FrontendHtml(AppKind kind, string headline)
    {
        var body = kind switch
        {
            AppKind.Calculator =>
                """
                <label>a <input id="a" type="number" value="2"></label>
                <label>b <input id="b" type="number" value="3"></label>
                <button id="go">Add</button>
                <pre id="out"></pre>
                <script>
                document.getElementById('go').onclick = async () => {
                  const a = document.getElementById('a').value;
                  const b = document.getElementById('b').value;
                  const r = await fetch('/api/add?a=' + a + '&b=' + b);
                  document.getElementById('out').textContent = await r.text();
                };
                </script>
                """,
            AppKind.Todo =>
                """
                <input id="title" placeholder="todo"><button id="add">Add</button>
                <ul id="list"></ul>
                <script>
                async function refresh(){
                  const r = await fetch('/api/todos');
                  const items = await r.json();
                  document.getElementById('list').innerHTML = items.map(i => '<li>' + i.title + '</li>').join('');
                }
                document.getElementById('add').onclick = async () => {
                  await fetch('/api/todos', {method:'POST', headers:{'Content-Type':'application/json'},
                    body: JSON.stringify({title: document.getElementById('title').value})});
                  await refresh();
                };
                refresh();
                </script>
                """,
            AppKind.HelloLlm =>
                """
                <button id="go">Thought</button><pre id="out"></pre>
                <script>
                document.getElementById('go').onclick = async () => {
                  const r = await fetch('/api/thought');
                  document.getElementById('out').textContent = await r.text();
                };
                </script>
                """,
            _ =>
                """
                <button id="go">Hello</button><pre id="out"></pre>
                <script>
                document.getElementById('go').onclick = async () => {
                  const r = await fetch('/api/hello');
                  document.getElementById('out').textContent = await r.text();
                };
                </script>
                """
        };

        var title = Escape(headline);
        return "<!doctype html>\n<html lang=\"en\"><head><meta charset=\"utf-8\"><title>"
               + title + "</title></head>\n<body>\n<h1>" + title + "</h1>\n"
               + body + "\n</body></html>\n";
    }

    private static string DockerCompose(string apiName) =>
        """
        services:
          api:
            image: mcr.microsoft.com/dotnet/sdk:10.0
            working_dir: /src
            volumes:
              - ./backend:/src
            command: ["dotnet", "run", "--project", "CSProjName.csproj", "--urls", "http://0.0.0.0:8080"]
            ports:
              - "43143:8080"
            extra_hosts:
              - "host.docker.internal:host-gateway"
        """.Replace("CSProjName", apiName, StringComparison.Ordinal);

    private static string Readme(AppKind kind, string projectId, string workItemId, string userRequest) =>
        $"""
        # {kind} work item

        - project: `{projectId}`
        - work item: `{workItemId}`
        - request: {userRequest}

        Generated at runtime by request-driven IA codegen (not a fixed calculator template copy).
        """;

    private static string ExtractHeadline(string? llmJson, AppKind kind, string userRequest)
    {
        if (!string.IsNullOrWhiteSpace(llmJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(llmJson);
                if (doc.RootElement.TryGetProperty("headline", out var h)
                    && h.GetString() is { Length: > 0 } text)
                {
                    return text.Length > 80 ? text[..80] : text;
                }
            }
            catch (JsonException)
            {
                // ignore
            }
        }

        return kind switch
        {
            AppKind.Calculator => "Calculator",
            AppKind.Todo => "Todo",
            AppKind.HelloLlm => "Hello + thought",
            AppKind.Hello => "Hello World",
            _ => Truncate(userRequest, 60)
        };
    }

    private static string SanitizeIdent(string s) =>
        Regex.Replace(s, "[^A-Za-z0-9_]", "");

    private static string Escape(string s) =>
        s.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    private static void WriteAll(string workspace, string relative, string content)
    {
        var path = Path.Combine(workspace, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.Replace("\r\n", "\n", StringComparison.Ordinal), Encoding.UTF8);
    }
}
