using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BuildGenix.Data.Entities;
using BuildGenix.WebApi.Contracts;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BuildGenix.WebApi.Tests;

public sealed class CatalogEndpointCoverageTests : IClassFixture<BuildGenixApiFactory>
{
    private readonly BuildGenixApiFactory _factory;
    private readonly HttpClient _client;
    private readonly string _projectId;
    private readonly string _workItemId;

    public CatalogEndpointCoverageTests(BuildGenixApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.TryAddWithoutValidation("X-User-Id", "catalog-tester");

        // Seed via in-memory repos so every WI-scoped route can resolve 200/501 instead of 404.
        var project = new ProjectEntity
        {
            RowKey = "projcatalog01",
            Name = "Catalog Project",
            CreatedBy = "catalog-tester",
            CreatedAt = DateTimeOffset.UtcNow
        };
        factory.Projects.UpsertAsync(project).GetAwaiter().GetResult();
        _projectId = project.RowKey;

        var created = factory.WorkItems.TryCreateAsync(_projectId, project.Name, "catalog-tester", null)
            .GetAwaiter().GetResult();
        _workItemId = created.Item!.WorkItemId;
    }

    [Fact]
    public void EndpointDataSource_covers_every_catalog_route_exactly_once()
    {
        using var scope = _factory.Services.CreateScope();
        var dataSource = scope.ServiceProvider.GetRequiredService<EndpointDataSource>();
        var mapped = dataSource.Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(e =>
            {
                var methods = e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods
                              ?? Array.Empty<string>();
                var template = "/" + e.RoutePattern.RawText!.TrimStart('/');
                return methods.Select(m => $"{m.ToUpperInvariant()} {NormalizeTemplate(template)}");
            })
            .Where(k => k.Contains("/api/", StringComparison.OrdinalIgnoreCase)
                        && !k.Contains("/api/connectivity", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var expected = CatalogRoutes.All
            .Select(e => $"{e.Method} {NormalizeTemplate(e.Template)}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = expected.Except(mapped, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        var extras = mapped.Except(expected, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();

        Assert.True(missing.Count == 0, "Missing mapped routes:\n" + string.Join('\n', missing));
        Assert.True(extras.Count == 0, "Unexpected extra /api routes:\n" + string.Join('\n', extras));
        Assert.Equal(46, expected.Count);
    }

    public static IEnumerable<object[]> AllCatalogCases()
    {
        foreach (var entry in CatalogRoutes.All)
        {
            yield return [entry.Method, entry.Template, entry.Area];
        }
    }

    [Theory]
    [MemberData(nameof(AllCatalogCases))]
    public async Task Each_catalog_route_responds_with_typed_json_or_problem(string method, string template, string area)
    {
        _ = area;
        var path = Expand(template);
        using var request = BuildRequest(method, path, template);
        var response = await _client.SendAsync(request);

        Assert.True(
            response.StatusCode is HttpStatusCode.OK
                or HttpStatusCode.Created
                or HttpStatusCode.Accepted
                or HttpStatusCode.BadRequest
                or HttpStatusCode.NotFound
                or HttpStatusCode.Conflict
                or HttpStatusCode.NotImplemented,
            $"{method} {path} returned {(int)response.StatusCode}");

        Assert.NotNull(response.Content.Headers.ContentType);
        Assert.Contains("json", response.Content.Headers.ContentType!.MediaType!, StringComparison.OrdinalIgnoreCase);

        var body = await response.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(body));
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }

    [Fact]
    public async Task Create_project_work_item_implement_lifecycle_still_works()
    {
        var createProject = await _client.PostAsJsonAsync(
            "/api/projects",
            new CreateProjectRequest("Lifecycle", null));
        Assert.Equal(HttpStatusCode.Created, createProject.StatusCode);
        var project = await createProject.Content.ReadFromJsonAsync<CreateProjectResponse>();

        var wi = await _client.PostAsJsonAsync(
            $"/api/projects/{project!.ProjectId}/work-items",
            new CreateWorkItemRequest(null, null));
        Assert.Equal(HttpStatusCode.Created, wi.StatusCode);
        var created = await wi.Content.ReadFromJsonAsync<CreateWorkItemResponse>();
        Assert.Equal("I01", created!.ConversationThreadId);

        var summary = await _client.PostAsJsonAsync(
            $"/api/work-items/{created.WorkItemId}/conversations/I01/summaries",
            new ProduceIntakeSummaryRequest(null));
        Assert.Equal(HttpStatusCode.OK, summary.StatusCode);

        var execute = await _client.PostAsJsonAsync(
            $"/api/work-items/{created.WorkItemId}/execute",
            new ExecuteWorkItemRequest(1, null));
        Assert.Equal(HttpStatusCode.OK, execute.StatusCode);
        var exec = await execute.Content.ReadFromJsonAsync<ExecuteWorkItemResponse>();
        Assert.False(exec!.AlreadyStarted);
    }

    private HttpRequestMessage BuildRequest(string method, string path, string template)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "POST" or "PATCH" or "PUT")
        {
            request.Content = new StringContent(SampleBody(template), Encoding.UTF8, "application/json");
        }

        return request;
    }

