using AgentHub.Agents.Pipeline.Ia;

namespace AgentHub.Agents.Pipeline.Tests;

public sealed class GraphifyCodeStateBuilderTests
{
    [Fact]
    public void Normalize_embeds_file_inventory_and_graph_counts()
    {
        var root = Path.Combine(Path.GetTempPath(), "bgx-gf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Program.cs"), "class A {}");
        var graph = """{"nodes":[{"id":"1"}],"edges":[{"from":"1","to":"1"}],"communities":[]}""";
        var state = GraphifyCodeStateBuilder.Normalize(graph, root);
        Assert.Contains("graphify extract . --code-only", state, StringComparison.Ordinal);
        Assert.Contains("Program.cs", state, StringComparison.Ordinal);
        Assert.Contains("\"nodes\":", state, StringComparison.Ordinal);
        try { Directory.Delete(root, true); } catch { /* best-effort */ }
    }

    [Fact]
    public async Task BuildAsync_runs_locked_graphify_command_when_installed()
    {
        var root = Path.Combine(Path.GetTempPath(), "bgx-gf-run-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "app.py"), "def hello():\n    return 1\n");
        try
        {
            var result = await GraphifyCodeStateBuilder.BuildAsync(root, CancellationToken.None);
            Assert.Equal(GraphifyCodeStateBuilder.LockedCommand, result.Command);
            Assert.Contains("nodes", result.CodeGraphJson, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("graphify extract . --code-only", result.CodeStateJson, StringComparison.Ordinal);
            Assert.True(File.Exists(result.GraphPath));
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Install: pip install graphifyy", StringComparison.Ordinal))
        {
            // Environment without graphify — skip rather than fail the suite.
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* best-effort */ }
        }
    }
}
