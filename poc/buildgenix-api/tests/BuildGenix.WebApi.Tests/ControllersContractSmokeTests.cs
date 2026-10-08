using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BuildGenix.Data.Entities;
using BuildGenix.WebApi.Contracts;

namespace BuildGenix.WebApi.Tests;

public sealed class ControllersContractSmokeTests : IClassFixture<BuildGenixApiFactory>
{
    private readonly HttpClient _client;

    public ControllersContractSmokeTests(BuildGenixApiFactory factory) =>
        _client = factory.CreateClient();

    [Fact]
    public async Task Create_project_and_two_work_items_then_implement_first()
    {
        _client.DefaultRequestHeaders.Add("X-User-Id", "tester");

        var createProject = await _client.PostAsJsonAsync(
            "/api/projects",
            new CreateProjectRequest("Controller Smoke", "from tests"));
        Assert.Equal(HttpStatusCode.Created, createProject.StatusCode);
        var project = await createProject.Content.ReadFromJsonAsync<CreateProjectResponse>();
        Assert.False(string.IsNullOrWhiteSpace(project!.ProjectId));

        var wi1 = await _client.PostAsJsonAsync(
            $"/api/projects/{project.ProjectId}/work-items",
            new CreateWorkItemRequest(null, null));
        var wi2 = await _client.PostAsJsonAsync(
            $"/api/projects/{project.ProjectId}/work-items",
            new CreateWorkItemRequest(null, null));
        Assert.Equal(HttpStatusCode.Created, wi1.StatusCode);
        Assert.Equal(HttpStatusCode.Created, wi2.StatusCode);

        var created1 = await wi1.Content.ReadFromJsonAsync<CreateWorkItemResponse>();
        var created2 = await wi2.Content.ReadFromJsonAsync<CreateWorkItemResponse>();
        Assert.Equal("I01", created1!.ConversationThreadId);
        Assert.Equal(WorkItemStatuses.Queued, created1.Status);

        var withoutSummary = await _client.PostAsJsonAsync(
            $"/api/work-items/{created1.WorkItemId}/execute",
            new ExecuteWorkItemRequest(1, null));
        Assert.Equal(HttpStatusCode.BadRequest, withoutSummary.StatusCode);

        var summary1 = await _client.PostAsJsonAsync(
            $"/api/work-items/{created1.WorkItemId}/conversations/I01/summaries",
            new ProduceIntakeSummaryRequest(null));
        Assert.Equal(HttpStatusCode.OK, summary1.StatusCode);
        var summary2 = await _client.PostAsJsonAsync(
            $"/api/work-items/{created2!.WorkItemId}/conversations/I01/summaries",
            new ProduceIntakeSummaryRequest(null));
        Assert.Equal(HttpStatusCode.OK, summary2.StatusCode);

        var execute = await _client.PostAsJsonAsync(
            $"/api/work-items/{created1.WorkItemId}/execute",
            new ExecuteWorkItemRequest(1, null));
        Assert.Equal(HttpStatusCode.OK, execute.StatusCode);
        var execBody = await execute.Content.ReadFromJsonAsync<ExecuteWorkItemResponse>();
        Assert.False(execBody!.AlreadyStarted);
        Assert.Equal(1, execBody.BoundSummaryRevision);

        var noop = await _client.PostAsJsonAsync(
            $"/api/work-items/{created1.WorkItemId}/execute",
            new ExecuteWorkItemRequest(1, null));
        var noopBody = await noop.Content.ReadFromJsonAsync<ExecuteWorkItemResponse>();
        Assert.True(noopBody!.AlreadyStarted);

        var conflict = await _client.PostAsJsonAsync(
            $"/api/work-items/{created2!.WorkItemId}/execute",
            new ExecuteWorkItemRequest(1, null));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task Stub_mutations_return_501_with_problem_json()
    {
        _client.DefaultRequestHeaders.Add("X-User-Id", "tester");
        var project = (await (await _client.PostAsJsonAsync(
            "/api/projects",
            new CreateProjectRequest("Stubs", null))).Content.ReadFromJsonAsync<CreateProjectResponse>())!;
        var wi = (await (await _client.PostAsJsonAsync(
            $"/api/projects/{project.ProjectId}/work-items",
            new CreateWorkItemRequest(null, null))).Content.ReadFromJsonAsync<CreateWorkItemResponse>())!;

        var answer = await _client.PostAsJsonAsync(
            $"/api/work-items/{wi.WorkItemId}/answer",
            new AnswerHumanActionRequest("hi"));
        Assert.Equal(HttpStatusCode.NotImplemented, answer.StatusCode);

        var docs = await _client.PostAsJsonAsync(
            $"/api/projects/{project.ProjectId}/documents",
            new RegisterProjectDocumentRequest("a.md", "text/markdown", null));
        Assert.Equal(HttpStatusCode.NotImplemented, docs.StatusCode);

        var intakeContext = await _client.GetAsync(
            $"/api/work-items/{wi.WorkItemId}/conversations/I01/context");
        Assert.Equal(HttpStatusCode.OK, intakeContext.StatusCode);
        var ctx = await intakeContext.Content.ReadFromJsonAsync<IntakeContextDto>();
        Assert.NotNull(ctx);
        Assert.Empty(ctx!.Fields);
    }

    [Fact]
    public async Task Create_work_item_persists_title()
    {
        _client.DefaultRequestHeaders.Add("X-User-Id", "tester");
        var project = (await (await _client.PostAsJsonAsync(
            "/api/projects",
            new CreateProjectRequest("Title Persist", null))).Content.ReadFromJsonAsync<CreateProjectResponse>())!;

        var create = await _client.PostAsJsonAsync(
            $"/api/projects/{project.ProjectId}/work-items",
            new CreateWorkItemRequest("Should Persist?", null));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<CreateWorkItemResponse>();

        var detail = await _client.GetFromJsonAsync<WorkItemDetailDto>(
            $"/api/work-items/{created!.WorkItemId}");
        Assert.Equal("Should Persist?", detail!.Title);
    }

    [Fact]
    public async Task Patch_project_settings_returns_501_not_ready()
    {
        _client.DefaultRequestHeaders.Add("X-User-Id", "tester");
        var project = (await (await _client.PostAsJsonAsync(
            "/api/projects",
            new CreateProjectRequest("Settings", null))).Content.ReadFromJsonAsync<CreateProjectResponse>())!;

        using var content = new StringContent(
            """{"description":null,"settings":{"k":1}}""",
            Encoding.UTF8,
            "application/json");
        var patch = await _client.PatchAsync($"/api/projects/{project.ProjectId}", content);
        Assert.Equal(HttpStatusCode.NotImplemented, patch.StatusCode);
        Assert.Equal("NotImplemented", await ReadProblemCodeAsync(patch));
    }

    [Fact]
    public async Task Stub_empty_bodies_return_400_validation_failed_with_code()
    {
        _client.DefaultRequestHeaders.Add("X-User-Id", "tester");
        var project = (await (await _client.PostAsJsonAsync(
            "/api/projects",
            new CreateProjectRequest("Validation", null))).Content.ReadFromJsonAsync<CreateProjectResponse>())!;

        var results = await _client.PostAsJsonAsync("/api/internal/sdlc-results", new { });
        Assert.Equal(HttpStatusCode.BadRequest, results.StatusCode);
        Assert.Equal("ValidationFailed", await ReadProblemCodeAsync(results));

        var docs = await _client.PostAsJsonAsync(
            $"/api/projects/{project.ProjectId}/documents",
            new { });
        Assert.Equal(HttpStatusCode.BadRequest, docs.StatusCode);
        Assert.Equal("ValidationFailed", await ReadProblemCodeAsync(docs));
    }

    [Fact]
    public async Task Unknown_run_log_returns_404()
    {
        _client.DefaultRequestHeaders.Add("X-User-Id", "tester");
        var project = (await (await _client.PostAsJsonAsync(
            "/api/projects",
            new CreateProjectRequest("RunLog", null))).Content.ReadFromJsonAsync<CreateProjectResponse>())!;
        var wi = (await (await _client.PostAsJsonAsync(
            $"/api/projects/{project.ProjectId}/work-items",
            new CreateWorkItemRequest(null, null))).Content.ReadFromJsonAsync<CreateWorkItemResponse>())!;

        var log = await _client.GetAsync($"/api/work-items/{wi.WorkItemId}/runs/r1/log");
        Assert.Equal(HttpStatusCode.NotFound, log.StatusCode);
        Assert.Equal("NotFound", await ReadProblemCodeAsync(log));
    }

    private static async Task<string?> ReadProblemCodeAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code)
            ? code.GetString()
            : null;
    }
}
