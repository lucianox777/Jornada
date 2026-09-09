using System.Collections.Immutable;
using System.Text.Json;

namespace Jornada.Contracts;

/// <summary>Projeção de leitura do histórico persistido; não concede autoridade à decisão.</summary>
public sealed record IdentityCompositionAppliedHistoryRow(
    Guid DecisionId, Guid ReferenceUuid, string MembersJson, string MembersHash,
    DateTimeOffset RegisteredAt, bool HasAppliedReceipt);

public static class IdentityCompositionAppliedHistory
{
    public static IdentityCompositionHistory Validate(IdentityCompositionAppliedHistoryRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!row.HasAppliedReceipt || row.DecisionId == Guid.Empty || row.ReferenceUuid == Guid.Empty ||
            row.RegisteredAt == default || row.RegisteredAt.Offset != TimeSpan.Zero ||
            string.IsNullOrWhiteSpace(row.MembersJson) || string.IsNullOrWhiteSpace(row.MembersHash))
            throw new InvalidOperationException("Histórico aplicado sem proveniência ou identificação válida.");
        Guid[] members;
        try
        {
            members = JsonSerializer.Deserialize<Guid[]>(row.MembersJson)
                ?? throw new InvalidOperationException("Histórico aplicado sem membros.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("JSON de histórico aplicado inválido.", ex);
        }
        var canonical = IdentityCompositionCanonical.SerializeHistoryMembers(members);
        if (!string.Equals(row.MembersJson, canonical, StringComparison.Ordinal) ||
            !string.Equals(row.MembersHash, IdentityCompositionCanonical.HashUtf8(canonical), StringComparison.Ordinal))
            throw new InvalidOperationException("Conteúdo canônico ou hash do histórico aplicado divergente.");
        return new IdentityCompositionHistory(row.ReferenceUuid, row.DecisionId, members.ToImmutableArray());
    }

    public static ImmutableArray<IdentityCompositionHistory> ValidateBatch(IEnumerable<IdentityCompositionAppliedHistoryRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var result = rows.Select(Validate).ToArray();
        if (result.Select(x => (x.CompositionId, x.ReferenceUuid)).Distinct().Count() != result.Length)
            throw new InvalidOperationException("Histórico aplicado duplicado.");
        return result.OrderBy(x => x.ReferenceUuid).ThenBy(x => x.CompositionId).ToImmutableArray();
    }
}
