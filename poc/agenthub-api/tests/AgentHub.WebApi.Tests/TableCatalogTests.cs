using AgentHub.Data;

namespace AgentHub.WebApi.Tests;

public sealed class TableCatalogTests
{
    [Fact]
    public void Built_catalog_contains_only_agenthub_tables()
    {
        Assert.Contains(TableNames.ActivityLog, TableCatalog.Built);
        Assert.Contains(TableNames.EffectLedger, TableCatalog.Built);
        Assert.Contains(TableNames.AgProcessedEvents, TableCatalog.Built);

        Assert.DoesNotContain(TableCatalog.Built, name =>
            name.Equals("Projects", StringComparison.OrdinalIgnoreCase)
            || name.Equals("WorkItems", StringComparison.OrdinalIgnoreCase)
            || name.Equals("SgProcessedEvents", StringComparison.OrdinalIgnoreCase)
            || name.Equals("WmProcessedEvents", StringComparison.OrdinalIgnoreCase));
    }
}
