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
        if (cutoffUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Cutoff must be expressed in UTC.", nameof(cutoffUtc));
        // Never treat synthetic or unverified source data as sufficient REAL evidence.
        var verified = string.Equals(sourceKind, "INDEPENDENT_REAL", StringComparison.Ordinal);
        var state = !verified ? "INDETERMINADO" : assessment.State switch
        {
            SampleSufficiencyAssessment.Status.Sufficient => "VERDE",
            SampleSufficiencyAssessment.Status.Insufficient => "AMARELO",
            _ => "INDETERMINADO"
        };
        var reasons = assessment.Reasons.ToList();
        if (!verified)
            reasons.Add("Independent real evidence provenance not verified; certification blocked.");
        return new Snapshot(policyVersion, cutoffUtc, state,
            JsonSerializer.Serialize(assessment.Counts.OrderBy(c => c.Stratum, StringComparer.Ordinal)),
            JsonSerializer.Serialize(reasons),
            JsonSerializer.Serialize(new { sourceKind = verified ? "INDEPENDENT_REAL" : "UNVERIFIED", verified }));
    }
}
