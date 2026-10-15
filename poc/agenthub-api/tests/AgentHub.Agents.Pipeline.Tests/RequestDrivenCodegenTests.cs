using AgentHub.Agents.Pipeline.Ia;

namespace AgentHub.Agents.Pipeline.Tests;

public sealed class RequestDrivenCodegenTests
{
    [Theory]
    [InlineData("Create a calculator app that adds two numbers", "fe-be", "Calculator", RequestDrivenCodegen.AppKind.Calculator)]
    [InlineData("Build a todo list API", "fe-be", "Todos", RequestDrivenCodegen.AppKind.Todo)]
    [InlineData("Hello world with a thought of the day", "fe-be-llm", "Hello", RequestDrivenCodegen.AppKind.HelloLlm)]
    [InlineData("Simple hello world page", "fe-be", "Hi", RequestDrivenCodegen.AppKind.Hello)]
    [InlineData("Inventory lookup service", "fe-be", "Inv", RequestDrivenCodegen.AppKind.Generic)]
    public void Classify_maps_requests(string request, string stack, string project, RequestDrivenCodegen.AppKind expected)
    {
        Assert.Equal(expected, RequestDrivenCodegen.Classify(request, stack, project));
    }

    [Fact]
    public void Generate_calculator_writes_add_api_not_template_copy()
    {
        var root = Path.Combine(Path.GetTempPath(), "bgx-rdc-" + Guid.NewGuid().ToString("N"));
        try
        {
            RequestDrivenCodegen.Generate(
                root,
                RequestDrivenCodegen.AppKind.Calculator,
                "p1",
                "w1",
                "Create a calculator app that adds two numbers");

            Assert.True(File.Exists(Path.Combine(root, "backend", "CalculatorApi.csproj")));
            Assert.True(File.Exists(Path.Combine(root, "backend", "Program.cs")));
            Assert.True(File.Exists(Path.Combine(root, "frontend", "index.html")));
            var program = File.ReadAllText(Path.Combine(root, "backend", "Program.cs"));
            Assert.Contains("/api/add", program, StringComparison.Ordinal);
            Assert.DoesNotContain("Materialize the checked-in", program, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Generate_todo_writes_todos_api()
    {
        var root = Path.Combine(Path.GetTempPath(), "bgx-rdc-" + Guid.NewGuid().ToString("N"));
        try
        {
            RequestDrivenCodegen.Generate(
                root,
                RequestDrivenCodegen.AppKind.Todo,
                "p1",
                "w1",
                "todo app");
            var program = File.ReadAllText(Path.Combine(root, "backend", "Program.cs"));
            Assert.Contains("/api/todos", program, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* best-effort */ }
        }
    }
}
