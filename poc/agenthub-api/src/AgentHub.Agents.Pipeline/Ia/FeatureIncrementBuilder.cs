using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgentHub.Agents.Pipeline.Ia;

/// <summary>
/// Copies the latest prior work-item workspace for a project and applies the next feature.
/// Supports multi-WI "build on top" without wiping the previous app.
/// </summary>
public static class FeatureIncrementBuilder
{
    public enum FeatureKind
    {
        Subtract,
        Multiply,
        TodoEndpoint,
        GenericNote
    }

    public sealed record Result(string PriorWorkspace, FeatureKind Feature, string[] RoutesAdded);

    public static string? FindPriorWorkspace(string projectId, string currentWorkItemId)
    {
        var root = Path.Combine(Path.GetTempPath(), "bgx-poc");
        if (!Directory.Exists(root))
        {
            return null;
        }

        var prefix = $"project-{Sanitize(projectId)}-work-item-";
        var currentFolder = MonoRepoLayout.FolderName(projectId, currentWorkItemId);
        var candidates = Directory.EnumerateDirectories(root)
            .Where(d =>
            {
                var name = Path.GetFileName(d);
                return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                       && !name.Equals(currentFolder, StringComparison.OrdinalIgnoreCase)
                       && File.Exists(Path.Combine(d, "backend", "Program.cs"));
            })
            .Select(d => new DirectoryInfo(d))
            .OrderByDescending(d => d.LastWriteTimeUtc)
            .ToList();

        return candidates.FirstOrDefault()?.FullName;
    }

    public static FeatureKind ClassifyFeature(string? userRequest)
    {
        var text = (userRequest ?? "").ToLowerInvariant();
        if (text.Contains("subtract", StringComparison.Ordinal) || text.Contains("subtraction", StringComparison.Ordinal))
        {
            return FeatureKind.Subtract;
        }

        if (text.Contains("multiply", StringComparison.Ordinal) || text.Contains("multiplication", StringComparison.Ordinal))
        {
            return FeatureKind.Multiply;
        }

        if (text.Contains("todo", StringComparison.Ordinal) || text.Contains("task list", StringComparison.Ordinal))
        {
            return FeatureKind.TodoEndpoint;
        }

        return FeatureKind.GenericNote;
    }

    public static Result Apply(
        string workspace,
        string projectId,
        string workItemId,
        string userRequest,
        string? explicitPriorPath = null)
    {
        var prior = explicitPriorPath;
        if (string.IsNullOrWhiteSpace(prior) || !Directory.Exists(prior))
        {
            prior = FindPriorWorkspace(projectId, workItemId);
        }

        if (string.IsNullOrWhiteSpace(prior) || !Directory.Exists(prior))
        {
            // No prior workspace: seed a calculator then apply feature (first Existing WI without disk baseline).
            RequestDrivenCodegen.Generate(
                workspace,
                RequestDrivenCodegen.AppKind.Calculator,
                projectId,
                workItemId,
                userRequest);
            prior = workspace;
        }
        else if (!PathsEqual(prior, workspace))
        {
            if (Directory.Exists(workspace))
            {
                Directory.Delete(workspace, recursive: true);
            }

            CopyTree(prior, workspace);
        }

        var feature = ClassifyFeature(userRequest);
        var added = ApplyFeature(workspace, feature, userRequest);
        MonoRepoLayout.SyncFrontendToWwwroot(workspace);
        File.WriteAllText(
            Path.Combine(workspace, "FEATURE.md"),
            $"""
            # Feature increment

            - Prior: `{prior}`
            - Feature: {feature}
            - Routes added: {string.Join(", ", added)}
            - Request: {userRequest}
            """);

        return new Result(prior!, feature, added);
    }

    private static string[] ApplyFeature(string workspace, FeatureKind feature, string userRequest)
    {
        return feature switch
        {
            FeatureKind.Subtract => ApplySubtract(workspace),
            FeatureKind.Multiply => ApplyMultiply(workspace),
            FeatureKind.TodoEndpoint => ApplyTodo(workspace),
            _ => ApplyGenericNote(workspace, userRequest)
        };
    }