    private string Expand(string template) =>
        template
            .Replace("{projectId}", _projectId, StringComparison.Ordinal)
            .Replace("{workItemId}", _workItemId, StringComparison.Ordinal)
            .Replace("{threadId}", "I01", StringComparison.Ordinal)
            .Replace("{artifactId}", "art-missing", StringComparison.Ordinal)
            .Replace("{gateId}", "gate-missing", StringComparison.Ordinal)
            .Replace("{findingId}", "finding-1", StringComparison.Ordinal)
            .Replace("{eventId}", "event-missing", StringComparison.Ordinal)
            .Replace("{runId}", "run-1", StringComparison.Ordinal)
            .Replace("{proposalId}", "prop-1", StringComparison.Ordinal)
            .Replace("{revision}", "1", StringComparison.Ordinal);

    private static string SampleBody(string template) => template switch
    {
        "/api/projects" => """{"name":"From catalog test","description":null}""",
        "/api/projects/{projectId}" => """{"description":"patched","settings":null}""",
        "/api/projects/{projectId}/work-items" => """{"title":null,"idempotencyKey":null}""",
        "/api/work-items/{workItemId}/execute" => """{"summaryRevision":1,"summaryArtifactId":null}""",
        "/api/work-items/{workItemId}/answer" => """{"answer":"human reply"}""",
        "/api/work-items/{workItemId}/pause" => """{"reason":null}""",
        "/api/work-items/{workItemId}/stop" => """{"reason":null}""",
        "/api/work-items/{workItemId}/conversations/{threadId}/messages" => """{"content":"hello","clientMessageId":null}""",
        "/api/work-items/{workItemId}/conversations/{threadId}/attachments" => """{}""",
        "/api/work-items/{workItemId}/conversations/{threadId}/summaries" => """{"force":null}""",
        "/api/work-items/{workItemId}/gates/{gateId}/decisions" =>
            """{"decision":"Approved","artifactId":"a1","artifactRevision":1,"feedback":null}""",
        "/api/work-items/{workItemId}/findings/{findingId}" =>
            """{"artifactKey":"review","disposition":"Open","note":null}""",
        "/api/projects/{projectId}/documents" => """{"name":"doc.md","contentType":"text/markdown","blobPath":null}""",
        "/api/work-items/{workItemId}/proposals/{proposalId}/decisions" => """{"decision":"Approved"}""",
        "/api/work-items/{workItemId}/proposals/bulk-decisions" => """{"decision":"Discarded","proposalIds":null}""",
        "/api/work-items/{workItemId}/sign-offs" =>
            """{"artifactId":"a1","artifactRevision":1,"accepted":true}""",
        "/api/internal/sdlc-results" => """{"messageId":"m1","payloadJson":"{}"}""",
        "/api/internal/sdlc-events" => """{"messageId":"m2","payloadJson":"{}"}""",
        _ => "{}"
    };

    private static string NormalizeTemplate(string template)
    {
        // ASP.NET may emit {revision:int}; catalog uses {revision}.
        return template
            .Replace("{revision:int}", "{revision}", StringComparison.OrdinalIgnoreCase)
            .TrimEnd('/');
    }
}
