using BuildGenix.Data;

namespace BuildGenix.WebApi.Tests;

public sealed class TableCatalogTests
{
    [Fact]
    public void Built_catalog_contains_only_buildgenix_tables()
    {
        Assert.Contains(TableNames.Projects, TableCatalog.Built);
        Assert.Contains(TableNames.WmProcessedEvents, TableCatalog.Built);

        Assert.DoesNotContain(TableCatalog.Built, name =>
            name.Equals("ActivityLog", StringComparison.OrdinalIgnoreCase)
            || name.Equals("EffectLedger", StringComparison.OrdinalIgnoreCase)
            || name.Equals("AgProcessedEvents", StringComparison.OrdinalIgnoreCase));
    }
}
