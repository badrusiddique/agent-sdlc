using AgentHub.Data.Entities;

namespace AgentHub.WebApi.Tests;

public sealed class EffectLedgerEntityTests
{
    [Fact]
    public void RowKeyFor_uses_ordinal_D6()
    {
        var rk = EffectLedgerEntity.RowKeyFor("task-1", "llm", 42);
        Assert.Equal("task-1_llm_000042", rk);
    }
}
