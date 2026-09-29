using System.Globalization;
using System.Text.Json;
using Jornada.Contracts;

namespace Jornada.Linkage.Evaluation;

/// <summary>
/// Proposta inicial, somente diagnóstica, de projeções/passes de blocking a partir
/// das marginais públicas IBGE. Não lê Gold/Silver, não cria ruleset e não promove
/// política. Marginais não fornecem distribuição conjunta pessoa+mãe+data.
/// </summary>
public static class IbgeInitialBlockingProposal
{
    public const string SchemaVersion = "JORNADA_IBGE_INITIAL_BLOCKING_PROPOSAL_V1";
    public const string MethodVersion = "IBGE_INITIAL_BLOCKING_PROPOSAL_V1";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string Build(string publicMarginalsJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicMarginalsJson);
        using var document = JsonDocument.Parse(publicMarginalsJson);
        var root = document.RootElement;

        var schema = RequiredString(root, "schema_version");
        var reference = RequiredString(root, "reference_code");
        var sha = RequiredString(root, "reference_content_sha256").ToLowerInvariant();
        var firstNameSex = RequiredString(root, "first_name_sex");
        var surnameSex = RequiredString(root, "surname_sex");

        if (schema != IbgePublicMarginalsExchange.Schema ||
            reference != IbgePublicMarginalsExchange.Reference ||
            sha.Length != 64 || sha.Any(static c => !Uri.IsHexDigit(c)) ||
            firstNameSex != "TODOS" || surnameSex != "TODOS")
            throw new InvalidDataException(
                "Proposta inicial exige marginais públicas canônicas NOME/TODOS e SOBRENOME/TODOS.");

        var first = ReadMarginal(root, "first_names");
        var surnames = ReadMarginal(root, "surnames");
        var firstSummary = Summarize("NOME/TODOS", first);
        var surnameSummary = Summarize("SOBRENOME/TODOS", surnames);

        var output = new
        {
            schema_version = SchemaVersion,
            method_version = MethodVersion,
            nature = "DIAGNOSTIC_INITIAL_PROPOSAL_NOT_PROMOTABLE",
            reference_code = reference,
            reference_content_sha256 = sha,
            source_schema_version = schema,
            marginal_metrics = new[] { firstSummary, surnameSummary },
            projection_candidates = new object[]
            {
                new
                {
                    feature = BlockingFeatureNames.FirstName,
                    evidence_basis = "IBGE_NOME_TODOS",
                    semantics = "primeiro nome; projeção calculada compatível com a marginal de prenomes",
                    promotable = false
                },
                new
                {
                    feature = BlockingFeatureNames.Surnames,
                    evidence_basis = "IBGE_SOBRENOME_TODOS_PRESENCA",
                    semantics = "tokens técnicos pós-prenome para índice por presença; não afirma sobrenome civil estruturado nem posição",
                    promotable = false
                }
            },
            pass_hypotheses = new object[]
            {
                new
                {
                    pass_id = "ibge-initial-name-first",
                    fields = new[] { BlockingFeatureNames.FirstName },
                    selectivity = "RELATIVE_PUBLISHED_OCCURRENCE_CONCENTRATION_ONLY",
                    requires_jornada_validation = true,
                    promotable = false
                },
                new
                {
                    pass_id = "ibge-initial-surname-any",
                    fields = new[] { BlockingFeatureNames.Surnames },
                    selectivity = "RELATIVE_PUBLISHED_OCCURRENCE_CONCENTRATION_ONLY",
                    requires_jornada_validation = true,
                    promotable = false
                },
                new
                {
                    pass_id = "ibge-initial-name-first-plus-birth-year",
                    fields = new[] { BlockingFeatureNames.FirstName, BlockingFeatureNames.BirthYear },
                    selectivity = "NOT_ESTIMABLE_FROM_IBGE_MARGINALS_NO_JOINT_DISTRIBUTION",
                    requires_jornada_validation = true,
                    promotable = false
                },
                new
                {
                    pass_id = "ibge-initial-surname-any-plus-birth-year",
                    fields = new[] { BlockingFeatureNames.Surnames, BlockingFeatureNames.BirthYear },
                    selectivity = "NOT_ESTIMABLE_FROM_IBGE_MARGINALS_NO_JOINT_DISTRIBUTION",
                    requires_jornada_validation = true,
                    promotable = false
                }
            },
            safeguards = new[]
            {
                "não cria ou altera identidade.linkage_ruleset",
                "não cria, valida ou ativa modelo",
                "não interpreta marginal de SOBRENOME como frequência de último token",
                "não presume independência entre nome, sobrenome, mãe e nascimento",
                "métricas de concentração usam somente ocorrências publicadas e não são probabilidades populacionais",
                "seleção operacional posterior exige dados Jornada e gates TRAIN/VALIDATION/TEST"
            }
        };

        return JsonSerializer.Serialize(output, JsonOptions) + "\n";
    }

    private static object Summarize(string marginal, IReadOnlyList<MarginalRow> rows)
    {
        if (rows.Count == 0)
            throw new InvalidDataException($"Marginal {marginal} vazia.");

        var total = rows.Aggregate(0L, static (sum, row) => checked(sum + row.Occurrences));
        var ordered = rows.OrderByDescending(static x => x.Occurrences)
            .ThenBy(static x => x.Name, StringComparer.Ordinal)
            .ToArray();
        var max = ordered[0].Occurrences;
        var top10 = ordered.Take(10).Aggregate(0L, static (sum, row) => checked(sum + row.Occurrences));
        decimal concentration = 0m;
        foreach (var row in rows)
        {
            var share = (decimal)row.Occurrences / total;
            concentration += share * share;
        }

        return new
        {
            marginal,
            distinct_published_values = rows.Count,
            published_occurrences_sum = total,
            max_published_occurrences = max,
            max_relative_published_occurrence_share = DecimalText((decimal)max / total),
            top10_relative_published_occurrence_share = DecimalText((decimal)top10 / total),
            relative_published_occurrence_concentration = DecimalText(concentration),
            interpretation = "diagnóstico relativo às ocorrências publicadas; não é probabilidade de bloco na população"
        };
    }

    private static IReadOnlyList<MarginalRow> ReadMarginal(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"Marginal {property} ausente.");
        var rows = new List<MarginalRow>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in array.EnumerateArray())
        {
            var name = RequiredString(item, "name").Trim().ToUpperInvariant();
            if (!item.TryGetProperty("occurrences", out var occurrencesElement) ||
                !occurrencesElement.TryGetInt64(out var occurrences) || occurrences <= 0 ||
                !seen.Add(name))
                throw new InvalidDataException($"Marginal {property} contém linha inválida ou duplicada.");
            rows.Add(new MarginalRow(name, occurrences));
        }
        return rows;
    }

    private static string RequiredString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException($"Campo obrigatório ausente: {property}.");
        return value.GetString()!;
    }

    private static string DecimalText(decimal value) =>
        value.ToString("0.############################", CultureInfo.InvariantCulture);

    private sealed record MarginalRow(string Name, long Occurrences);
}
