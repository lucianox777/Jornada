using System.Text.Json;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Read-only DT23 presentation snapshot. It does not persist, schedule or invoke calibration.
/// Only the independent-evidence evaluator can determine sufficiency.
/// </summary>
public static class SampleSufficiencyDiagnosticSnapshot
{
    public sealed record Snapshot(string PolicyVersion, DateTimeOffset CutoffUtc, string State,
        string CountersJson, string ReasonsJson, string ProvenanceJson);

    public static Snapshot Create(SampleSufficiencyAssessment.Result assessment,
        string policyVersion, DateTimeOffset cutoffUtc, string sourceKind)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        if (string.IsNullOrWhiteSpace(policyVersion) || policyVersion != policyVersion.Trim())
            throw new ArgumentException("Canonical policy version required.", nameof(policyVersion));
        if (cutoffUtc == default || cutoffUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("A non-default UTC cutoff is required.", nameof(cutoffUtc));
        // Unknown or malformed provenance is an observation, not an exception:
        // it must produce INDETERMINADO and can never certify sufficiency.
        // This includes null, blank and padded source kinds.
        // Never treat synthetic or unverified source data as sufficient REAL evidence.
        // A source label is not an independent audit receipt. Fail closed until an audited collector exists.
        const bool verified = false;
        var state = !verified ? "INDETERMINADO" : assessment.State switch
        {
            SampleSufficiencyAssessment.Status.Sufficient => "VERDE",
            SampleSufficiencyAssessment.Status.Insufficient => "AMARELO",
            _ => "INDETERMINADO"
        };
        var reasons = new[] { "Independent real evidence provenance not verified; certification blocked." };
        return new Snapshot(policyVersion, cutoffUtc, state,
            JsonSerializer.Serialize(verified ? assessment.Counts.OrderBy(c => c.Stratum, StringComparer.Ordinal) : Enumerable.Empty<object>()),
            JsonSerializer.Serialize(reasons),
            JsonSerializer.Serialize(new { sourceKind = verified ? "INDEPENDENT_REAL" : "UNVERIFIED", verified }));
    }
}
