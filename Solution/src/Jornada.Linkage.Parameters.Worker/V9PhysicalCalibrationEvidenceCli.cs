using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Jornada.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.VisualBasic.FileIO;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Physically executes the existing V9 adaptive SQL probe on a disposable
/// JornadaHarness database and seed-42 synthetic TRAIN/VALIDATION only.
/// Pair-level FS metrics are explicitly NOT post-Resolve FDR or certification.
/// </summary>
public static class V9PhysicalCalibrationEvidenceCli
{
    public static async Task RunAsync(string[] args)
    {
        if (args.Length != 5 || args[0] != "--v9-physical-probe-seed42"
            || args[2] is not ("clean" or "correlated")
            || !double.TryParse(args[4], NumberStyles.Float,
                CultureInfo.InvariantCulture, out var globalRecallTarget)
            || !double.IsFinite(globalRecallTarget)
            || globalRecallTarget is < 0 or > 1)
            throw new ArgumentException(
                "Uso: --v9-physical-probe-seed42 <observacoes.csv> <clean|correlated> <output.json> <recall-global:0..1>");

        var inputPath = Path.GetFullPath(args[1]);
        var outputPath = Path.GetFullPath(args[3]);
        if (File.Exists(outputPath) || string.Equals(inputPath, outputPath, StringComparison.Ordinal))
            throw new InvalidOperationException("Artefato físico deve ser novo e distinto do corpus.");
        var manifestPath = Path.Combine(Path.GetDirectoryName(inputPath)!, "generation-manifest.json");
        using (var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath)))
        {
            var meta = manifest.RootElement;
            if (meta.GetProperty("seed").GetUInt64() != 42UL
                || meta.GetProperty("error_profile").GetString() != args[2]
                || meta.GetProperty("population_profile").GetString() != "DEMOGRAPHIC_PRIMARY_V1")
                throw new InvalidDataException("Exige massa sintética IBGE-derived seed42 e perfil declarado.");
        }

        var rawSha = Convert.ToHexString(
            SHA256.HashData(await File.ReadAllBytesAsync(inputPath))).ToLowerInvariant();
        var observations = ReadNoCpfTrainValidation(inputPath);
        var passes = new[]
        {
            LinkageBlockingPass.Create("P_PHON_M_LAST",
                [BlockingCandidateFeatureCatalog.FullNamePhoneticPtBr,
                 BlockingCandidateFeatureCatalog.MotherLastName]),
            LinkageBlockingPass.Create("P_LAST_M_PHON",
                [BlockingCandidateFeatureCatalog.LastName,
                 BlockingCandidateFeatureCatalog.MotherFullNamePhoneticPtBr]),
            LinkageBlockingPass.Create("P_FIRST_BIRTH_YEAR",
                [BlockingCandidateFeatureCatalog.FirstName,
                 BlockingCandidateFeatureCatalog.BirthYear])
        };
        var options = new[] { .50m, .80m, .95m }.Select(t =>
            new V9FsAdaptiveOptionConfiguration(
                "T" + t.ToString("0.00", CultureInfo.InvariantCulture)
                    .Replace(".", "", StringComparison.Ordinal) + "_TF0",
                .5m, t, TfWeight: 0m)).ToArray();
        var study = Convert.ToHexString(SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(
                "V9_PHYSICAL_SEED42_TRAIN_VAL_V1|" + args[2] + "|" + rawSha)))
            .ToLowerInvariant();
        var budget = new AdaptiveCalibrationPreviewBudget(
            AdaptiveCalibrationPreviewBudget.CurrentVersion,
            study, globalRecallTarget, .01d,
            MaximumBlockingFalseDegreeP99: 1_000_000d,
            MinimumValidationIdentityGroups: 2);

        var conn = Environment.GetEnvironmentVariable("ConnectionStrings__Jornada")
            ?? throw new InvalidOperationException("Conexão JornadaHarness descartável ausente.");
        await using var sql = new SqlConnection(conn);
        await sql.OpenAsync();
        var measured = await V9PhysicalSqlAdaptiveCalibrationProbe.RunAsync(
            sql, observations, passes, options, budget, study,
            CancellationToken.None, repeatedSqlMaterializationRuns: 20);
        if (measured.RepeatedSqlMaterializationQuantiles is null
            || measured.ValidationGlobalCoverage is not { Count: 3 })
            throw new InvalidDataException("Probe não retornou medidas SQL físicas completas TF=0.");

        var diagnostic = new
        {
            version = "V9_SEED42_PHYSICAL_SQL_FS_PAIR_DIAGNOSTIC_V1",
            source_kind = "PHYSICAL_SQL_FS_PAIR_THRESHOLD_ONLY",
            profile = args[2],
            seed = 42,
            split = "VALIDATION",
            people_requested_by_generator = 30000,
            synthetic_input_sha256 = rawSha,
            study_sha256 = study,
            test_used = false,
            physical_sql_executed = true,
            final_rank_resolve_evaluated = false,
            fdr_after_resolve_measured = false,
            operationally_certified = false,
            sql_blocking_fingerprint_sha256 = measured.BlockingFingerprintSha256,
            measured.PreflightPairIncidences,
            measured.PhysicalDistinctCandidatePairs,
            measured.TrainEligibleTruePairsBeforeBlocking,
            measured.ValidationEligibleTruePairsBeforeBlocking,
            measured.TrainTruePairsRecoveredBySql,
            measured.ValidationTruePairsRecoveredBySql,
            measured.RepeatedSqlMaterializationQuantiles,
            options = measured.ValidationGlobalCoverage
        };
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath,
            JsonSerializer.Serialize(diagnostic, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            evidence = outputPath,
            physical_sql = true,
            post_resolve = false,
            validation_M_eligible = measured.ValidationEligibleTruePairsBeforeBlocking,
            validation_M_sql = measured.ValidationTruePairsRecoveredBySql,
            blocking_recall = (double)measured.ValidationTruePairsRecoveredBySql
                / measured.ValidationEligibleTruePairsBeforeBlocking,
            option_count = measured.ValidationGlobalCoverage.Count
        }));
    }

    private static V9PhysicalCalibrationObservation[] ReadNoCpfTrainValidation(string file)
    {
        using var csv = new TextFieldParser(file);
        csv.SetDelimiters(",");
        csv.HasFieldsEnclosedInQuotes = true;
        var header = csv.ReadFields() ?? throw new InvalidDataException("CSV sem cabeçalho.");
        var columns = header.Select((name, i) => (name, i)).ToDictionary(
            x => x.name, x => x.i, StringComparer.Ordinal);
        foreach (var name in new[] { "base_person_id", "particao", "gestor", "nome",
                     "nome_mae", "data_nascimento", "cpf" })
            if (!columns.ContainsKey(name))
                throw new InvalidDataException("Coluna obrigatória ausente: " + name);
        var rows = new List<V9PhysicalCalibrationObservation>();
        while (!csv.EndOfData)
        {
            var fields = csv.ReadFields() ?? throw new InvalidDataException("Linha CSV inválida.");
            if (fields.Length != header.Length)
                throw new InvalidDataException("CSV malformado.");
            string Get(string field) => fields[columns[field]];
            var partition = Get("particao");
            if (partition == "TEST")
                continue; // TEST não entra no probe ou na seleção.
            if (partition is not ("TRAIN" or "VALIDATION"))
                throw new InvalidDataException("Partição inesperada na massa.");
            if (!string.IsNullOrWhiteSpace(Get("cpf")))
                continue; // Experimento FS estritamente sem CPF.
            var birth = Get("data_nascimento");
            var dob = string.IsNullOrWhiteSpace(birth) ? (DateOnly?)null
                : DateOnly.ParseExact(birth, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            rows.Add(new V9PhysicalCalibrationObservation(
                checked(rows.Count + 1), Get("base_person_id"), partition,
                Get("gestor"), Get("nome"), Get("nome_mae"), dob));
        }
        if (rows.Count < 1000
            || !rows.Any(x => x.Partition == "TRAIN")
            || !rows.Any(x => x.Partition == "VALIDATION"))
            throw new InvalidDataException("Corpus sem CPF TRAIN/VALIDATION insuficiente.");
        return rows.ToArray();
    }
}