    private static string[] ApplySubtract(string workspace)
    {
        var program = Path.Combine(workspace, "backend", "Program.cs");
        if (!File.Exists(program))
        {
            return [];
        }

        var text = File.ReadAllText(program);
        if (text.Contains("/api/subtract", StringComparison.Ordinal))
        {
            return [];
        }

        EnsureExtraMarker(ref text);
        EnsureCalculatorMath(workspace);
        text = text.Replace(
            "// {{EXTRA_ENDPOINTS}}",
            """
            app.MapGet("/api/subtract", (double a, double b) =>
                Results.Ok(new { operation = "subtract", a, b, result = a - b }));
            // {{EXTRA_ENDPOINTS}}
            """,
            StringComparison.Ordinal);
        File.WriteAllText(program, text);
        PatchFrontend(workspace, "subBtn", "/api/subtract", "Subtract");
        return ["/api/subtract"];
    }

    private static string[] ApplyMultiply(string workspace)
    {
        var program = Path.Combine(workspace, "backend", "Program.cs");
        if (!File.Exists(program))
        {
            return [];
        }

        var text = File.ReadAllText(program);
        if (text.Contains("/api/multiply", StringComparison.Ordinal))
        {
            return [];
        }

        EnsureExtraMarker(ref text);
        text = text.Replace(
            "// {{EXTRA_ENDPOINTS}}",
            """
            app.MapGet("/api/multiply", (double a, double b) =>
                Results.Ok(new { operation = "multiply", a, b, result = a * b }));
            // {{EXTRA_ENDPOINTS}}
            """,
            StringComparison.Ordinal);
        File.WriteAllText(program, text);
        PatchFrontend(workspace, "mulBtn", "/api/multiply", "Multiply");
        return ["/api/multiply"];
    }

    private static string[] ApplyTodo(string workspace)
    {
        var program = Path.Combine(workspace, "backend", "Program.cs");
        if (!File.Exists(program))
        {
            return [];
        }

        var text = File.ReadAllText(program);
        if (text.Contains("/api/todos", StringComparison.Ordinal))
        {
            return [];
        }

        EnsureExtraMarker(ref text);
        var storePath = Path.Combine(workspace, "backend", "TodoStore.cs");
        if (!File.Exists(storePath))
        {
            File.WriteAllText(
                storePath,
                """
                public sealed class TodoStore
                {
                    private readonly List<object> _items = new();
                    private int _id;
                    public IReadOnlyList<object> List() => _items;
                    public object Add(string title)
                    {
                        var item = new { id = ++_id, title };
                        _items.Add(item);
                        return item;
                    }
                }
                public record TodoCreate(string Title);
                """);
        }

        if (!text.Contains("TodoStore", StringComparison.Ordinal))
        {
            text = text.Replace(
                "var app = builder.Build();",
                "builder.Services.AddSingleton<TodoStore>();\nvar app = builder.Build();",
                StringComparison.Ordinal);
        }

        text = text.Replace(
            "// {{EXTRA_ENDPOINTS}}",
            """
            app.MapGet("/api/todos", (TodoStore store) => Results.Ok(store.List()));
            app.MapPost("/api/todos", (TodoStore store, TodoCreate body) =>
            {
                var item = store.Add(body.Title ?? "item");
                return Results.Created($"/api/todos", item);
            });
            // {{EXTRA_ENDPOINTS}}
            """,
            StringComparison.Ordinal);
        File.WriteAllText(program, text);
        return ["/api/todos"];
    }

