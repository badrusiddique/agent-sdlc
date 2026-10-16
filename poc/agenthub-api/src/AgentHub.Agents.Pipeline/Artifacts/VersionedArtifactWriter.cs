using System.Text.Json;
using AgentHub.Core.Abstractions;

namespace AgentHub.Agents.Pipeline.Artifacts;

/// <summary>
/// Writes the revision blob plus optional named copy and LATEST / state-context pointers.
/// </summary>
public static class VersionedArtifactWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static async Task WriteAsync(
        IArtifactBlobStore blobs,
        string projectId,
        string workItemId,
        int revision,
        string artifactKey,
        string revisionRelativePath,
        string content,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? stateExtra = null)
    {
        await blobs.WriteJsonAsync(revisionRelativePath, content, cancellationToken).ConfigureAwait(false);

        var stem = VersionedArtifactPaths.LogicalStem(artifactKey);
        if (stem is not null
            && (revisionRelativePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                || content.TrimStart().StartsWith('{')
                || content.TrimStart().StartsWith('[')))
        {
            var named = VersionedArtifactPaths.NamedVersion(projectId, workItemId, stem, revision);
            await blobs.WriteJsonAsync(named, content, cancellationToken).ConfigureAwait(false);
        }

        var latest = JsonSerializer.Serialize(
            new
            {
                projectId,
                workItemId,
                revision,
                updatedAt = DateTimeOffset.UtcNow,
                key = artifactKey,
                path = revisionRelativePath,
                named = stem is null
                    ? null
                    : VersionedArtifactPaths.NamedVersion(projectId, workItemId, stem, revision)
            },
            JsonOptions);
        await blobs
            .WriteJsonAsync(VersionedArtifactPaths.LatestPointer(projectId, workItemId), latest, cancellationToken)
            .ConfigureAwait(false);

        var state = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["projectId"] = projectId,
            ["workItemId"] = workItemId,
            ["revision"] = revision.ToString(),
            ["blobPrefix"] = VersionedArtifactPaths.RevisionPrefix(projectId, workItemId, revision),
            ["lastArtifactKey"] = artifactKey,
            ["updatedAt"] = DateTimeOffset.UtcNow.ToString("O")
        };
        if (stateExtra is not null)
        {
            foreach (var pair in stateExtra)
            {
                state[pair.Key] = pair.Value;
            }
        }

        await blobs
            .WriteJsonAsync(
                VersionedArtifactPaths.StateContext(projectId, workItemId),
                JsonSerializer.Serialize(state, JsonOptions),
                cancellationToken)
            .ConfigureAwait(false);
    }
}
