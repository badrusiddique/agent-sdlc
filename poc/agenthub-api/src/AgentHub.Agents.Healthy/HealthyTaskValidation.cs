using AgentHub.Core.Models;

namespace AgentHub.Agents.Healthy;

/// <summary>HEALTHY-stage rules on top of the shared TaskMessage contract.</summary>
public static class HealthyTaskValidation
{
    public static IReadOnlyList<string> Validate(TaskMessage message)
    {
        var errors = message.Validate().ToList();
        if (!string.IsNullOrWhiteSpace(message.Stage)
            && !string.Equals(message.Stage, HealthyAgentConstants.StageCode, StringComparison.Ordinal))
        {
            errors.Add($"Stage must be {HealthyAgentConstants.StageCode} for this agent.");
        }

        return errors;
    }
}
