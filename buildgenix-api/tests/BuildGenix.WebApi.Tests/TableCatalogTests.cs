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

    [Fact]
    public void Built_catalog_lists_all_ten_owned_tables()
    {
        Assert.Equal(10, TableCatalog.Built.Count);
        Assert.Contains(TableNames.Artifacts, TableCatalog.Built);
        Assert.Contains(TableNames.ApprovalGates, TableCatalog.Built);
        Assert.Contains(TableNames.Approvals, TableCatalog.Built);
        Assert.Contains(TableNames.WorkItemEvents, TableCatalog.Built);
        Assert.Contains(TableNames.SgProcessedEvents, TableCatalog.Built);
        Assert.Equal(TableCatalog.Built.Count, TableCatalog.Built.Distinct(StringComparer.Ordinal).Count());
    }
}
