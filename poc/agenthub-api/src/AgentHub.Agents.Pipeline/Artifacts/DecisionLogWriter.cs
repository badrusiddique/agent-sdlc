using System.Text.Json;
using System.Text.Json.Nodes;
using AgentHub.Core.Abstractions;

namespace AgentHub.Agents.Pipeline.Artifacts;

/// <summary>
/// Per-work-item decision log so each agent (and the UI) can see what was decided so far.
/// Crash-safe: written under the revision prefix and mirrored as DECISION_LOG_V{n}.json + DECISION_LOG.json.
/// </summary>
public static class DecisionLogWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static async Task AppendAsync(
        IArtifactBlobStore blobs,
        string projectId,
        string workItemId,
        int revision,
        string blobPrefix,
        string agent,
        string stage,
        string phase,
        string summary,
        IReadOnlyList<string>? decisions = null,
        IReadOnlyDictionary<string, string>? extras = null,
        CancellationToken cancellationToken = default)
    {
        var rev = Math.Max(1, revision);
        var prefix = blobPrefix.EndsWith('/') ? blobPrefix : blobPrefix + "/";
        var revPath = prefix + "decision-log.json";
        var rootPath = VersionedArtifactPaths.WorkItemRoot(projectId, workItemId) + "DECISION_LOG.json";
        var namedPath = VersionedArtifactPaths.NamedVersion(projectId, workItemId, "DECISION_LOG", rev);

        var existing = await blobs.TryReadJsonAsync(revPath, cancellationToken).ConfigureAwait(false)
                       ?? await blobs.TryReadJsonAsync(rootPath, cancellationToken).ConfigureAwait(false);

        JsonObject root;
        if (!string.IsNullOrWhiteSpace(existing))
        {
            try
            {
                root = JsonNode.Parse(existing) as JsonObject ?? new JsonObject();
            }
            catch (JsonException)
            {
                root = new JsonObject();
            }
        }
        else
        {
            root = new JsonObject();
        }

        root["projectId"] = projectId;
        root["workItemId"] = workItemId;
        root["revision"] = rev;
        root["updatedAt"] = DateTimeOffset.UtcNow.ToString("O");

        var entries = root["entries"] as JsonArray ?? new JsonArray();
        var entry = new JsonObject
        {
            ["at"] = DateTimeOffset.UtcNow.ToString("O"),
            ["agent"] = agent,
            ["stage"] = stage,
            ["phase"] = phase,
            ["summary"] = summary,
            ["decisions"] = new JsonArray((decisions ?? Array.Empty<string>()).Select(d => JsonValue.Create(d)).ToArray())
        };
        if (extras is not null)
        {
            var extraObj = new JsonObject();
            foreach (var pair in extras)
            {
                extraObj[pair.Key] = pair.Value;
            }

            entry["extras"] = extraObj;
        }

        entries.Add(entry);
        root["entries"] = entries;

        // Rolling one-paragraph handoff for the next agent / UI headline.
        root["decisionSummary"] = BuildRollingSummary(entries);

        var json = root.ToJsonString(JsonOptions);
        await blobs.WriteJsonAsync(revPath, json, cancellationToken).ConfigureAwait(false);
        await blobs.WriteJsonAsync(rootPath, json, cancellationToken).ConfigureAwait(false);
        await blobs.WriteJsonAsync(namedPath, json, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<string?> TryLoadLatestAsync(
        IArtifactBlobStore blobs,
        string projectId,
        string workItemId,
        string? blobPrefix,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(blobPrefix))
        {
            var prefix = blobPrefix.EndsWith('/') ? blobPrefix : blobPrefix + "/";
            var fromRev = await blobs.TryReadJsonAsync(prefix + "decision-log.json", cancellationToken)
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(fromRev))
            {
                return fromRev;
            }
        }

        return await blobs
            .TryReadJsonAsync(
                VersionedArtifactPaths.WorkItemRoot(projectId, workItemId) + "DECISION_LOG.json",
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static string BuildRollingSummary(JsonArray entries)
    {
        var parts = new List<string>();
        foreach (var node in entries)
        {
            if (node is not JsonObject o)
            {
                continue;
            }

            var agent = o["agent"]?.GetValue<string>() ?? "?";
            var stage = o["stage"]?.GetValue<string>() ?? "?";
            var summary = o["summary"]?.GetValue<string>() ?? "";
            parts.Add($"{agent}/{stage}: {summary}");
        }

        if (parts.Count == 0)
        {
            return "No decisions recorded yet.";
        }

        // Keep UI-friendly length.
        var joined = string.Join(" → ", parts);
        return joined.Length <= 1200 ? joined : joined[..1186] + "...[truncated]";
    }
}
