using AgentHub.Agents.Pipeline.Ia;
using Xunit;

namespace AgentHub.Agents.Pipeline.Tests;

public sealed class DeliveryModeTests
{
    [Theory]
    [InlineData("Create a calculator", "New", null, DeliveryMode.Basic)]
    [InlineData("Rewrite the existing net8 calculator as net10", "Rewrite", "fixtures/legacy-net8-calculator", DeliveryMode.Rewrite)]
    [InlineData("Update the calculator app to subtract two numbers", "Existing", null, DeliveryMode.FeatureIncrement)]
    [InlineData("Migrate to .NET 10 greenfield", "New", "fixtures/legacy-net8-calculator", DeliveryMode.Rewrite)]
    public void Classify_delivery_mode(
        string request,
        string projectMode,
        string? baseline,
        DeliveryMode expected)
    {
        Dictionary<string, string>? artifacts = baseline is null
            ? null
            : new Dictionary<string, string> { ["baselineSourcePath"] = baseline };
        Assert.Equal(expected, DeliveryModeClassifier.Classify(request, projectMode, artifacts));
    }

    [Fact]
    public void Rewrite_fixture_to_net10_preserves_add()
    {
        var root = Path.Combine(Path.GetTempPath(), "bgx-rewrite-" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = GreenfieldRewriteBuilder.Rewrite(
                root,
                null,
                "p1",
                "w1",
                "Rewrite legacy calculator to net10");
            Assert.Equal("net10.0", result.TargetFramework);
            Assert.Contains(result.PreservedRoutes, r => r.Contains("/api/add", StringComparison.Ordinal));
            var csproj = Directory.EnumerateFiles(Path.Combine(root, "backend"), "*.csproj").First();
            Assert.Contains("net10.0", File.ReadAllText(csproj), StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(root, "REWRITE.md")));
            // Greenfield FE must match Playwright calculator selectors (not legacy #add-only).
            var fe = File.ReadAllText(Path.Combine(root, "frontend", "index.html"));
            Assert.Contains("id=\"a\"", fe, StringComparison.Ordinal);
            Assert.Contains("id=\"b\"", fe, StringComparison.Ordinal);
            Assert.Contains("id=\"go\"", fe, StringComparison.Ordinal);
            Assert.Contains("id=\"out\"", fe, StringComparison.Ordinal);
            Assert.DoesNotContain("id=\"add\"", fe, StringComparison.Ordinal);
            var www = File.ReadAllText(Path.Combine(root, "backend", "wwwroot", "index.html"));
            Assert.Contains("id=\"go\"", www, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Feature_increment_adds_subtract_on_prior()
    {
        var prior = Path.Combine(Path.GetTempPath(), "bgx-prior-" + Guid.NewGuid().ToString("N"));
        var next = Path.Combine(Path.GetTempPath(), "bgx-next-" + Guid.NewGuid().ToString("N"));
        try
        {
            RequestDrivenCodegen.Generate(
                prior,
                RequestDrivenCodegen.AppKind.Calculator,
                "p1",
                "w1",
                "Create a calculator");
            var result = FeatureIncrementBuilder.Apply(
                next,
                "p1",
                "w2",
                "Update the calculator app to subtract two numbers",
                prior);
            Assert.Equal(FeatureIncrementBuilder.FeatureKind.Subtract, result.Feature);
            Assert.Contains("/api/subtract", result.RoutesAdded);
            var program = File.ReadAllText(Path.Combine(next, "backend", "Program.cs"));
            Assert.Contains("/api/add", program, StringComparison.Ordinal);
            Assert.Contains("/api/subtract", program, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(prior, true); } catch { /* ignore */ }
            try { Directory.Delete(next, true); } catch { /* ignore */ }
        }
    }
}
