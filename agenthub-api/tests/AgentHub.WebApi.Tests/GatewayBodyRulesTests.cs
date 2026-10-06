using AgentHub.Infrastructure.Llm;

namespace AgentHub.WebApi.Tests;

public sealed class GatewayBodyRulesTests
{
    [Theory]
    [InlineData("openai", false)]
    [InlineData("opus", false)]
    [InlineData("sonnet1", false)]
    [InlineData("5.1", true)]
    [InlineData("5.4", true)]
    public void UsesMaxCompletionTokens_matches_ai_gateway_doc(string alias, bool expected) =>
        Assert.Equal(expected, GatewayLlmClient.UsesMaxCompletionTokens(alias));
}
