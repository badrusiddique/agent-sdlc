namespace AgentHub.Agents.Pipeline;

/// <summary>
/// Independent SDLC personas. Each gets its own system prompt and a filtered artifact pack —
/// later personas must not inherit the author's rationalization as shared "conversation memory".
/// </summary>
public enum PersonaKind
{
    Product,
    Architect,
    Planner,
    Developer,
    Reviewer,
    QaAutomation,
    QaReporter
}
