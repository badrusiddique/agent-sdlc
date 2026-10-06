using System.Net;
using System.Net.Http.Json;
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
}
