namespace AgriSage.Domain.Features.Diagnosis.Enums;

// How the Farmer is told to read a diagnosis result (AI_DIAGNOSIS.md 2.0, D2, D3, D15). Not stored: it follows from the
// case status and the stored passed_policy of the latest successful inference.
public enum DiagnosisCertainty
{
    // The AI's answer passed the policy.
    Confident,

    // The AI answered but did not pass the policy: a top guess, no chemical treatment, no product.
    Uncertain,

    // A reviewer decided the case before Human Review was removed (2026-10-21).
    Verified
}
