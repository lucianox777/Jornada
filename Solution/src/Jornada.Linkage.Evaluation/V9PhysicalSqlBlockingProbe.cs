using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jornada.Contracts;
using Jornada.Linkage.Runner;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Evaluation;

/// <summary>
/// Physical SQL join over the EXACT canonical name/birth blocking keys of the
/// combined candidate-only C1 policy. Counts ALL distinct eligible pairs in
/// that SQL union, never adjacent U sampled pairs. Does not claim an active
/// VERSIONED_ALIAS ruleset or production TF.
/// </summary>
public static class V9PhysicalSqlBlockingProbe
{
    public const string Version = "V9_PHYSICAL_SQL_ACTIVE_DYNAMIC_PLUS_C1_FULL_CANDIDATE_CENSUS_V1";
    public const string BlockingPolicy = "ACTIVE_LINKAGE_RULESET_SQL_UNION_" + CombinedIdentityCandidatePlanner.MethodVersion;
    public const string Scope = "ACTUAL_SQL_ACTIVE_DYNAMIC_PASSES_PLUS_C1_CURRENT_KEYS_NO_ALIAS_HISTORY";
    private const int HardPairCap = 1_000_000;
    private sealed record Observation(int Id, string Base, string Partition, string Gestor,
        string? Name, string? Mother, DateOnly? Birth, string? Cpf,
        string? NameFull, string? MotherFull, string? NamePhonetic, string? MotherPhonetic);
    private sealed record Frozen(string Version, ulong SourceSeed, string SourceInputFingerprint,
        decimal ThresholdLogOdds, FellegiSunterJointNominalCellV9[] Cells,
        Dictionary<string, decimal> Parameters);

    public sealed record Split(
        string Partition, long EligibleTrueInterGestorNoCpfPairs,
        long SqlCandidateTruePairs, long SqlCandidateFalsePairs,
        long TruePairsLostBySqlBlocking,
        decimal FalseCandidatesPerRetrievedTrue,
        decimal FalseCandidatesPerEligibleTrue,
        decimal FalseCandidateShare,
        long AcceptedTruePairs, long AcceptedWrongPairs,
        long RejectedTruePairsIncludingLostByBlocking, long RejectedFalseCandidates,
        decimal RecallAgainstEligibleTrue,
        decimal? ConditionalFalsePositiveRateAmongFalseCandidates,
        decimal? WrongLinkRateAmongAcceptedPairs,
        decimal? WrongLinksPerCorrectLink);

    /// <summary>
    /// Pre-registered alternative two-evidence SQL passes. Every count is
    /// NEW against the unchanged ACTIVE + C1 baseline, NOT against the other
    /// alternatives. These rows are diagnostic; NEVER merged into #v9pairs.
    /// TEST is reporting-only; no policy selection from TEST.
    /// </summary>
    public sealed record BlockingPassSensitivity(
        string PassId, string Partition,
        long ExtraTruePairs, long ExtraFalsePairs,
        long ExtraAcceptedTruePairs, long ExtraAcceptedWrongPairs,
        decimal? ExtraFalseCandidatesPerTrue,
        decimal? ExtraWrongLinkShareAmongAccepted);

    public const string BlockingAblationVersion = "V9_SQL_HARNESS_SIX_PASSES_V1_BASELINE_UNCHANGED";

    public sealed record Result(
        string Version, string Scope, string BlockingPolicy, string ProjectionVersion,
        ulong Seed, int CorpusPeople, int Observations, string CorpusInputFingerprint,
        string ObservationsSha256, string FrozenModelSha256,
        ulong FrozenSourceSeed, string FrozenSourceInputFingerprint,
        decimal FrozenThresholdLogOdds, long SqlCandidatePairsTotal,
        int CandidatePairHardCap, IReadOnlyList<Split> Splits,
        string Verdict, string OperationalConclusion)
    {
        public string? ActiveRulesetFingerprint { get; init; }
        public int ActiveRulesetPassCount { get; init; }
        public long ActiveRulesetNewCandidatePairs { get; init; }
        public V9SyntheticBaseFrequencyAudit.Report? TrainBaseFrequencyAudit { get; init; }
        public V9BlockingTruthLossAudit.Report? TruePairLossAudit { get; init; }
        public V9SqlBlockingFrameParityAudit.Report? ActiveSameFrameParityAudit { get; init; }
        public V9SqlConditionedNominalSupportAudit.Report? TrainSqlNominalSupportAudit { get; init; }
        public V9SqlCandidateFanoutAudit.Report? CandidateFanoutAudit { get; init; }
        public V9SqlConditionedBirthSupportAudit.Report? TrainSqlBirthSupportAudit { get; init; }
        public V9SqlConditionalMuIdentifiabilityAudit.Report? TrainSqlMuIdentifiabilityAudit { get; init; }
        public V9SqlBaseClusterConcentrationAudit.Report? TrainSqlBaseClusterConcentrationAudit { get; init; }
        public string? BlockingAblationContract { get; init; }
        public IReadOnlyList<BlockingPassSensitivity>? PreRegisteredPassSensitivity { get; init; }
        public V9SqlTrainValidationUnionSearch.Report? TrainValidationUnionSearch { get; init; }
        public V9SqlTrainValidationUnionFanoutAudit.Report? TrainValidationUnionFanout { get; init; }
        public V9SqlBirthYearPassFamilyAudit.Report? BirthYearPassFamilyTrainValidation { get; init; }
        public V9SqlElevenPassResidualAudit.Report? ElevenPassResidualTrainValidation { get; init; }
        public V9SqlNamePrefix3BirthAudit.Report? Prefix3ExactBirthTrainValidation { get; init; }
        public V9SqlTwoPrefix3Audit.Report? OtherPrefix3PairsTrainValidation { get; init; }
    }

