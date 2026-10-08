using AgentHub.Core.Models;

namespace AgentHub.Agents.Pipeline;

public static class PipelineTaskValidation
{
    public static IReadOnlyList<string> Validate(TaskMessage message)
    {
        var errors = message.Validate().ToList();
        if (!string.IsNullOrWhiteSpace(message.Stage)
            && !PipelineAgentConstants.SupportedStages.Contains(message.Stage, StringComparer.Ordinal))
        {
            errors.Add(
                $"Stage must be one of: {string.Join(", ", PipelineAgentConstants.SupportedStages)}.");
        }

        if (string.IsNullOrWhiteSpace(message.Context.UserRequest)
            && string.IsNullOrWhiteSpace(message.Context.StackProfile))
        {
            errors.Add("Context.UserRequest or Context.StackProfile is required for the pipeline agent.");
        }

        return errors;
    }
}
