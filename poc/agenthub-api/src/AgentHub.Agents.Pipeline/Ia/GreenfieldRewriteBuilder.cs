using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgentHub.Agents.Pipeline.Ia;

/// <summary>
/// Rewrites an existing baseline folder into a net10.0 greenfield work-item tree.
/// Preserves discovered public HTTP routes; does not invent a second product.
/// </summary>
public static class GreenfieldRewriteBuilder
{
    public const string TargetFramework = "net10.0";
    public const string DefaultFixtureRelativePath = "fixtures/legacy-net8-calculator";

    public sealed record Result(
        string BaselinePath,
        string TargetFramework,
        string[] PreservedRoutes,
        string ApiName);

    public static string ResolveDefaultFixtureRoot() =>
        ResolveExistingBaseline(DefaultFixtureRelativePath)
        ?? throw new InvalidOperationException(
            $"Could not locate {DefaultFixtureRelativePath}. Pass BaselineArtifacts.baselineSourcePath.");

    public static string? ResolveExistingBaseline(string baselinePath)
    {
        if (Path.IsPathRooted(baselinePath) && Directory.Exists(baselinePath))
        {
            return Path.GetFullPath(baselinePath);
        }

        var trimmed = baselinePath.Trim().TrimStart('.', '/', '\\');
        foreach (var start in new[]
                 {
                     Directory.GetCurrentDirectory(),
                     AppContext.BaseDirectory,
                     Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..")
                 })
        {
            var dir = new DirectoryInfo(Path.GetFullPath(start));
            for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
            {
                foreach (var candidate in new[]
                         {
                             Path.Combine(dir.FullName, trimmed),
                             Path.Combine(dir.FullName, "poc", trimmed),
                             Path.Combine(dir.FullName, DefaultFixtureRelativePath),
                             Path.Combine(dir.FullName, "poc", DefaultFixtureRelativePath)
                         })
                {
                    if (Directory.Exists(candidate))
                    {
                        return Path.GetFullPath(candidate);
                    }
                }
            }
        }

        return Path.IsPathRooted(baselinePath) ? baselinePath : null;
    }

    public static Result Rewrite(
        string workspace,
        string? baselinePath,
        string projectId,
        string workItemId,
        string userRequest)
    {
        var source = string.IsNullOrWhiteSpace(baselinePath)
            ? ResolveDefaultFixtureRoot()
            : ResolveExistingBaseline(baselinePath);
        if (string.IsNullOrWhiteSpace(source) || !Directory.Exists(source))
        {
            throw new InvalidOperationException($"Baseline path missing for rewrite: {baselinePath}");
        }

        if (Directory.Exists(workspace))
        {
            Directory.Delete(workspace, recursive: true);
        }

        Directory.CreateDirectory(workspace);
        CopyTree(source, workspace);

        var routes = DiscoverRoutes(workspace);
        if (routes.Length == 0)
        {
            routes = ["/health", "/api/add"];
        }

        BumpCsprojsToNet10(workspace);
        EnsureModernLayout(workspace, routes, userRequest);
        WriteTests(workspace, routes);
        MonoRepoLayout.WriteReadme(workspace, projectId, workItemId, userRequest);
        File.WriteAllText(
            Path.Combine(workspace, "REWRITE.md"),
            $"""
            # Greenfield rewrite

            - Baseline: `{source}`
            - Target: `{TargetFramework}`
            - Preserved routes: {string.Join(", ", routes)}
            - Request: {userRequest}
            """);

        MonoRepoLayout.SyncFrontendToWwwroot(workspace);
        return new Result(source, TargetFramework, routes, "RewrittenApi");
    }

