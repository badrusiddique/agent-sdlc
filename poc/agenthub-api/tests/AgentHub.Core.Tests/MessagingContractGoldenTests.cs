using System.Text.Json;
using AgentHub.Core.Messaging;
using AgentHub.Core.Models;

namespace AgentHub.Core.Tests;

public sealed class MessagingContractGoldenTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void TaskMessage_golden_deserializes_and_validates()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Contracts", "task-message.v1.json"));
        var message = JsonSerializer.Deserialize<TaskMessage>(json, JsonOptions);
        Assert.NotNull(message);
        message!.EnsureDefaults();
        Assert.Empty(message.Validate());
        Assert.Equal("task-contract-1:1", message.LogicalMessageId);
        Assert.Equal("proj-contract-1:wi-contract-1", message.StateContextId);
        Assert.Equal(
            MessageIds.LogicalMessageId(message.TaskId, message.InputVersion),
            message.LogicalMessageId);
    }

    [Fact]
    public void AgentResult_golden_deserializes()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Contracts", "agent-result.v1.json"));
        var result = JsonSerializer.Deserialize<AgentResult>(json, JsonOptions);
        Assert.NotNull(result);
        Assert.Equal(AgentResultStatus.Completed, result!.Status);
        Assert.Equal("task-contract-1:1:1", result.RunId);
        Assert.Equal(
            "proj-contract-1/wi-contract-1/v1/healthy-result.json",
            result.Artifacts["healthy"]);
    }

    [Fact]
    public void TaskMessage_Validate_does_not_require_HEALTHY_stage()
    {
        var message = new TaskMessage
        {
            WorkItemId = "wi",
            ProjectId = "p",
            Stage = "SYSTEM_DESIGN",
            TaskId = "t",
            CorrelationId = "c",
            InputVersion = 1
        };
        Assert.Empty(message.Validate());
    }
}