    public static async Task<Result> RunAsync(
        SqlConnection connection, string corpusRoot, string frozenPath,
        ulong seed, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") != "Development")
            throw new InvalidOperationException("Blocking físico sintético exige DOTNET_ENVIRONMENT=Development.");
        if (connection.State != ConnectionState.Open)
            throw new InvalidOperationException("Conexão SQL deve estar aberta.");
        await using (var verify = new SqlCommand("SELECT DB_NAME();", connection))
        {
            var database = (string?)await verify.ExecuteScalarAsync(ct);
            if (database != "JornadaHarness")
                throw new InvalidOperationException("Proibida execução fora da base sintética isolada JornadaHarness.");
        }
        var manifestPath = Path.Combine(corpusRoot, "generation-manifest.json");
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, ct));
        var meta = manifest.RootElement;
        if (meta.GetProperty("seed").GetUInt64() != seed
            || meta.GetProperty("population_profile").GetString() != "DEMOGRAPHIC_PRIMARY_V1")
            throw new InvalidDataException("Fingerprint/profiling do corpus sintético diverge.");
        var fingerprint = meta.GetProperty("input_fingerprint_sha256").GetString()!;
        var outputs = meta.GetProperty("outputs");
        var expectedObservationSha = outputs.EnumerateArray()
            .Single(x => x.GetProperty("path").GetString() == "observacoes.csv")
            .GetProperty("sha256").GetString()!;
        var observationsPath = Path.Combine(corpusRoot, "observacoes.csv");
        var observationBytes = await File.ReadAllBytesAsync(observationsPath, ct);
        var observationSha = Convert.ToHexString(SHA256.HashData(observationBytes));
        if (!string.Equals(expectedObservationSha, observationSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("SHA-256 do corpus não coincide com manifesto.");
        var rawModel = (await File.ReadAllTextAsync(frozenPath, ct)).TrimEnd('\n', '\r');
        var modelSha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawModel)));
        var frozen = JsonSerializer.Deserialize<Frozen>(rawModel)
            ?? throw new InvalidDataException("Modelo V9 congelado inválido.");
        if (frozen.Version != "V9_SYNTHETIC_FROZEN_JOINT_FS_FROM_SEED42_V1"
            || frozen.SourceSeed != 42UL
            || frozen.Cells is not { Length: 81 }
            || frozen.Parameters is null || frozen.Parameters.Count < 8
            || (seed == 42UL && !string.Equals(frozen.SourceInputFingerprint, fingerprint,
                StringComparison.OrdinalIgnoreCase))
            || (seed != 42UL && string.Equals(frozen.SourceInputFingerprint, fingerprint,
                StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Fonte, modelo ou 81 células V9 congeladas inconsistentes.");
        if (Math.Abs(frozen.Cells.Sum(x => x.M)-1m) > .0000001m
            || Math.Abs(frozen.Cells.Sum(x => x.U)-1m) > .0000001m)
            throw new InvalidDataException("M/U congelado não normalizado.");
        var observationRows = ReadObservations(Encoding.UTF8.GetString(observationBytes));
        if (observationRows.Length <= 30000)
            throw new InvalidDataException("Corpus exige observações de 30.000 bases.");
        var expectedPeopleSha = outputs.EnumerateArray()
            .Single(x => x.GetProperty("path").GetString() == "pessoas_verdade.csv")
            .GetProperty("sha256").GetString()!;
        var peopleBytes = await File.ReadAllBytesAsync(
            Path.Combine(corpusRoot, "pessoas_verdade.csv"), ct);
        var peopleSha = Convert.ToHexString(SHA256.HashData(peopleBytes));
        if (!string.Equals(expectedPeopleSha, peopleSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("SHA-256 da massa-base diverge do manifesto.");
        var basePeople = ReadBasePeople(Encoding.UTF8.GetString(peopleBytes));
        if (basePeople.Length != 30000)
            throw new InvalidDataException("Verificação da massa-base sintética exige exatamente 30.000 pessoas.");
        var basePartition = basePeople.ToDictionary(
            x => x.BasePersonId, x => x.Partition, StringComparer.Ordinal);
        if (observationRows.Any(x => !basePartition.TryGetValue(x.Base, out var partition)
                || partition != x.Partition))
            throw new InvalidDataException("Observação sintética sem pessoa-base ou com split divergente.");
        // Source is the *unique synthetic BASE PEOPLE population* in TRAIN,
        // not repeated candidate endpoints or noise-corrupted observations.
        // No TF U baseline or calibrated weight can be inferred from this audit.
        var tfBaseAudit = V9SyntheticBaseFrequencyAudit.Analyze(
            basePeople, string.Concat(fingerprint, ":", peopleSha));

        await using (var create = new SqlCommand(
            """
            CREATE TABLE #v9obs(
                obs_id INT NOT NULL PRIMARY KEY,
                part VARCHAR(12) COLLATE Latin1_General_100_BIN2 NOT NULL,
                gestor NVARCHAR(40) COLLATE Latin1_General_100_BIN2 NOT NULL,
                cpf VARCHAR(16) NULL,
                nome NVARCHAR(250) COLLATE Latin1_General_100_BIN2 NULL,
                nome_p3 NVARCHAR(3) COLLATE Latin1_General_100_BIN2 NULL,
                mae NVARCHAR(250) COLLATE Latin1_General_100_BIN2 NULL,
                nome_fon NVARCHAR(150) COLLATE Latin1_General_100_BIN2 NULL,
                mae_fon NVARCHAR(150) COLLATE Latin1_General_100_BIN2 NULL,
                nascimento DATE NULL
            );
            """, connection))
            await create.ExecuteNonQueryAsync(ct);

        var table = new DataTable();
        foreach (var (name, kind) in new (string, Type)[] {
            ("obs_id", typeof(int)), ("part", typeof(string)), ("gestor", typeof(string)),
            ("cpf", typeof(string)), ("nome", typeof(string)), ("nome_p3", typeof(string)),
            ("mae", typeof(string)),
            ("nome_fon", typeof(string)), ("mae_fon", typeof(string)),
            ("nascimento", typeof(DateTime)) })
            table.Columns.Add(name, kind);
        foreach (var o in observationRows)
            table.Rows.Add(o.Id, o.Partition, o.Gestor, (object?)o.Cpf ?? DBNull.Value,
                (object?)o.NameFull ?? DBNull.Value,
                o.NameFull is { Length: >= 3 } ? o.NameFull[..3] : DBNull.Value,
                (object?)o.MotherFull ?? DBNull.Value,
                (object?)o.NamePhonetic ?? DBNull.Value,
                (object?)o.MotherPhonetic ?? DBNull.Value,
                o.Birth is { } birth ? birth.ToDateTime(TimeOnly.MinValue) : DBNull.Value);
        using (var bulk = new SqlBulkCopy(connection) {
            DestinationTableName = "#v9obs", BatchSize = 15000,
            BulkCopyTimeout = 300 })
            await bulk.WriteToServerAsync(table, ct);
        table.Dispose();

        // The C1 five passes are the exact conjunctive equality predicates
        // supplied by CombinedIdentityCandidatePlanner (including D/M swap
        // and adjacent year). SQL executes ALL distinct candidates (no U proxy).
        // Same split / inter-Gestor / no CPF are explicit diagnostic frame.
        await using (var materialize = new SqlCommand(
            """
            CREATE INDEX IX_v9_name ON #v9obs(part,nome,mae,nascimento);
            CREATE INDEX IX_v9_namefon ON #v9obs(part,nome_fon,mae,nascimento);
            CREATE INDEX IX_v9_maefon ON #v9obs(part,nome,mae_fon,nascimento);

            -- Temporary supporting indexes for the pre-registered ablation JOINs;
            -- no index or SQL schema outside JornadaHarness is changed.
            CREATE INDEX IX_v9_person_birth ON #v9obs(part,nome,nascimento);
            CREATE INDEX IX_v9_mother_birth ON #v9obs(part,mae,nascimento);
            CREATE INDEX IX_v9_personfon_birth ON #v9obs(part,nome_fon,nascimento);
            CREATE INDEX IX_v9_motherfon_birth ON #v9obs(part,mae_fon,nascimento);
            CREATE INDEX IX_v9_prefix3_birth ON #v9obs(part,nome_p3,nascimento);

            SELECT DISTINCT q.a, q.b, q.part
            INTO #v9pairs
            FROM (
                SELECT a.obs_id a,b.obs_id b,a.part
                FROM #v9obs a JOIN #v9obs b
                    ON a.part=b.part AND a.nome=b.nome AND a.mae=b.mae
                    AND a.nascimento=b.nascimento AND a.obs_id<b.obs_id
                WHERE a.cpf IS NULL AND b.cpf IS NULL AND a.gestor<>b.gestor
                UNION ALL
                SELECT a.obs_id,b.obs_id,a.part
                FROM #v9obs a JOIN #v9obs b
                    ON a.part=b.part AND a.nome=b.nome AND a.mae=b.mae
                    AND YEAR(a.nascimento)=YEAR(b.nascimento)
                    AND DAY(a.nascimento)=MONTH(b.nascimento)
                    AND MONTH(a.nascimento)=DAY(b.nascimento)
                    AND a.obs_id<b.obs_id
                WHERE a.cpf IS NULL AND b.cpf IS NULL AND a.gestor<>b.gestor
                  AND DAY(a.nascimento)<=12 AND MONTH(a.nascimento)<=12
                  AND DAY(a.nascimento)<>MONTH(a.nascimento)
                UNION ALL
                SELECT a.obs_id,b.obs_id,a.part
                FROM #v9obs a JOIN #v9obs b
                    ON a.part=b.part AND a.nome=b.nome AND a.mae=b.mae
                    AND ABS(YEAR(a.nascimento)-YEAR(b.nascimento))=1
                    AND MONTH(a.nascimento)=MONTH(b.nascimento)
                    AND DAY(a.nascimento)=DAY(b.nascimento)
                    AND a.obs_id<b.obs_id
                WHERE a.cpf IS NULL AND b.cpf IS NULL AND a.gestor<>b.gestor
                UNION ALL
                SELECT a.obs_id,b.obs_id,a.part
                FROM #v9obs a JOIN #v9obs b
                    ON a.part=b.part AND a.nome_fon=b.nome_fon AND a.mae=b.mae
                    AND a.nascimento=b.nascimento AND a.obs_id<b.obs_id
                WHERE a.cpf IS NULL AND b.cpf IS NULL AND a.gestor<>b.gestor
                UNION ALL
                SELECT a.obs_id,b.obs_id,a.part
                FROM #v9obs a JOIN #v9obs b
                    ON a.part=b.part AND a.nome=b.nome AND a.mae_fon=b.mae_fon
                    AND a.nascimento=b.nascimento AND a.obs_id<b.obs_id
                WHERE a.cpf IS NULL AND b.cpf IS NULL AND a.gestor<>b.gestor
            ) q;
            CREATE UNIQUE CLUSTERED INDEX IX_v9_pair ON #v9pairs(a,b);
            """, connection) { CommandTimeout = 600 })
            await materialize.ExecuteNonQueryAsync(ct);

        // Capture the PHYSICAL C1 membership BEFORE adding ACTIVE rules.
        // Keep only independently known M ordinals in memory; no candidate
        // values or pair IDs are exported. No TOP or silent sampling.
        var c1TruePairs = new HashSet<(int A, int B)>();
        await using (var query = new SqlCommand(
            "SELECT a,b FROM #v9pairs ORDER BY a,b;", connection) { CommandTimeout = 600 })
        await using (var reader = await query.ExecuteReaderAsync(ct))
        {
            long count = 0;
            while (await reader.ReadAsync(ct))
            {
                if (++count > HardPairCap)
                    throw new InvalidOperationException("C1 físico excedeu cap de pares.");
                var a = reader.GetInt32(0);
                var b = reader.GetInt32(1);
                if (observationRows[a].Base == observationRows[b].Base
                    && !c1TruePairs.Add((a, b)))
                    throw new InvalidDataException("C1 físico contém M duplicado.");
            }
        }

        // Candidate C1 is additive to the published ACTIVE dynamic ruleset.
        // Record its exact passes to test AND/OR semantics on the SAME M frame,
        // not on the training sample used by BlockingRuleSetOptimizer.
        var activeRules = await UnionPublishedDynamicPassesAsync(
            connection, observationRows, ct);

        // Count truth pairs independently of SQL blocking, preserving FN of
        // missing candidates rather than silently conditioning recall on hits.
        var bySplit = new[] { "TRAIN", "VALIDATION", "TEST" }
            .ToDictionary(x => x, x => new Acc(x), StringComparer.Ordinal);
        // The denominator is independently labelled BEFORE reading #v9pairs.
        // Store no PII in the output, only ephemeral ordinals and observed
        // comparison-state flags for aggregate breakdowns.
        var eligibleTruthPairs = new List<V9BlockingTruthLossAudit.Pair>();
        var frame = observationRows.Where(x => x.Cpf is null && x.Birth.HasValue)
            .GroupBy(x => (x.Partition, x.Base));
        foreach (var group in frame)
        {
            var rows = group.ToArray();
            for (var a = 0; a < rows.Length; a++)
                for (var b = a+1; b < rows.Length; b++)
                {
                    var left = rows[a];
                    var right = rows[b];
                    if (left.Gestor == right.Gestor)
                        continue;
                    bySplit[group.Key.Partition].Eligible++;
                    eligibleTruthPairs.Add(new V9BlockingTruthLossAudit.Pair(
                        Math.Min(left.Id, right.Id),
                        Math.Max(left.Id, right.Id),
                        group.Key.Partition,
                        left.NameFull is null || right.NameFull is null,
                        left.NameFull is not null && right.NameFull is not null
                            && left.NameFull == right.NameFull,
                        left.MotherFull is null || right.MotherFull is null,
                        left.MotherFull is not null && right.MotherFull is not null
                            && left.MotherFull == right.MotherFull,
                        left.Birth == right.Birth));
                }
        }
        var recoveredTruePairs = new HashSet<(int A, int B)>();
        var fanout = new V9SqlCandidateFanoutAudit.Collector(
            observationRows.Select(x => new V9SqlCandidateFanoutAudit.Observation(
                x.Id, x.Partition, x.Gestor, x.Base,
                x.Cpf is null && x.Birth.HasValue)));
        // M/U support comes from the exact same SQL candidate frame as the
        // scorer, TRAIN only, not from the previously sampled proxy by initial.
        var trainSqlPairs = new List<V9SqlConditionedNominalSupportAudit.Candidate>();
        var trainValidationSqlBaseline = new List<V9SqlTrainValidationUnionSearch.Pair>();
        var trainSqlBirthPairs = new List<V9SqlConditionedBirthSupportAudit.Candidate>();
        long allCandidate = 0;
        await using (var command = new SqlCommand(
            "SELECT a,b,part FROM #v9pairs ORDER BY a,b;", connection) {
                CommandTimeout = 600 })
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                if (++allCandidate > HardPairCap)
                    throw new InvalidOperationException(
                        "Censo SQL completo excedeu o limite de 1 milhão; abortar, nunca amostrar U silenciosamente.");
                var a = observationRows[reader.GetInt32(0)];
                var b = observationRows[reader.GetInt32(1)];
                var partition = reader.GetString(2);
                if (partition != a.Partition || partition != b.Partition
                    || a.Gestor == b.Gestor || a.Cpf is not null || b.Cpf is not null)
                    throw new InvalidDataException("SQL gerou par fora do frame elegível.");
                var split = bySplit[partition];
                // The SQL physical cursor is the single source of candidate
                // edges; include zero-hit eligible observations in denominator.
                fanout.AddCandidate(a.Id, b.Id, partition);
                if (partition == "TRAIN")
                {
                    trainSqlPairs.Add(new V9SqlConditionedNominalSupportAudit.Candidate(
                        a.Id, b.Id, partition, a.Base, b.Base,
                        a.Name, b.Name, a.Mother, b.Mother));
                    // All SQL eligible endpoints in this diagnostic have a
                    // date. Unknown birth dates remain out of this *specific*
                    // frame, never a general contract requirement.
                    if (a.Birth is not { } birthA || b.Birth is not { } birthB)
                        throw new InvalidDataException(
                            "Nascimento ausente em candidato TRAIN do frame SQL restrito.");
                    trainSqlBirthPairs.Add(new V9SqlConditionedBirthSupportAudit.Candidate(
                        a.Id, b.Id, partition, a.Base, b.Base, birthA, birthB));
                }
                var same = a.Base == b.Base;
                if (partition is "TRAIN" or "VALIDATION")
                    trainValidationSqlBaseline.Add(new V9SqlTrainValidationUnionSearch.Pair(
                        a.Id, b.Id, partition, same));
                if (same)
                {
                    split.TrueCandidate++;
                    if (!recoveredTruePairs.Add((a.Id, b.Id)))
                        throw new InvalidDataException("Censo SQL retornou M duplicado.");
                }
                else split.FalseCandidate++;
                var score = FellegiSunterScoring.CalculateCompositeNominalV9PreviewWithBreakdown(
                    frozen.Parameters, a.Name, b.Name, a.Mother, b.Mother,
                    frozen.Cells, CompositeNominalComparisonV9.ContractVersion,
                    leftBirthDate: a.Birth, rightBirthDate: b.Birth);
                if (score.Contributions.Count(x => x.Evidence == "NOMINAL_CONJUNTO_V9") != 1)
                    throw new InvalidOperationException("V9 FS deveria ter uma única evidência nominal.");
                if (score.Score.LogOdds > frozen.ThresholdLogOdds)
                {
                    if (same) split.Tp++;
                    else split.Fp++;
                }
            }
        }
        var fanoutReport = fanout.Build();
        foreach (var endpointSplit in fanoutReport.Splits)
        {
            var pairSplit = bySplit[endpointSplit.Partition];
            if (endpointSplit.CandidateTruePairs != pairSplit.TrueCandidate
                || endpointSplit.CandidateFalsePairs != pairSplit.FalseCandidate)
                throw new InvalidDataException(
                    "Fan-out de pontas diverge dos pares SQL físicos.");
        }
        var trainNominalSupport = V9SqlConditionedNominalSupportAudit.Analyze(trainSqlPairs);
        var trainBirthSupport = V9SqlConditionedBirthSupportAudit.Analyze(trainSqlBirthPairs);
        if (trainBirthSupport.SqlCandidateM != trainNominalSupport.SqlCandidateM
            || trainBirthSupport.SqlCandidateU != trainNominalSupport.SqlCandidateU
            || trainBirthSupport.SqlCandidatePairs != trainNominalSupport.SqlCandidatePairs)
            throw new InvalidDataException("Nascimento e nome não conciliam no mesmo SQL TRAIN.");
        var trainMuIdentifiability = V9SqlConditionalMuIdentifiabilityAudit.Analyze(
            trainNominalSupport, trainBirthSupport, tfBaseAudit);
        var trainClusterConcentration = V9SqlBaseClusterConcentrationAudit.Analyze(
            trainSqlPairs, trainSqlBirthPairs, trainNominalSupport,
            trainBirthSupport, tfBaseAudit.TrainingBasePeople);
        if (trainClusterConcentration.SqlCandidateM != trainMuIdentifiability.SqlCandidateM
            || trainClusterConcentration.SqlCandidateU != trainMuIdentifiability.SqlCandidateU)
            throw new InvalidDataException("Concentração por base diverge do suporte SQL TRAIN.");
        if (trainNominalSupport.SqlCandidateM != bySplit["TRAIN"].TrueCandidate
            || trainNominalSupport.SqlCandidateU != bySplit["TRAIN"].FalseCandidate)
            throw new InvalidDataException("Suporte m/u nominal TRAIN não concilia com SQL físico.");
        var lossAudit = V9BlockingTruthLossAudit.Analyze(
            eligibleTruthPairs, recoveredTruePairs);
        var parityProjectionCache = new Dictionary<int, IReadOnlySet<BlockingProjectionKey>>();
        IReadOnlySet<BlockingProjectionKey> KeysFor(int id)
        {
            if (!parityProjectionCache.TryGetValue(id, out var keys))
            {
                var row = observationRows[id];
                keys = BlockingProjectionKeyProjector.Project(
                    row.Name, row.Mother, row.Birth).ToHashSet();
                parityProjectionCache.Add(id, keys);
            }
            return keys;
        }
        var frameParity = V9SqlBlockingFrameParityAudit.Analyze(
            eligibleTruthPairs.Select(x => new V9SqlBlockingFrameParityAudit.Pair(
                x.LowerObservationId, x.HigherObservationId, x.Partition,
                KeysFor(x.LowerObservationId), KeysFor(x.HigherObservationId))).ToArray(),
            activeRules.Passes.Select(x =>
                new V9SqlBlockingFrameParityAudit.Pass(x.PassId, x.Fields)).ToArray(),
            c1TruePairs, recoveredTruePairs);
        var metrics = bySplit.Values.Select(x => x.Build()).ToArray();
        foreach (var split in metrics)
        {
            var corresponding = lossAudit.Partitions.Single(x => x.Partition == split.Partition);
            if (corresponding.EligibleTruePairs != split.EligibleTrueInterGestorNoCpfPairs
                || corresponding.RetrievedTruePairs != split.SqlCandidateTruePairs
                || corresponding.LostTruePairs != split.TruePairsLostBySqlBlocking)
                throw new InvalidDataException("Decomposição M diverge do censo SQL físico.");
        }
        // Report incremental candidate coverage and accepted-link risk for
        // 6 PRE-REGISTERED SQL alternatives. Do not add to the baseline:
        // the frozen proxy-calibrated FS model has NOT been refit on SQL.
        var unionSearch = await MeasurePreRegisteredPassesAsync(
            connection, observationRows, frozen,
            trainValidationSqlBaseline,
            bySplit["TRAIN"].Eligible, bySplit["VALIDATION"].Eligible, ct);
        var passSensitivity = unionSearch.Sensitivity;
        // Separate five NEW birth-year tolerance pass families. Diagnostic
        // TRAIN/VALIDATION only; TEST and current ruleset never changed.
        var birthYearExploration = await ExploreBirthYearPassesAsync(
            connection, observationRows, trainValidationSqlBaseline,
            bySplit["TRAIN"].Eligible, bySplit["VALIDATION"].Eligible, ct);
        var elevenResidual = V9SqlElevenPassResidualAudit.Analyze(
            observationRows.Where(x=>x.Partition is "TRAIN" or "VALIDATION")
                .Select(x=>new V9SqlBirthYearPassFamilyAudit.Observation(
                    x.Id,x.Partition,x.Gestor,x.Cpf is null && x.Birth.HasValue))
                .ToArray(),
            trainValidationSqlBaseline, unionSearch.Extensions,
            birthYearExploration.Extensions, eligibleTruthPairs,
            unionSearch.Search, birthYearExploration.Report, HardPairCap,
            observationRows.Where(x=>x.Partition is "TRAIN" or "VALIDATION")
                .ToDictionary(x=>x.Id,x=>x.Base),seed,fingerprint,
            observationRows.Where(x=>x.Partition is "TRAIN" or "VALIDATION"
                && x.Cpf is null && x.Birth.HasValue)
                .Select(x=>new V9SqlTwoSignalResidualAudit.Signal(
                    x.Id,x.Partition,x.NamePhonetic,x.MotherPhonetic,x.Birth!.Value.Year))
                .ToArray());
        // New experimental indexed prefix3 + exact birth family, TRAIN/VALIDATION only.
        var prefix3Birth = await V9SqlNamePrefix3BirthProbe.RunAsync(
            connection,
            observationRows.Select(x=>new V9SqlNamePrefix3BirthProbe.Observation(
                x.Id,x.Base,x.Partition,x.Gestor,
                x.NameFull is { Length: >= 3 } ? x.NameFull[..3] : null,
                x.Birth,x.Cpf)).ToArray(),
            trainValidationSqlBaseline,unionSearch.Extensions,
            birthYearExploration.Extensions,eligibleTruthPairs,elevenResidual,
            HardPairCap,ct);
        // Real SQL candidates: mother's prefix3 + DOB; person+mother prefix3.
        // Previous one-pass prefix3 table remains available on this connection.
        var furtherPrefixPasses=await V9SqlTwoPrefix3Probe.RunAsync(
            connection,observationRows.Select(x=>new V9SqlTwoPrefix3Probe.Observation(
                x.Id,x.Base,x.Partition,x.Gestor,
                x.NameFull is { Length: >= 3 } ? x.NameFull[..3] : null,
                x.MotherFull is { Length: >= 3 } ? x.MotherFull[..3] : null,
                x.Birth,x.Cpf,x.NameFull,x.MotherFull,
                x.NamePhonetic,x.MotherPhonetic)).ToArray(),
            trainValidationSqlBaseline,unionSearch.Extensions,
            birthYearExploration.Extensions,eligibleTruthPairs,
            prefix3Birth,HardPairCap,ct);
        if (metrics.Any(x => x.EligibleTrueInterGestorNoCpfPairs <= 0)
            || metrics.Any(x => x.SqlCandidateTruePairs > x.EligibleTrueInterGestorNoCpfPairs))
            throw new InvalidOperationException("Gabarito ou recuperação SQL M incompatíveis.");
        return new Result(
            Version, Scope, BlockingPolicy, BlockingProjectionKeyProjector.MethodVersion,
            seed, 30000, observationRows.Length, fingerprint, observationSha, modelSha,
            frozen.SourceSeed, frozen.SourceInputFingerprint, frozen.ThresholdLogOdds,
            allCandidate, HardPairCap, metrics,
            metrics.All(x => x.SqlCandidateFalsePairs == 0)
                ? "FULL_SQL_UNION_NO_FALSE_CANDIDATE_EXPOSURE_UNESTIMABLE_FALSE_LINK_RISK"
                : "MEASURED_SQL_ACTIVE_RULESET_PLUS_C1_LINK_ERRORS_NO_PERFORMANCE_GATE",
            "NOT_OPERATIONAL_ALIAS_HISTORY_OR_CALIBRATED_TF")
        {
            ActiveRulesetFingerprint = activeRules.Fingerprint,
            ActiveRulesetPassCount = activeRules.PassCount,
            ActiveRulesetNewCandidatePairs = activeRules.NewPairs,
            TrainBaseFrequencyAudit = tfBaseAudit,
            TruePairLossAudit = lossAudit,
            ActiveSameFrameParityAudit = frameParity,
            TrainSqlNominalSupportAudit = trainNominalSupport,
            CandidateFanoutAudit = fanoutReport,
            TrainSqlBirthSupportAudit = trainBirthSupport,
            TrainSqlMuIdentifiabilityAudit = trainMuIdentifiability,
            TrainSqlBaseClusterConcentrationAudit = trainClusterConcentration,
            BlockingAblationContract = BlockingAblationVersion,
            PreRegisteredPassSensitivity = passSensitivity,
            TrainValidationUnionSearch = unionSearch.Search,
            TrainValidationUnionFanout = unionSearch.Fanout,
            BirthYearPassFamilyTrainValidation = birthYearExploration.Report,
            ElevenPassResidualTrainValidation = elevenResidual,
            Prefix3ExactBirthTrainValidation = prefix3Birth,
            OtherPrefix3PairsTrainValidation = furtherPrefixPasses
        };
    }


    private sealed class AblationAcc(string pass, string split)
    {
        public long M, U, Tp, Fp;
        public BlockingPassSensitivity Report()
        {
            if (Tp > M || Fp > U)
                throw new InvalidDataException("Ablation SQL: aceitos excedem recuperados.");
            return new BlockingPassSensitivity(pass, split, M, U, Tp, Fp,
                M == 0 ? null : (decimal)U / M,
                Tp + Fp == 0 ? null : (decimal)Fp / (Tp + Fp));
        }
    }

    private static async Task<(BlockingPassSensitivity[] Sensitivity,
            V9SqlTrainValidationUnionSearch.Report Search,
            V9SqlTrainValidationUnionFanoutAudit.Report Fanout,
            IReadOnlyList<V9SqlTrainValidationUnionSearch.Extension> Extensions)>
        MeasurePreRegisteredPassesAsync(
            SqlConnection connection, Observation[] observations, Frozen frozen,
            IReadOnlyList<V9SqlTrainValidationUnionSearch.Pair> baseline,
            long trainEligibleM, long validationEligibleM,
            CancellationToken ct)
    {
        // Separate, immutable exploratory alternatives. EACH pass is compared
        // to the SAME #v9pairs baseline; their totals cannot be summed due to
        // overlaps, nor used to retune the threshold on TEST. Two independent
        // blocking attributes are required in every pass.
        (string Name, string Condition)[] proposed =
        [
            ("PERSON_EXACT_PLUS_BIRTH_EXACT",
                "a.nome=b.nome AND a.nascimento=b.nascimento"),
            ("MOTHER_EXACT_PLUS_BIRTH_EXACT",
                "a.mae=b.mae AND a.nascimento=b.nascimento"),
            ("PERSON_EXACT_PLUS_MOTHER_EXACT_ANY_BIRTH",
                "a.nome=b.nome AND a.mae=b.mae"),
            ("PERSON_PHONETIC_PLUS_BIRTH_EXACT",
                "a.nome_fon=b.nome_fon AND a.nascimento=b.nascimento"),
            ("MOTHER_PHONETIC_PLUS_BIRTH_EXACT",
                "a.mae_fon=b.mae_fon AND a.nascimento=b.nascimento"),
            ("PERSON_AND_MOTHER_PHONETIC_PLUS_BIRTH_EXACT",
                "a.nome_fon=b.nome_fon AND a.mae_fon=b.mae_fon AND a.nascimento=b.nascimento")
        ];
        await using (var create = new SqlCommand(
            """
            CREATE TABLE #v9opt(
                a INT NOT NULL, b INT NOT NULL,
                part VARCHAR(12) COLLATE Latin1_General_100_BIN2 NOT NULL,
                PRIMARY KEY CLUSTERED(a,b)
            );
            """,connection) { CommandTimeout = 90 })
            await create.ExecuteNonQueryAsync(ct);
        var results = new List<BlockingPassSensitivity>(proposed.Length * 3);
        var extensions = new List<V9SqlTrainValidationUnionSearch.Extension>(proposed.Length);
        foreach (var pass in proposed)
        {
            var addedTrainingAndValidation =
                new List<V9SqlTrainValidationUnionSearch.Pair>();
            // SQL clauses are compile-time constants, never user input.
            // Restriction to observed birth prevents a different eligible
            // denominator from being mistaken for an apparent recall gain.
            await using (var insert = new SqlCommand($"""
                INSERT INTO #v9opt(a,b,part)
                SELECT DISTINCT a.obs_id,b.obs_id,a.part
                FROM #v9obs a JOIN #v9obs b
                  ON a.part=b.part AND a.obs_id<b.obs_id
                 AND {pass.Condition}
                WHERE a.cpf IS NULL AND b.cpf IS NULL
                  AND a.gestor<>b.gestor
                  AND a.nascimento IS NOT NULL AND b.nascimento IS NOT NULL
                  AND NOT EXISTS (
                    SELECT 1 FROM #v9pairs current_pairs
                    WHERE current_pairs.a=a.obs_id AND current_pairs.b=b.obs_id);
                """,connection) { CommandTimeout = 600 })
                await insert.ExecuteNonQueryAsync(ct);
            long sqlCount;
            await using (var count = new SqlCommand(
                "SELECT COUNT_BIG(*) FROM #v9opt;",connection) { CommandTimeout = 90 })
                sqlCount = (long)(await count.ExecuteScalarAsync(ct) ?? 0L);
            if (sqlCount > HardPairCap)
                throw new InvalidOperationException(
                    "Ablation SQL excedeu limite de candidatos; não amostrar, truncar ou ativar passe.");
            var byPartition = new[] { "TRAIN", "VALIDATION", "TEST" }
                .ToDictionary(x => x, x => new AblationAcc(pass.Name, x),
                    StringComparer.Ordinal);
            long visited = 0;
            await using (var command = new SqlCommand(
                "SELECT a,b,part FROM #v9opt ORDER BY a,b;",connection) {
                CommandTimeout = 600 })
            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    if (++visited > HardPairCap)
                        throw new InvalidOperationException(
                            "Ablation SQL excedeu limite de leitura.");
                    var a = observations[reader.GetInt32(0)];
                    var b = observations[reader.GetInt32(1)];
                    var part = reader.GetString(2);
                    if (a.Partition != part || b.Partition != part
                        || a.Gestor == b.Gestor || a.Cpf is not null || b.Cpf is not null
                        || !a.Birth.HasValue || !b.Birth.HasValue)
                        throw new InvalidDataException("Ablation SQL fora do frame.");
                    var acc = byPartition[part];
                    var match = a.Base == b.Base;
                    if (match) acc.M++; else acc.U++;
                    // Collect only TRAIN/VALIDATION for an actual union search.
                    // TEST ablation remains an independently reported sensitivity;
                    // it never participates in pass ranking or policy selection.
                    if (part is "TRAIN" or "VALIDATION")
                        addedTrainingAndValidation.Add(
                            new V9SqlTrainValidationUnionSearch.Pair(
                                a.Id, b.Id, part, match));
                    var score = FellegiSunterScoring.CalculateCompositeNominalV9PreviewWithBreakdown(
                        frozen.Parameters, a.Name,b.Name,a.Mother,b.Mother,
                        frozen.Cells, CompositeNominalComparisonV9.ContractVersion,
                        leftBirthDate:a.Birth,rightBirthDate:b.Birth);
                    if (score.Score.LogOdds > frozen.ThresholdLogOdds)
                    {
                        if (match) acc.Tp++; else acc.Fp++;
                    }
                }
            }
            if (visited != sqlCount)
                throw new InvalidDataException("Ablation SQL perdeu candidatos.");
            results.AddRange(byPartition.Values.Select(x=>x.Report()));
            extensions.Add(new V9SqlTrainValidationUnionSearch.Extension(
                pass.Name, addedTrainingAndValidation));
            await using var clear = new SqlCommand("TRUNCATE TABLE #v9opt;", connection);
            await clear.ExecuteNonQueryAsync(ct);
        }
        if (results.Count != 18 ||
            results.Any(x => x.ExtraTruePairs < 0 || x.ExtraFalsePairs < 0
                || x.ExtraAcceptedTruePairs > x.ExtraTruePairs
                || x.ExtraAcceptedWrongPairs > x.ExtraFalsePairs))
            throw new InvalidDataException("Ablation SQL sem suporte íntegro por split.");
        var search = V9SqlTrainValidationUnionSearch.Analyze(
            baseline, extensions, trainEligibleM, validationEligibleM, HardPairCap);
        foreach (var ablation in results.Where(x => x.Partition is "TRAIN" or "VALIDATION"))
        {
            var extension = extensions.Single(x => x.PassId == ablation.PassId);
            var subset = extension.ExtraPairs.Where(x => x.Partition == ablation.Partition).ToArray();
            if (subset.LongCount(x => x.IsReferenceMatch) != ablation.ExtraTruePairs
                || subset.LongCount(x => !x.IsReferenceMatch) != ablation.ExtraFalsePairs)
                throw new InvalidDataException(
                    "Busca de uniões de blocking diverge da ablação SQL individual.");
        }
        var fanout = V9SqlTrainValidationUnionFanoutAudit.Analyze(
            observations.Where(x => x.Partition is "TRAIN" or "VALIDATION")
                .Select(x => new V9SqlTrainValidationUnionFanoutAudit.Observation(
                    x.Id, x.Partition, x.Gestor, x.Cpf is null && x.Birth.HasValue))
                .ToArray(),
            baseline, extensions, search, HardPairCap);
        return (results.ToArray(), search, fanout,
            Array.AsReadOnly(extensions.ToArray()));
    }


    private static async Task<(V9SqlBirthYearPassFamilyAudit.Report Report,
            IReadOnlyList<V9SqlBirthYearPassFamilyAudit.Pass> Extensions)>
        ExploreBirthYearPassesAsync(
            SqlConnection connection, Observation[] observations,
            IReadOnlyList<V9SqlTrainValidationUnionSearch.Pair> baseline,
            long trainEligibleM, long validationEligibleM,
            CancellationToken ct)
    {
        // Every join is an independent, frozen, two-signal predicate.
        // YEAR relaxation tolerates day/month edits without treating
        // missing mother or a missing date as a neutral agreement.
        // Five probes use the same physical SQL baseline frame. TEST is
        // excluded in SQL, NOT merely filtered at report/ranking time.
        (string Name, string SqlPredicate)[] proposed =
        [
            ("PERSON_EXACT_PLUS_BIRTH_YEAR",
                "a.nome=b.nome AND YEAR(a.nascimento)=YEAR(b.nascimento)"),
            ("PERSON_PHONETIC_PLUS_BIRTH_YEAR",
                "a.nome_fon=b.nome_fon AND YEAR(a.nascimento)=YEAR(b.nascimento)"),
            ("MOTHER_EXACT_PLUS_BIRTH_YEAR",
                "a.mae=b.mae AND YEAR(a.nascimento)=YEAR(b.nascimento)"),
            ("MOTHER_PHONETIC_PLUS_BIRTH_YEAR",
                "a.mae_fon=b.mae_fon AND YEAR(a.nascimento)=YEAR(b.nascimento)"),
            ("PERSON_AND_MOTHER_PHONETIC_ANY_BIRTH",
                "a.nome_fon=b.nome_fon AND a.mae_fon=b.mae_fon")
        ];
        await using (var create = new SqlCommand(
            """
            CREATE TABLE #v9year(
                a INT NOT NULL, b INT NOT NULL,
                part VARCHAR(12) COLLATE Latin1_General_100_BIN2 NOT NULL,
                PRIMARY KEY CLUSTERED(a,b)
            );
            """, connection) { CommandTimeout = 90 })
            await create.ExecuteNonQueryAsync(ct);

        var extensions = new List<V9SqlBirthYearPassFamilyAudit.Pass>(proposed.Length);
        foreach (var pass in proposed)
        {
            // Frozen SQL predicates, never user-supplied. The existing
            // IX_v9_person_birth, IX_v9_personfon_birth and
            // IX_v9_mother_birth indexes support equality anchors.
            await using (var insert = new SqlCommand($"""
                INSERT INTO #v9year(a,b,part)
                SELECT DISTINCT a.obs_id,b.obs_id,a.part
                FROM #v9obs a JOIN #v9obs b
                  ON a.part=b.part AND a.obs_id<b.obs_id
                 AND {pass.SqlPredicate}
                WHERE a.part IN ('TRAIN','VALIDATION')
                  AND a.cpf IS NULL AND b.cpf IS NULL
                  AND a.gestor<>b.gestor
                  AND a.nascimento IS NOT NULL AND b.nascimento IS NOT NULL
                  AND NOT EXISTS (
                    SELECT 1 FROM #v9pairs current_pairs
                    WHERE current_pairs.a=a.obs_id
                      AND current_pairs.b=b.obs_id);
                """, connection) { CommandTimeout = 600 })
                await insert.ExecuteNonQueryAsync(ct);
            long sqlCount;
            await using (var count = new SqlCommand(
                "SELECT COUNT_BIG(*) FROM #v9year;", connection) { CommandTimeout = 90 })
                sqlCount = (long)(await count.ExecuteScalarAsync(ct) ?? -1L);
            if (sqlCount < 0 || sqlCount > HardPairCap)
                throw new InvalidOperationException(
                    "Nova família SQL excedeu cap; não amostrar, truncar ou ativar.");
            var pairSet = new List<V9SqlBirthYearPassFamilyAudit.Pair>();
            await using (var cmd = new SqlCommand(
                "SELECT a,b,part FROM #v9year ORDER BY a,b;", connection)
                { CommandTimeout = 600 })
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    if (pairSet.Count >= HardPairCap)
                        throw new InvalidOperationException(
                            "Nova família SQL excedeu limite de leitura.");
                    var a = observations[reader.GetInt32(0)];
                    var b = observations[reader.GetInt32(1)];
                    var part = reader.GetString(2);
                    if (part is not ("TRAIN" or "VALIDATION")
                        || a.Partition != part || b.Partition != part
                        || a.Gestor == b.Gestor || a.Cpf is not null || b.Cpf is not null
                        || !a.Birth.HasValue || !b.Birth.HasValue)
                        throw new InvalidDataException(
                            "Passe relaxado gerou par fora do frame SQL TRAIN/VALIDATION.");
                    pairSet.Add(new V9SqlBirthYearPassFamilyAudit.Pair(
                        a.Id,b.Id,part,a.Base==b.Base));
                }
            }
            if (pairSet.Count != sqlCount)
                throw new InvalidDataException("Nova família SQL perdeu candidatos.");
            extensions.Add(new V9SqlBirthYearPassFamilyAudit.Pass(
                pass.Name, pairSet));
            await using var clear = new SqlCommand(
                "TRUNCATE TABLE #v9year;", connection);
            await clear.ExecuteNonQueryAsync(ct);
        }

        var report = V9SqlBirthYearPassFamilyAudit.Analyze(
            observations.Where(x => x.Partition is "TRAIN" or "VALIDATION")
                .Select(x => new V9SqlBirthYearPassFamilyAudit.Observation(
                    x.Id,x.Partition,x.Gestor,x.Cpf is null && x.Birth.HasValue))
                .ToArray(),
            baseline.Select(x => new V9SqlBirthYearPassFamilyAudit.Pair(
                x.A,x.B,x.Partition,x.IsReferenceMatch)).ToArray(),
            extensions,trainEligibleM,validationEligibleM,HardPairCap);
        // One new pass's SQL count must equal its own aggregate incremental
        // M+U across TRAIN/VALIDATION. Prevent silent candidate filtering.
        foreach (var extension in extensions)
        {
            var actual = report.Splits
                .Where(x => x.PassId == extension.PassId)
                .Sum(x => x.ExtraM + x.ExtraU);
            if (actual != extension.ExtraPairs.Count)
                throw new InvalidDataException(
                    "Nova família SQL não conciliou a leitura completa de pares.");
        }
        return (report, Array.AsReadOnly(extensions.ToArray()));
    }

    private sealed record ActivePass(string PassId, string[] Fields);

    private static async Task<(string Fingerprint, int PassCount, long NewPairs, ActivePass[] Passes)>
        UnionPublishedDynamicPassesAsync(
            SqlConnection connection, IReadOnlyList<Observation> rows,
            CancellationToken ct)
    {
        // FAIL CLOSED: the actively published pass definition, not a guessed
        // set of fields from source code, is the source of this SQL census.
        Guid rulesetId;
        string fingerprint;
        await using (var query = new SqlCommand(
            """
            SELECT r.ruleset_id,r.fingerprint_sha256
            FROM identidade.linkage_ruleset r
            JOIN identidade.modelo_linkage m ON m.modelo_id=r.modelo_id
            WHERE m.status=N'ATIVO';
            """, connection) { CommandTimeout = 90 })
        await using (var read = await query.ExecuteReaderAsync(ct))
        {
            if (!await read.ReadAsync(ct))
                throw new InvalidOperationException("Não existe ruleset ATIVO no SQL de ensaio.");
            rulesetId = read.GetGuid(0);
            fingerprint = read.GetString(1);
            if (await read.ReadAsync(ct))
                throw new InvalidOperationException(
                    "Há múltiplos rulesets ativos; censo SQL precisa de um único contrato.");
        }
        var fields = new SortedDictionary<int, (string Id, List<string> Fields)>();
        await using (var query = new SqlCommand(
            """
            SELECT p.passe_ordem,p.passe_id,c.atributo
            FROM identidade.linkage_ruleset_passe p
            JOIN identidade.linkage_ruleset_passe_campo c
              ON c.ruleset_id=p.ruleset_id AND c.passe_ordem=p.passe_ordem
            WHERE p.ruleset_id=@id
            ORDER BY p.passe_ordem,c.campo_ordem;
            """, connection) { CommandTimeout = 90 })
        {
            query.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = rulesetId;
            await using var reader = await query.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var order = reader.GetInt32(0);
                var id = reader.GetString(1);
                if (!fields.TryGetValue(order, out var current))
                {
                    current = (id, new List<string>());
                    fields.Add(order, current);
                }
                else if (id != current.Id)
                    throw new InvalidDataException("Passe ativo com ID inconsistente.");
                current.Fields.Add(reader.GetString(2));
            }
        }
        var passes = fields.Values.Select(x => new ActivePass(x.Id,
            x.Fields.Distinct(StringComparer.Ordinal).ToArray())).ToArray();
        if (passes.Length == 0 || passes.Any(x => x.Fields.Length == 0))
            throw new InvalidOperationException("Ruleset ativo não possui passes completos.");
        var keyTable = new DataTable();
        keyTable.Columns.Add("obs_id", typeof(int));
        keyTable.Columns.Add("feature", typeof(string));
        keyTable.Columns.Add("value", typeof(string));
        var availableFeatures = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        foreach (var key in BlockingProjectionKeyProjector.Project(row.Name,row.Mother,row.Birth))
        {
            keyTable.Rows.Add(row.Id,key.Feature,key.Value);
            availableFeatures.Add(key.Feature);
        }
        await using (var query = new SqlCommand(
            """
            CREATE TABLE #v9keys(
                obs_id INT NOT NULL,
                feature VARCHAR(90) COLLATE Latin1_General_100_BIN2 NOT NULL,
                value NVARCHAR(450) COLLATE Latin1_General_100_BIN2 NOT NULL
            );
            """, connection))
            await query.ExecuteNonQueryAsync(ct);
        using (var bulk = new SqlBulkCopy(connection) {
            DestinationTableName = "#v9keys", BatchSize = 15000, BulkCopyTimeout = 300 })
            await bulk.WriteToServerAsync(keyTable, ct);
        keyTable.Dispose();
        await using (var query = new SqlCommand(
            "CREATE INDEX IX_v9keys_value ON #v9keys(feature,value,obs_id);",
            connection) { CommandTimeout = 240 })
            await query.ExecuteNonQueryAsync(ct);

        long before;
        await using (var q = new SqlCommand("SELECT COUNT_BIG(*) FROM #v9pairs;",connection))
            before = (long)(await q.ExecuteScalarAsync(ct) ?? 0L);

        foreach (var pass in passes)
        {
            // A feature absent in the entire synthetic source frame cannot
            // recover any pair; this mirrors the Runner's incomplete clause.
            if (pass.Fields.Any(x => !availableFeatures.Contains(x)))
                continue;
            // Anchor the SQL index probe with the most selective identity
            // feature; all other fields remain mandatory AND EXISTS clauses.
            int Priority(string feature) =>
                feature is BlockingFeatureNames.FullName or BlockingFeatureNames.MotherFullName
                    ? 0 : feature is BlockingFeatureNames.FullNamePhoneticPtBr
                    or BlockingFeatureNames.MotherFullNamePhoneticPtBr ? 1 :
                    feature is BlockingFeatureNames.LastName or BlockingFeatureNames.MotherLastName
                    ? 2 : feature is BlockingFeatureNames.FirstName or BlockingFeatureNames.MotherFirstName
                    ? 3 : 4;
            var ordered = pass.Fields.OrderBy(Priority).ThenBy(x => x,StringComparer.Ordinal).ToArray();
            var text = new StringBuilder(
                """
                INSERT INTO #v9pairs(a,b,part)
                SELECT DISTINCT a.obs_id,b.obs_id,a.part
                FROM #v9keys ka
                JOIN #v9keys kb ON kb.feature=@anchor
                    AND ka.feature=@anchor AND ka.value=kb.value AND ka.obs_id<kb.obs_id
                JOIN #v9obs a ON a.obs_id=ka.obs_id
                JOIN #v9obs b ON b.obs_id=kb.obs_id
                WHERE a.part=b.part AND a.gestor<>b.gestor
                  AND a.cpf IS NULL AND b.cpf IS NULL
                  AND a.nascimento IS NOT NULL AND b.nascimento IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM #v9pairs c
                    WHERE c.a=a.obs_id AND c.b=b.obs_id)
                """);
            for(var i=1;i<ordered.Length;i++)
                text.Append(CultureInfo.InvariantCulture, $"""
                
                  AND EXISTS (
                    SELECT 1 FROM #v9keys x
                    JOIN #v9keys y ON x.value=y.value AND y.feature=@f{i}
                    WHERE x.feature=@f{i} AND x.obs_id=a.obs_id AND y.obs_id=b.obs_id)
                """);
            await using var cmd = new SqlCommand(text.ToString(),connection) {
                CommandTimeout = 600 };
            cmd.Parameters.Add("@anchor",SqlDbType.VarChar,90).Value = ordered[0];
            for (var i=1;i<ordered.Length;i++)
                cmd.Parameters.Add("@f"+i,SqlDbType.VarChar,90).Value = ordered[i];
            await cmd.ExecuteNonQueryAsync(ct);
            await using var guard = new SqlCommand(
                "SELECT COUNT_BIG(*) FROM #v9pairs;",connection);
            var count = (long)(await guard.ExecuteScalarAsync(ct) ?? 0L);
            if(count > HardPairCap)
                throw new InvalidOperationException(
                    "Ruleset ativo gerou mais de 1 milhão de pares; censo não amostra U.");
        }
        long after;
        await using(var cmd = new SqlCommand("SELECT COUNT_BIG(*) FROM #v9pairs;",connection))
            after = (long)(await cmd.ExecuteScalarAsync(ct) ?? 0L);
        return (fingerprint, passes.Length, after-before, passes);
    }

    private sealed class Acc(string partition)
    {
        public long Eligible, TrueCandidate, FalseCandidate, Tp, Fp;
        public Split Build()
        {
            var accepted = Tp + Fp;
            return new Split(partition, Eligible, TrueCandidate, FalseCandidate,
                Eligible - TrueCandidate,
                TrueCandidate == 0 ? 0 : (decimal)FalseCandidate / TrueCandidate,
                Eligible == 0 ? 0 : (decimal)FalseCandidate / Eligible,
                TrueCandidate + FalseCandidate == 0 ? 0
                    : (decimal)FalseCandidate / (TrueCandidate + FalseCandidate),
                Tp, Fp, Eligible - Tp, FalseCandidate - Fp,
                Eligible == 0 ? 0 : (decimal)Tp / Eligible,
                FalseCandidate == 0 ? null : (decimal)Fp / FalseCandidate,
                accepted == 0 ? null : (decimal)Fp / accepted,
                Tp == 0 ? null : (decimal)Fp / Tp);
        }
    }

    private static V9SyntheticBaseFrequencyAudit.BasePerson[] ReadBasePeople(string csv)
    {
        using var source = new StringReader(csv);
        var header = SplitCsv(source.ReadLine() ?? throw new InvalidDataException("Massa-base vazia."));
        var indexes = header.Select((name, pos) => (name, pos))
            .ToDictionary(x => x.name, x => x.pos, StringComparer.Ordinal);
        foreach (var required in new[] { "base_person_id", "particao", "nome", "nome_mae" })
            if (!indexes.ContainsKey(required))
                throw new InvalidDataException("Massa-base sem coluna: " + required);
        var rows = new List<V9SyntheticBaseFrequencyAudit.BasePerson>();
        string? line;
        while ((line = source.ReadLine()) is not null)
        {
            if (line.Length == 0) continue;
            var cells = SplitCsv(line);
            string C(string name) => cells[indexes[name]];
            rows.Add(new V9SyntheticBaseFrequencyAudit.BasePerson(
                C("base_person_id"), C("particao"),
                Empty(C("nome")), Empty(C("nome_mae"))));
        }
        return rows.ToArray();
    }

    private static Observation[] ReadObservations(string csv)
    {
        using var source = new StringReader(csv);
        var header = SplitCsv(source.ReadLine() ?? throw new InvalidDataException("CSV vazio."));
        var indexes = header.Select((name, pos) => (name, pos))
            .ToDictionary(x => x.name, x => x.pos, StringComparer.Ordinal);
        foreach (var required in new[] { "observacao_id","base_person_id","particao",
                    "gestor","nome","nome_mae","data_nascimento","cpf" })
            if (!indexes.ContainsKey(required))
                throw new InvalidDataException("CSV sem coluna: "+required);
        var rows = new List<Observation>();
        var known = new HashSet<string>(StringComparer.Ordinal);
        string? line;
        while ((line = source.ReadLine()) is not null)
        {
            if (line.Length == 0) continue;
            var cells = SplitCsv(line);
            string C(string name) => cells[indexes[name]];
            var id = C("observacao_id");
            if (!known.Add(id)) throw new InvalidDataException("Observação duplicada.");
            var birthText = C("data_nascimento");
            DateOnly? birth = null;
            if (!string.IsNullOrEmpty(birthText))
            {
                if (!DateOnly.TryParseExact(birthText,"yyyy-MM-dd",
                    CultureInfo.InvariantCulture,DateTimeStyles.None,out var date))
                    throw new InvalidDataException("Data de nascimento sintética inválida.");
                birth = date;
            }
            var name = Empty(C("nome"));
            var mother = Empty(C("nome_mae"));
            var projected = BlockingProjectionKeyProjector.Project(name,mother,birth)
                .ToDictionary(x => x.Feature+"\u001e"+x.Value,x => x.Value,StringComparer.Ordinal);
            string? Key(string feature) => projected.Keys
                .Where(k=>k.StartsWith(feature+"\u001e",StringComparison.Ordinal))
                .Select(k=>projected[k]).FirstOrDefault();
            var part = C("particao");
            if (part is not ("TRAIN" or "VALIDATION" or "TEST"))
                throw new InvalidDataException("Partição desconhecida.");
            rows.Add(new Observation(rows.Count,C("base_person_id"),part,C("gestor"),
                name,mother,birth,Empty(C("cpf")),
                Key(BlockingFeatureNames.FullName),
                Key(BlockingFeatureNames.MotherFullName),
                Key(BlockingFeatureNames.FullNamePhoneticPtBr),
                Key(BlockingFeatureNames.MotherFullNamePhoneticPtBr)));
        }
        return rows.ToArray();
    }

    private static string? Empty(string s) => s.Length == 0 ? null : s;

    private static string[] SplitCsv(string line)
    {
        var fields = new List<string>();
        var value = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (quoted && i + 1 < line.Length && line[i+1] == '"')
                    { value.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (ch == ',' && !quoted) { fields.Add(value.ToString()); value.Clear(); }
            else value.Append(ch);
        }
        if (quoted) throw new InvalidDataException("CSV com aspas sem fechar.");
        fields.Add(value.ToString());
        return fields.ToArray();
    }
}
