using BuildGenix.WebApi.Contracts;

namespace BuildGenix.WebApi.Tests;

public sealed class CatalogRouteInventoryTests
{
    [Fact]
    public void Catalog_has_exactly_46_http_routes()
    {
        Assert.Equal(46, CatalogRoutes.Count);
    }

    [Fact]
    public void Catalog_has_no_duplicate_method_template_pairs()
    {
        var keys = CatalogRoutes.All.Select(e => $"{e.Method} {e.Template}").ToList();
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Catalog_templates_are_rooted_under_api()
    {
        Assert.All(CatalogRoutes.All, e => Assert.StartsWith("/api/", e.Template, StringComparison.Ordinal));
    }
}
