using AgentHub.Agents.Healthy;

namespace AgentHub.Agents.Healthy.Tests;

public sealed class PromptLoaderTests
{
    [Fact]
    public void LoadSystemPrompt_returns_non_empty_file_content()
    {
        var prompt = PromptLoader.LoadSystemPrompt();
        Assert.False(string.IsNullOrWhiteSpace(prompt));
        Assert.Contains("agenthub-health-agent", prompt, StringComparison.OrdinalIgnoreCase);
    }
}
