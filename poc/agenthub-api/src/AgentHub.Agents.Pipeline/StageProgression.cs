using AgentHub.Core.Messaging;
using AgentHub.Core.Models;

namespace AgentHub.Agents.Pipeline;

/// <summary>
/// Event-driven stage chain: each agent publishes the next TaskMessage to the next agent topic.
/// No BuildGenix orchestrator loop.
/// </summary>
public static class StageProgression
{
    /// <summary>
    /// Returns the next stage after a successful completion, or null when the pipeline is terminal.
    /// </summary>
    public static string? NextStageAfterSuccess(string completedStage) =>
        completedStage.ToUpperInvariant() switch
        {
            "PRD" => PipelineAgentConstants.StageSystemDesign,
            "SYSTEM_DESIGN" => PipelineAgentConstants.StageIaPlan,
            "IA_PLAN" => PipelineAgentConstants.StageIaCodegen,
            "IA_CODEGEN" or "IMPLEMENTATION" or "TESTING" => PipelineAgentConstants.StageIaReview,
            "IA_REVIEW" => PipelineAgentConstants.StageIaPushDeploy,
            "IA_PUSH_DEPLOY" or "DEPLOY_DEV" => PipelineAgentConstants.StageTaGenerate,
            "TA_GENERATE" => PipelineAgentConstants.StageTaVerify,
            "TA_VERIFY" => PipelineAgentConstants.StageTaReport,
            "TA_REPORT" => PipelineAgentConstants.StageTaMerge,
            "TA_MERGE" => PipelineAgentConstants.StageDeploySandbox,
            "DEPLOY_SANDBOX" => null,
            _ => null
        };

    public static TaskMessage BuildNextMessage(
        TaskMessage current,
        AgentResult result,
        string nextStage,
        int? outputRevisionOverride = null)
    {
        var next = new TaskMessage
        {
            WorkItemId = current.WorkItemId,
            ProjectId = current.ProjectId,
            Stage = nextStage,
            TaskId = Guid.CreateVersion7().ToString("N"),
            CorrelationId = current.CorrelationId,
            CausationId = $"{current.Stage}:{current.TaskId}",
            InputVersion = Math.Max(1, current.InputVersion + 1),
            OutputRevision = outputRevisionOverride ?? Math.Max(1, current.OutputRevision),
            PipelineVersion = current.PipelineVersion,
            StateContextId = current.StateContextId,
            BlobPrefix = $"{current.ProjectId}/{current.WorkItemId}/v{outputRevisionOverride ?? Math.Max(1, current.OutputRevision)}/",
            Config = current.Config,
            Context = CloneContext(current.Context, result),
            Artifacts = MergeArtifacts(current.Artifacts, result.Artifacts)
        };
        next.EnsureDefaults();
        return next;
    }

    /// <summary>TA RouteBack → IA_CODEGEN with bumped artifact revision.</summary>
    public static TaskMessage BuildRouteBackMessage(TaskMessage current, AgentResult result)
    {
        var rev = Math.Max(1, current.OutputRevision) + 1;
        var next = BuildNextMessage(current, result, PipelineAgentConstants.StageIaCodegen, rev);
        next.Context.DeploymentAction = null;
        // Drop one-shot probe so TA does not loop forever.
        next.Artifacts.Remove("taRoutebackProbe");
        return next;
    }

    public static bool IsRouteBack(AgentResult result) =>
        result.Outputs is not null
        && result.Outputs.TryGetValue("signal.kind", out var kind)
        && string.Equals(kind, "RouteBack", StringComparison.OrdinalIgnoreCase);

    private static TaskMessageContext CloneContext(TaskMessageContext ctx, AgentResult result)
    {
        var clone = new TaskMessageContext
        {
            UserRequest = ctx.UserRequest,
            ProjectName = ctx.ProjectName,
            ProjectSlug = ctx.ProjectSlug,
            ProjectMode = ctx.ProjectMode,
            StackProfile = ctx.StackProfile,
            TemplateId = ctx.TemplateId,
            Environment = ctx.Environment,
            DeploymentAction = ctx.DeploymentAction,
            WorkBranch = ctx.WorkBranch,
            HeadCommit = result.Outputs?.GetValueOrDefault("headCommit") ?? ctx.HeadCommit,
            WorkspaceRoot = result.Outputs?.GetValueOrDefault("workspaceRoot") ?? ctx.WorkspaceRoot,
            DeployedUrl = result.Outputs?.GetValueOrDefault("deployedUrls")
                          ?? result.Outputs?.GetValueOrDefault("deployedUrl")
                          ?? ctx.DeployedUrl,
            PrUrl = result.Outputs?.GetValueOrDefault("prUrl") ?? ctx.PrUrl,
            PrNumber = ctx.PrNumber,
            BaselineArtifacts = new Dictionary<string, string>(ctx.BaselineArtifacts, StringComparer.Ordinal),
            AutoApprove = ctx.AutoApprove,
            SimulateTaRouteback = ctx.SimulateTaRouteback,
            RouteBackCount = ctx.RouteBackCount
        };

        if (result.Outputs is not null
            && result.Outputs.TryGetValue("prNumber", out var pn)
            && int.TryParse(pn, out var n))
        {
            clone.PrNumber = n;
        }

        return clone;
    }

    private static Dictionary<string, string> MergeArtifacts(
        Dictionary<string, string> prior,
        Dictionary<string, string>? fromResult)
    {
        var merged = new Dictionary<string, string>(prior, StringComparer.Ordinal);
        if (fromResult is null)
        {
            return merged;
        }

        foreach (var pair in fromResult)
        {
            merged[pair.Key] = pair.Value;
        }

        return merged;
    }
}