    private static string[] ApplyGenericNote(string workspace, string userRequest)
    {
        var program = Path.Combine(workspace, "backend", "Program.cs");
        if (!File.Exists(program))
        {
            return [];
        }

        var text = File.ReadAllText(program);
        if (text.Contains("/api/feature-note", StringComparison.Ordinal))
        {
            return [];
        }

        EnsureExtraMarker(ref text);
        var safe = JsonSerializer.Serialize(userRequest);
        var insert =
            "app.MapGet(\"/api/feature-note\", () => Results.Ok(new { note = " + safe + " }));\n"
            + "// {{EXTRA_ENDPOINTS}}";
        text = text.Replace("// {{EXTRA_ENDPOINTS}}", insert, StringComparison.Ordinal);
        File.WriteAllText(program, text);
        return ["/api/feature-note"];
    }

    private static void EnsureExtraMarker(ref string text)
    {
        if (text.Contains("// {{EXTRA_ENDPOINTS}}", StringComparison.Ordinal))
        {
            return;
        }

        // Insert before MapFallback or app.Run
        if (text.Contains("app.MapFallbackToFile", StringComparison.Ordinal))
        {
            text = text.Replace(
                "app.MapFallbackToFile",
                "// {{EXTRA_ENDPOINTS}}\napp.MapFallbackToFile",
                StringComparison.Ordinal);
        }
        else if (text.Contains("app.Run()", StringComparison.Ordinal))
        {
            text = text.Replace(
                "app.Run()",
                "// {{EXTRA_ENDPOINTS}}\napp.Run()",
                StringComparison.Ordinal);
        }
        else
        {
            text += "\n// {{EXTRA_ENDPOINTS}}\n";
        }
    }

    private static void EnsureCalculatorMath(string workspace)
    {
        var path = Path.Combine(workspace, "backend", "Services", "CalculatorMath.cs");
        if (File.Exists(path))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            """
            namespace CalculatorApi.Services;
            public static class CalculatorMath
            {
                public static double Add(double a, double b) => a + b;
                public static double Subtract(double a, double b) => a - b;
            }
            """);
    }

    private static void PatchFrontend(string workspace, string buttonId, string apiPath, string label)
    {
        foreach (var index in new[]
                 {
                     Path.Combine(workspace, "frontend", "index.html"),
                     Path.Combine(workspace, "backend", "wwwroot", "index.html")
                 })
        {
            if (!File.Exists(index))
            {
                continue;
            }

            var html = File.ReadAllText(index);
            if (html.Contains(buttonId, StringComparison.Ordinal) || html.Contains(apiPath, StringComparison.Ordinal))
            {
                continue;
            }

            if (!html.Contains("<!-- {{EXTRA_UI}} -->", StringComparison.Ordinal))
            {
                html = html.Replace("</body>", "<!-- {{EXTRA_UI}} -->\n</body>", StringComparison.OrdinalIgnoreCase);
            }

            var snippet =
                "<button id=\"" + buttonId + "\" type=\"button\">" + label + "</button>\n"
                + "<script>\n"
                + "document.getElementById('" + buttonId + "')?.addEventListener('click', async () => {\n"
                + "  const r = await fetch('" + apiPath + "?a=5&b=2');\n"
                + "  console.log(await r.text());\n"
                + "});\n"
                + "</script>\n"
                + "<!-- {{EXTRA_UI}} -->";
            html = html.Replace("<!-- {{EXTRA_UI}} -->", snippet, StringComparison.Ordinal);
            File.WriteAllText(index, html);
        }
    }

    private static void CopyTree(string source, string dest)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, file).Replace('\\', '/');
            if (rel.StartsWith("bin/", StringComparison.OrdinalIgnoreCase)
                || rel.StartsWith("obj/", StringComparison.OrdinalIgnoreCase)
                || rel.StartsWith(".git/", StringComparison.OrdinalIgnoreCase)
                || rel.StartsWith("graphify-out/", StringComparison.OrdinalIgnoreCase)
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

    private static bool PathsEqual(string a, string b) =>
        string.Equals(
            Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, '/'),
            Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, '/'),
            StringComparison.OrdinalIgnoreCase);

    private static string Sanitize(string value)
    {
        var cleaned = new string(value.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "x" : cleaned;
    }
}
