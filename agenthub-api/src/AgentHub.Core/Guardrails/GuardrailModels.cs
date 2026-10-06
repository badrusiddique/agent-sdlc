namespace AgentHub.Core.Guardrails;

public enum GuardrailDecision
{
    Allow = 0,
    Block = 1
}

public static class GuardrailReasonCodes
{
    public const string InputOk = "input_ok";
    public const string OutputOk = "output_ok";
    public const string PromptInjectionSuspected = "prompt_injection_suspected";
    public const string SecretSuspected = "secret_suspected";
    public const string InputTooLong = "input_too_long";
    public const string OutputTooLong = "output_too_long";
    public const string OutputEmpty = "output_empty";
    public const string OutputShapeViolation = "output_shape_violation";
    public const string SystemPromptLeakage = "system_prompt_leakage";
}

public sealed record GuardrailVerdict(
    GuardrailDecision Decision,
    IReadOnlyList<string> ReasonCodes,
    string CorrelationId,
    string Stage);

public interface IGuardrailPipeline
{
    GuardrailVerdict EvaluateInput(string userOrDocumentText, string? correlationId);

    GuardrailVerdict EvaluateOutput(string modelText, string systemPrompt, string? correlationId);
}

/// <summary>Policy violation — dispatcher should fail without retry.</summary>
public sealed class GuardrailBlockedException : Exception
{
    public GuardrailBlockedException(GuardrailVerdict verdict)
        : base($"Guardrail blocked ({verdict.Stage}): {string.Join(',', verdict.ReasonCodes)}")
    {
        Verdict = verdict;
    }

    public GuardrailVerdict Verdict { get; }
}
