using AgentHub.Agents.Pipeline.Ia;
using AgentHub.Agents.Pipeline.Ta;
using Xunit;

namespace AgentHub.Agents.Pipeline.Tests;

public sealed class DeployedSmokeGeneratorTests
{
    [Theory]
    [InlineData("Create a calculator that adds two numbers", "fe-be", "Add", "/api/add")]
    [InlineData("Create a todo list app", "fe-be", "Todos", "/api/todos")]
    [InlineData("Create a hello world app without llm", "fe-be", "Hello", "/api/hello")]
    [InlineData("Hello world with llm thought", "fe-be-llm", "Thought", "/api/thought")]
    public void DefaultCases_match_request_driven_kind(
        string request,
        string stack,
        string expectedName,
        string pathPrefix)
    {
        var cases = DeployedSmokeGenerator.DefaultCases(stack, request, "Demo");
        Assert.Equal(2, cases.Count);
        Assert.Equal("HealthOk", cases[0].Name);
        Assert.Equal(expectedName, cases[1].Name);
        Assert.StartsWith(pathPrefix, cases[1].Path);
    }

    [Fact]
    public void PrimaryCase_covers_all_app_kinds()
    {
        foreach (RequestDrivenCodegen.AppKind kind in Enum.GetValues<RequestDrivenCodegen.AppKind>())
        {
            var c = DeployedSmokeGenerator.PrimaryCase(kind);
            Assert.False(string.IsNullOrWhiteSpace(c.Path));
            Assert.StartsWith("/api/", c.Path);
        }
    }
}