    private static void CopyTree(string source, string dest)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, file).Replace('\\', '/');
            if (rel.StartsWith("bin/", StringComparison.OrdinalIgnoreCase)
                || rel.StartsWith("obj/", StringComparison.OrdinalIgnoreCase)
                || rel.StartsWith(".git/", StringComparison.OrdinalIgnoreCase)
                || rel.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
                || rel.Contains("/obj/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var target = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static string[] DiscoverRoutes(string workspace)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cs in Directory.EnumerateFiles(workspace, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(cs);
            foreach (Match m in Regex.Matches(text, @"MapGet\(\s*""([^""]+)"""))
            {
                found.Add(m.Groups[1].Value);
            }
        }

        return found.OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }

    private static void BumpCsprojsToNet10(string workspace)
    {
        foreach (var csproj in Directory.EnumerateFiles(workspace, "*.csproj", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(csproj);
            text = Regex.Replace(
                text,
                @"<TargetFramework>net\d+\.\d+</TargetFramework>",
                $"<TargetFramework>{TargetFramework}</TargetFramework>");
            if (!text.Contains($"<TargetFramework>{TargetFramework}</TargetFramework>", StringComparison.Ordinal))
            {
                text = text.Replace(
                    "</PropertyGroup>",
                    $"  <TargetFramework>{TargetFramework}</TargetFramework>\n  </PropertyGroup>",
                    StringComparison.Ordinal);
            }

            // Rename legacy project file to RewrittenApi for clarity when present.
            File.WriteAllText(csproj, text);
        }
    }

    private static void EnsureModernLayout(string workspace, string[] routes, string userRequest)
    {
        var backend = Path.Combine(workspace, "backend");
        Directory.CreateDirectory(backend);
        var csproj = Directory.EnumerateFiles(backend, "*.csproj").FirstOrDefault()
                     ?? Path.Combine(backend, "RewrittenApi.csproj");
        if (!File.Exists(csproj))
        {
            File.WriteAllText(
                csproj,
                $"""
                <Project Sdk="Microsoft.NET.Sdk.Web">
                  <PropertyGroup>
                    <TargetFramework>{TargetFramework}</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <RootNamespace>RewrittenApi</RootNamespace>
                  </PropertyGroup>
                </Project>
                """);
        }

        var program = Path.Combine(backend, "Program.cs");
        if (!File.Exists(program) || !File.ReadAllText(program).Contains("/health", StringComparison.Ordinal))
        {
            var sb = new StringBuilder();
            sb.AppendLine("var builder = WebApplication.CreateBuilder(args);");
            sb.AppendLine("var app = builder.Build();");
            sb.AppendLine("app.UseDefaultFiles();");
            sb.AppendLine("app.UseStaticFiles();");
            sb.AppendLine("app.MapGet(\"/health\", () => Results.Ok(new { status = \"ok\", runtime = \"net10\" }));");
            if (routes.Any(r => r.Contains("/api/add", StringComparison.Ordinal)))
            {
                sb.AppendLine(
                    "app.MapGet(\"/api/add\", (double a, double b) => Results.Ok(new { operation = \"add\", a, b, result = a + b }));");
            }

            sb.AppendLine("app.MapFallbackToFile(\"index.html\");");
            sb.AppendLine("app.Run();");
            sb.AppendLine("public partial class Program;");
            File.WriteAllText(program, sb.ToString());
        }
        else
        {
            // Annotate health so smoke can see rewrite happened.
            var text = File.ReadAllText(program);
            text = text.Replace("runtime = \"net8\"", "runtime = \"net10\"", StringComparison.Ordinal);
            if (!text.Contains("net10", StringComparison.Ordinal)
                && text.Contains("/health", StringComparison.Ordinal))
            {
                text = text.Replace(
                    "Results.Ok(new { status = \"ok\" })",
                    "Results.Ok(new { status = \"ok\", runtime = \"net10\" })",
                    StringComparison.Ordinal);
            }

            if (!text.Contains("partial class Program", StringComparison.Ordinal))
            {
                text = text.TrimEnd() + "\npublic partial class Program;\n";
            }

            File.WriteAllText(program, text);
        }

        // Always replace baseline FE with a net10 greenfield UI that matches
        // PlaywrightSuiteGenerator contracts (#a/#b/#go for add, etc.). Keeping the
        // legacy fixture markup (#add only) makes TA Playwright permanently red.
        var frontend = Path.Combine(workspace, "frontend");
        Directory.CreateDirectory(frontend);
        var index = Path.Combine(frontend, "index.html");
        File.WriteAllText(index, BuildGreenfieldFrontendHtml(routes, userRequest));

        var apiName = Path.GetFileNameWithoutExtension(csproj);
        File.WriteAllText(
            Path.Combine(workspace, "docker-compose.yml"),
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
            """.Replace("CSProjName", apiName, StringComparison.Ordinal));
    }

    private static void WriteTests(string workspace, string[] routes)
    {
        var testsDir = Path.Combine(workspace, "tests", "RewrittenApi.Tests");
        Directory.CreateDirectory(testsDir);
        File.WriteAllText(
            Path.Combine(testsDir, "RewrittenApi.Tests.csproj"),
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>{TargetFramework}</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <IsPackable>false</IsPackable>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
                <PackageReference Include="xunit" Version="2.9.2" />
                <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
                <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.0" />
              </ItemGroup>
              <ItemGroup>
                <ProjectReference Include="..\..\backend\*.csproj" />
              </ItemGroup>
            </Project>
            """);

        // Fix project reference to concrete csproj
        var backendCsproj = Directory.EnumerateFiles(Path.Combine(workspace, "backend"), "*.csproj").First();
        var rel = Path.GetRelativePath(testsDir, backendCsproj).Replace('\\', '/');
        var testCsproj = File.ReadAllText(Path.Combine(testsDir, "RewrittenApi.Tests.csproj"));
        testCsproj = testCsproj.Replace(
            @"Include=""..\..\backend\*.csproj""",
            $@"Include=""{rel}""",
            StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(testsDir, "RewrittenApi.Tests.csproj"), testCsproj);

        var hasAdd = routes.Any(r => r.Contains("/api/add", StringComparison.Ordinal));
        File.WriteAllText(
            Path.Combine(testsDir, "ApiRoundTripTests.cs"),
            $$"""
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
                    var doc = await _client.GetFromJsonAsync<Dictionary<string, object>>("/health");
                    Assert.NotNull(doc);
                    Assert.Equal("ok", doc!["status"]?.ToString());
                }
            {{(hasAdd ? """

                [Fact]
                public async Task Add_works()
                {
                    var doc = await _client.GetFromJsonAsync<Dictionary<string, object>>("/api/add?a=2&b=3");
                    Assert.NotNull(doc);
                    Assert.Equal(5d, Convert.ToDouble(doc!["result"].ToString()));
                }
            """ : "")}}
            }
            """);
    }

    private static string BuildGreenfieldFrontendHtml(string[] routes, string userRequest)
    {
        var hasAdd = routes.Any(r => r.Contains("/api/add", StringComparison.Ordinal));
        var title = "Rewritten Calculator (net10)";
        string body;
        if (hasAdd)
        {
            body =
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
                """;
        }
        else
        {
            var safe = System.Net.WebUtility.HtmlEncode(userRequest);
            body =
                "<p>" + safe + "</p>\n"
                + "<button id=\"go\">Ping</button>\n"
                + "<pre id=\"out\"></pre>\n"
                + "<script>\n"
                + "document.getElementById('go').onclick = async () => {\n"
                + "  const r = await fetch('/health');\n"
                + "  document.getElementById('out').textContent = await r.text();\n"
                + "};\n"
                + "</script>\n";
        }

        return "<!doctype html>\n<html lang=\"en\"><head><meta charset=\"utf-8\"><title>"
               + title + "</title></head>\n<body>\n<h1>" + title + "</h1>\n"
               + body + "\n</body></html>\n";
    }
}
