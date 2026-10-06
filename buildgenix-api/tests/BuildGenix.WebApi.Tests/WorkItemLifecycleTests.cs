using BuildGenix.Data.Repositories;
using BuildGenix.WebApi.Tests.Fakes;

namespace BuildGenix.WebApi.Tests;

public sealed class WorkItemLifecycleTests
{
    [Fact]
    public async Task TryCreate_does_not_report_active_conflict_for_second_draft()
    {
        var repo = new InMemoryWorkItemRepository();
        var first = await repo.TryCreateAsync("p1", "Demo", "user-a", null);
        var second = await repo.TryCreateAsync("p1", "Demo", "user-a", null);

        Assert.Equal(WorkItemCreateOutcome.Created, first.Outcome);
        Assert.Equal(WorkItemCreateOutcome.Created, second.Outcome);
        Assert.NotEqual(first.Item!.WorkItemId, second.Item!.WorkItemId);
        Assert.False(repo.HasActiveLock("p1"));
    }

    [Fact]
    public async Task TryImplement_acquires_active_lock_and_second_wi_conflicts()
    {
        var repo = new InMemoryWorkItemRepository();
        var a = (await repo.TryCreateAsync("p1", "Demo", "user-a", null)).Item!;
        var b = (await repo.TryCreateAsync("p1", "Demo", "user-a", null)).Item!;

        var started = await repo.TryImplementAsync(a.WorkItemId, summaryRevision: 1);
        Assert.Equal(WorkItemImplementOutcome.Started, started.Outcome);
        Assert.True(started.Item!.PipelineStarted);
        Assert.True(repo.HasActiveLock("p1"));

        var conflict = await repo.TryImplementAsync(b.WorkItemId, summaryRevision: 1);
        Assert.Equal(WorkItemImplementOutcome.ActiveConflict, conflict.Outcome);
        Assert.Equal(a.WorkItemId, conflict.ActiveWorkItemId);
    }

    [Fact]
    public async Task TryImplement_same_revision_is_success_noop()
    {
        var repo = new InMemoryWorkItemRepository();
        var a = (await repo.TryCreateAsync("p1", "Demo", "user-a", null)).Item!;
        await repo.TryImplementAsync(a.WorkItemId, 2);
        var again = await repo.TryImplementAsync(a.WorkItemId, 2);

        Assert.Equal(WorkItemImplementOutcome.AlreadyStarted, again.Outcome);
        Assert.True(again.Item!.PipelineStarted);
        Assert.Equal(2, again.Item.BoundSummaryRevision);
    }

    [Fact]
    public async Task TryImplement_different_revision_while_in_flight_is_open_outcome()
    {
        var repo = new InMemoryWorkItemRepository();
        var a = (await repo.TryCreateAsync("p1", "Demo", "user-a", null)).Item!;
        await repo.TryImplementAsync(a.WorkItemId, 1);
        var open = await repo.TryImplementAsync(a.WorkItemId, 2);
        Assert.Equal(WorkItemImplementOutcome.SameWiDifferentRevisionOpen, open.Outcome);
    }
}
