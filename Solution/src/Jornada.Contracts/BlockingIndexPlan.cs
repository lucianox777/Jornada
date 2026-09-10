using System.Security.Cryptography;
using System.Text;

namespace Jornada.Contracts;

/// <summary>
/// Requisito físico de um passe de blocking. A ordem das chaves é semântica:
/// um índice atende ao passe quando começa pelo mesmo prefixo ordenado.
/// </summary>
public sealed record BlockingIndexRequirement(
    string PassId,
    IReadOnlyList<string> OrderedKeys);

/// <summary>
/// Índice físico conhecido ou proposto. O nome não identifica a versão da regra:
/// índices equivalentes devem ser reutilizáveis entre versões.
/// </summary>
public sealed record BlockingPhysicalIndex(
    string Name,
    IReadOnlyList<string> OrderedKeys);

public sealed record BlockingIndexAssignment(
    string PassId,
    IReadOnlyList<string> OrderedKeys,
    string IndexName,
    bool RequiresCreation);

/// <summary>
/// Plano determinístico de suporte físico para uma versão de regras de blocking.
/// Não executa DDL. Apenas decide reutilização/proposição de índices.
/// </summary>
public sealed record BlockingIndexPlan(
    string RuleSetVersion,
    IReadOnlyList<BlockingIndexAssignment> Assignments,
    IReadOnlyList<BlockingPhysicalIndex> ProposedIndexes)
{
    public const string PlannerVersion = "BLOCKING_INDEX_PLAN_V1";

    public static BlockingIndexPlan Create(
        string ruleSetVersion,
        IEnumerable<BlockingIndexRequirement> requirements,
        IEnumerable<BlockingPhysicalIndex>? existingIndexes = null,
        int maxProposedIndexes = 8)
    {
        if (string.IsNullOrWhiteSpace(ruleSetVersion))
            throw new ArgumentException("Rule-set version is required.", nameof(ruleSetVersion));
        if (maxProposedIndexes < 0)
            throw new ArgumentOutOfRangeException(nameof(maxProposedIndexes));

        ArgumentNullException.ThrowIfNull(requirements);

        var normalizedRequirements = requirements
            .Select(NormalizeRequirement)
            .OrderBy(static x => x.PassId, StringComparer.Ordinal)
            .ToArray();

        if (normalizedRequirements.Length == 0)
            throw new ArgumentException("At least one blocking index requirement is required.", nameof(requirements));

        if (normalizedRequirements
            .Select(static x => x.PassId)
            .Distinct(StringComparer.Ordinal)
            .Count() != normalizedRequirements.Length)
        {
            throw new ArgumentException("Pass identifiers must be unique.", nameof(requirements));
        }

        var existing = (existingIndexes ?? Array.Empty<BlockingPhysicalIndex>())
            .Select(NormalizeIndex)
            .OrderBy(static x => x.Name, StringComparer.Ordinal)
            .ToArray();

        var proposedBySignature = new Dictionary<string, BlockingPhysicalIndex>(StringComparer.Ordinal);
        var assignments = new List<BlockingIndexAssignment>(normalizedRequirements.Length);

        foreach (var requirement in normalizedRequirements)
        {
            var reusable = existing.FirstOrDefault(index => Covers(index.OrderedKeys, requirement.OrderedKeys));
            if (reusable is not null)
            {
                assignments.Add(new BlockingIndexAssignment(
                    requirement.PassId,
                    requirement.OrderedKeys,
                    reusable.Name,
                    RequiresCreation: false));
                continue;
            }

            // Um índice já proposto para uma combinação mais ampla também pode atender
            // requisitos posteriores cujo conjunto ordenado seja seu prefixo.
            var proposedReusable = proposedBySignature.Values
                .OrderBy(static x => x.Name, StringComparer.Ordinal)
                .FirstOrDefault(index => Covers(index.OrderedKeys, requirement.OrderedKeys));

            if (proposedReusable is not null)
            {
                assignments.Add(new BlockingIndexAssignment(
                    requirement.PassId,
                    requirement.OrderedKeys,
                    proposedReusable.Name,
                    RequiresCreation: true));
                continue;
            }

            var signature = Signature(requirement.OrderedKeys);
            if (!proposedBySignature.TryGetValue(signature, out var proposed))
            {
                if (proposedBySignature.Count >= maxProposedIndexes)
                    throw new InvalidOperationException(
                        $"Blocking index plan exceeds the configured limit of {maxProposedIndexes} proposed indexes.");

                proposed = new BlockingPhysicalIndex(
                    BuildStableIndexName(requirement.OrderedKeys),
                    requirement.OrderedKeys);
                proposedBySignature.Add(signature, proposed);
            }

            assignments.Add(new BlockingIndexAssignment(
                requirement.PassId,
                requirement.OrderedKeys,
                proposed.Name,
                RequiresCreation: true));
        }

        return new BlockingIndexPlan(
            ruleSetVersion.Trim(),
            assignments,
            proposedBySignature.Values.OrderBy(static x => x.Name, StringComparer.Ordinal).ToArray());
    }

    private static BlockingIndexRequirement NormalizeRequirement(BlockingIndexRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        if (string.IsNullOrWhiteSpace(requirement.PassId))
            throw new ArgumentException("Pass identifier is required.");

        return new BlockingIndexRequirement(
            requirement.PassId.Trim(),
            NormalizeKeys(requirement.OrderedKeys));
    }

    private static BlockingPhysicalIndex NormalizeIndex(BlockingPhysicalIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);
        if (string.IsNullOrWhiteSpace(index.Name))
            throw new ArgumentException("Index name is required.");

        return new BlockingPhysicalIndex(index.Name.Trim(), NormalizeKeys(index.OrderedKeys));
    }

    private static IReadOnlyList<string> NormalizeKeys(IReadOnlyList<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var normalized = keys
            .Select(static x => x?.Trim() ?? string.Empty)
            .ToArray();

        if (normalized.Length == 0 || normalized.Any(static x => x.Length == 0))
            throw new ArgumentException("Every index requirement must contain non-empty ordered keys.");
        if (normalized.Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalized.Length)
            throw new ArgumentException("Index keys cannot be repeated in the same requirement.");

        return normalized;
    }

    private static bool Covers(IReadOnlyList<string> indexKeys, IReadOnlyList<string> requiredKeys)
    {
        if (indexKeys.Count < requiredKeys.Count)
            return false;

        for (var i = 0; i < requiredKeys.Count; i++)
        {
            if (!string.Equals(indexKeys[i], requiredKeys[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static string Signature(IReadOnlyList<string> keys) =>
        string.Join("\u001f", keys.Select(static x => x.ToUpperInvariant()));

    private static string BuildStableIndexName(IReadOnlyList<string> keys)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Signature(keys)));
        return $"IX_LINKAGE_BLOCK_{Convert.ToHexString(hash.AsSpan(0, 6))}";
    }
}
