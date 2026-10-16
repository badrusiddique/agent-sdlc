using System.Text.Json;
using AgentHub.Core.Models;

namespace AgentHub.Agents.Pipeline.Deploy;

/// <summary>
/// Deployment-agent stub for the POC: does not re-deploy. Publishes the already-live
/// sandbox URL for BuildGenix / HITL acceptance of the next environment stage.
/// </summary>
public static class SandboxDeployPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static string BuildArtifact(TaskMessage message, string? deployedUrl, string? prUrl)
    {
        var url = deployedUrl ?? message.Context.DeployedUrl ?? "";
        return JsonSerializer.Serialize(
            new
            {
                environment = "sandbox",
                status = "waiting_for_approval",
                message =
                    "Requested app is deployed to the sandbox environment. Review the URL, then approve to move to the next stage.",
                deployedUrl = url,
                prUrl = prUrl ?? message.Context.PrUrl,
                projectId = message.ProjectId,
                workItemId = message.WorkItemId,
                revision = message.OutputRevision < 1 ? 1 : message.OutputRevision,
                at = DateTimeOffset.UtcNow
            },
            JsonOptions);
    }

    public static string BuildMarkdown(string? deployedUrl) =>
        "# Sandbox deployment\n\n"
        + "The Implementation + Testing Agents finished. The app is live in **sandbox**.\n\n"
        + $"- URL: {deployedUrl ?? "(missing)"}\n"
        + "- Next: human approves promotion (stub — auto-approve in local POC `/run`).\n";
}
