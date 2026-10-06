namespace BuildGenix.WebApi.Contracts;

/// <summary>
/// Single inventory of HTTP catalog routes (D-C6). Keep controllers 1:1 with this list; no duplicates.
/// Heartbeat / health are ops and intentionally excluded.
/// </summary>
public static class CatalogRoutes
{
    public sealed record Entry(string Method, string Template, string Area);

    public static readonly IReadOnlyList<Entry> All =
    [
        new("GET", "/api/projects", "Projects"),
        new("GET", "/api/projects/{projectId}", "Projects"),
        new("POST", "/api/projects", "Projects"),
        new("PATCH", "/api/projects/{projectId}", "Projects"),
        new("GET", "/api/pending-actions", "Home"),
        new("GET", "/api/home/activity", "Home"),
        new("GET", "/api/projects/{projectId}/work-items", "WorkItems"),
        new("GET", "/api/work-items/{workItemId}", "WorkItems"),
        new("POST", "/api/projects/{projectId}/work-items", "WorkItems"),
        new("POST", "/api/work-items/{workItemId}/execute", "WorkItems"),
        new("POST", "/api/work-items/{workItemId}/answer", "WorkItems"),
        new("POST", "/api/work-items/{workItemId}/pause", "WorkItems"),
        new("POST", "/api/work-items/{workItemId}/stop", "WorkItems"),
        new("GET", "/api/work-items/{workItemId}/progress", "WorkItems"),
        new("GET", "/api/work-items/{workItemId}/telemetry", "WorkItems"),
        new("GET", "/api/work-items/{workItemId}/conversations", "Conversations"),
        new("GET", "/api/work-items/{workItemId}/conversations/{threadId}", "Conversations"),
        new("POST", "/api/work-items/{workItemId}/conversations/{threadId}/messages", "Conversations"),
        new("POST", "/api/work-items/{workItemId}/conversations/{threadId}/attachments", "Conversations"),
        new("GET", "/api/work-items/{workItemId}/conversations/{threadId}/context", "Conversations"),
        new("POST", "/api/work-items/{workItemId}/conversations/{threadId}/summaries", "Conversations"),
        new("GET", "/api/work-items/{workItemId}/conversations/{threadId}/summaries/{revision}", "Conversations"),
        new("GET", "/api/work-items/{workItemId}/artifacts", "Artifacts"),
        new("GET", "/api/work-items/{workItemId}/artifacts/{artifactId}", "Artifacts"),
        new("GET", "/api/work-items/{workItemId}/artifacts/{artifactId}/content", "Artifacts"),
        new("GET", "/api/projects/{projectId}/artifacts", "Artifacts"),
        new("GET", "/api/work-items/{workItemId}/gates", "Gates"),
        new("GET", "/api/work-items/{workItemId}/approvals", "Gates"),
        new("POST", "/api/work-items/{workItemId}/gates/{gateId}/decisions", "Gates"),
        new("GET", "/api/work-items/{workItemId}/assumptions", "Assumptions"),
        new("PATCH", "/api/work-items/{workItemId}/findings/{findingId}", "Findings"),
        new("GET", "/api/work-items/{workItemId}/activity", "Activity"),
        new("GET", "/api/work-items/{workItemId}/activity/{eventId}", "Activity"),
        new("GET", "/api/work-items/{workItemId}/runs/{runId}/log", "Activity"),
        new("GET", "/api/work-items/{workItemId}/activity/export", "Activity"),
        new("GET", "/api/projects/{projectId}/human-actions", "Project"),
        new("GET", "/api/projects/{projectId}/documents", "Library"),
        new("POST", "/api/projects/{projectId}/documents", "Library"),
        new("GET", "/api/work-items/{workItemId}/proposals", "Steering"),
        new("POST", "/api/work-items/{workItemId}/proposals/{proposalId}/decisions", "Steering"),
        new("POST", "/api/work-items/{workItemId}/proposals/bulk-decisions", "Steering"),
        new("GET", "/api/work-items/{workItemId}/interventions", "Steering"),
        new("GET", "/api/work-items/{workItemId}/steering/cost", "Steering"),
        new("POST", "/api/work-items/{workItemId}/sign-offs", "SignOff"),
        new("POST", "/api/internal/sdlc-results", "Internal"),
        new("POST", "/api/internal/sdlc-events", "Internal")
    ];

    public static int Count => All.Count;
}
