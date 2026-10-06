using System.Net;
using System.Net.Http.Json;
using BuildGenix.Data.Entities;
using BuildGenix.Data.Repositories;
using BuildGenix.WebApi.Contracts;
using BuildGenix.WebApi.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

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

        var pending = await _client.GetAsync("/api/pending-actions");
        Assert.Equal(HttpStatusCode.OK, pending.StatusCode);

        var activity = await _client.GetAsync("/api/home/activity");
        Assert.Equal(HttpStatusCode.OK, activity.StatusCode);
    }

    [Theory]
    [InlineData("/api/projects")]
    [InlineData("/api/pending-actions")]
    [InlineData("/api/home/activity")]
    public async Task Catalog_get_routes_respond(string path)
    {
        var response = await _client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

public sealed class BuildGenixApiFactory : WebApplicationFactory<Program>
{
    private readonly InMemoryProjectRepository _projects = new();
    private readonly InMemoryWorkItemRepository _workItems = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureTestServices(services =>
        {
            RemoveHostedServices(services);
            services.RemoveAll<IProjectRepository>();
            services.RemoveAll<IWorkItemRepository>();
            services.AddSingleton<IProjectRepository>(_projects);
            services.AddSingleton<IWorkItemRepository>(_workItems);
        });
    }

    private static void RemoveHostedServices(IServiceCollection services)
    {
        var hosted = services.Where(d => d.ServiceType == typeof(IHostedService)).ToList();
        foreach (var descriptor in hosted)
        {
            services.Remove(descriptor);
        }
    }
}
