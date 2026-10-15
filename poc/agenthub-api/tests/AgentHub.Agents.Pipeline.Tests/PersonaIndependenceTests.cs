using AgentHub.Agents.Pipeline;

namespace AgentHub.Agents.Pipeline.Tests;

public sealed class PersonaIndependenceTests
{
    [Theory]
    [InlineData(PersonaKind.Product, "Independent Product")]
    [InlineData(PersonaKind.Architect, "Independent Solution Architect")]
    [InlineData(PersonaKind.Planner, "Independent Implementation Planner")]
    [InlineData(PersonaKind.Developer, "Independent Developer")]
    [InlineData(PersonaKind.Reviewer, "Independent Code Reviewer")]
    [InlineData(PersonaKind.QaAutomation, "Independent QA Automation")]
    [InlineData(PersonaKind.QaReporter, "Independent QA Lead")]
    public void LoadPersonaSystemPrompt_is_distinct_and_declares_independence(
        PersonaKind persona,
        string expectedMarker)
    {
        var prompt = PromptLoader.LoadPersonaSystemPrompt(persona);
        Assert.False(string.IsNullOrWhiteSpace(prompt));
        Assert.Contains(expectedMarker, prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Reviewer_prompt_forbids_trusting_implementer_narrative()
    {
        var prompt = PromptLoader.LoadPersonaSystemPrompt(PersonaKind.Reviewer);
        Assert.Contains("did NOT implement", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do NOT trust the implementer", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void QaAutomation_prompt_forbids_cheerleading_and_requires_live_url()
    {
        var prompt = PromptLoader.LoadPersonaSystemPrompt(PersonaKind.QaAutomation);
        Assert.Contains("did NOT implement or code-review", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("deployed URL", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("approve the review", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Persona_prompts_are_pairwise_distinct()
    {
        var kinds = Enum.GetValues<PersonaKind>();
        var texts = kinds.Select(PromptLoader.LoadPersonaSystemPrompt).ToArray();
        for (var i = 0; i < texts.Length; i++)
        {
            for (var j = i + 1; j < texts.Length; j++)
            {
                Assert.NotEqual(texts[i], texts[j]);
            }
        }
    }
}
