using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jornada.Contracts;
using Jornada.Linkage.Runner;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Opt-in, explicitly noncertifying V9 operational TEST of existing SQL rows.
/// Reads Silver from an explicitly named external database with a SELECT-only
/// account. Does not create a corpus, SQL model, table, run or Gold/Serving row.
/// The candidate graph is rebuilt in memory using the winner's frozen blocking
/// passes, not the yet-unproven production RunAsync pipeline. No truth is
/// available, so no TP/FP/FN, FDR, recall or IID claims can be emitted.
/// </summary>
public static class V9ExternalDatabaseReadOnlyTest
{
    public const int MaximumObservations = 100_000;
    public const int MaximumCandidateEdges = 2_000_000;
    public const int MaximumBlockingKeys = 2_000_000;
    public const string ReportSchema = "V9_EXTERNAL_DATABASE_READONLY_OPERATIONAL_DIAGNOSTIC_V1";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string ValidateDatabaseName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128
            || !Regex.IsMatch(name, @"^[a-zA-Z][a-zA-Z0-9_]{0,127}$",
                RegexOptions.CultureInvariant)
            || name.Equals("master", StringComparison.OrdinalIgnoreCase)
            || name.Equals("model", StringComparison.OrdinalIgnoreCase)
            || name.Equals("msdb", StringComparison.OrdinalIgnoreCase)
            || name.Equals("tempdb", StringComparison.OrdinalIgnoreCase)
            || name.Equals("JornadaHarness", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "TEST em database existente exige nome explícito, seguro e distinto do banco sintético/sistema.");
        return name;
    }

    /// <summary>Overrides ONLY Initial Catalog; server/credentials come from a separate secret.</summary>
    public static SqlConnectionStringBuilder BuildReadOnlyConnection(string source, string databaseName)
    {
        ValidateDatabaseName(databaseName);
        if (string.IsNullOrWhiteSpace(source))
            throw new InvalidOperationException("Conexão externa TEST somente leitura não configurada.");
        var builder = new SqlConnectionStringBuilder(source)
        {
            InitialCatalog = databaseName,
            ApplicationIntent = ApplicationIntent.ReadOnly,
            Pooling = false,
            Enlist = false,
            PersistSecurityInfo = false
        };
        if (string.IsNullOrWhiteSpace(builder.DataSource)
            || !string.IsNullOrEmpty(builder.AttachDBFilename))
            throw new InvalidDataException("TEST externo exige servidor SQL configurado sem attach local.");
        return builder;
    }

    /// <summary>
    /// A SQL readonly principal is mandatory. ApplicationIntent alone does
    /// not prohibit writes on a primary replica, so verify SQL effective
    /// permissions BEFORE fetching any citizen row.
    /// </summary>
    public static async Task VerifyReadOnlyPrincipalAsync(
        SqlConnection connection, string expectedDatabase, CancellationToken ct = default)
    {
        if (connection.State != ConnectionState.Open)
            throw new InvalidOperationException("Conexão externa TEST precisa estar aberta.");
        await using var command = new SqlCommand(
            """
            SELECT DB_NAME(),
                CASE WHEN IS_SRVROLEMEMBER('sysadmin') = 1
                    OR IS_ROLEMEMBER('db_owner') = 1
                    OR IS_ROLEMEMBER('db_datawriter') = 1
                    OR COALESCE(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CONTROL'), 0) = 1
                    OR COALESCE(HAS_PERMS_BY_NAME(N'silver.pessoa_observacao', 'OBJECT', 'INSERT'), 0) = 1
                    OR COALESCE(HAS_PERMS_BY_NAME(N'silver.pessoa_observacao', 'OBJECT', 'UPDATE'), 0) = 1
                    OR COALESCE(HAS_PERMS_BY_NAME(N'silver.pessoa_observacao', 'OBJECT', 'DELETE'), 0) = 1
                    OR COALESCE(HAS_PERMS_BY_NAME(N'gold.pessoa', 'OBJECT', 'INSERT'), 0) = 1
                    OR COALESCE(HAS_PERMS_BY_NAME(N'gold.pessoa', 'OBJECT', 'UPDATE'), 0) = 1
                    OR COALESCE(HAS_PERMS_BY_NAME(N'gold.pessoa', 'OBJECT', 'DELETE'), 0) = 1
                    OR COALESCE(HAS_PERMS_BY_NAME(N'identidade.linkage_resultado', 'OBJECT', 'INSERT'), 0) = 1
                    OR COALESCE(HAS_PERMS_BY_NAME(N'identidade.linkage_resultado', 'OBJECT', 'UPDATE'), 0) = 1
                    OR COALESCE(HAS_PERMS_BY_NAME(N'identidade.linkage_resultado', 'OBJECT', 'DELETE'), 0) = 1
                    THEN 1 ELSE 0 END AS has_writes,
                COALESCE(HAS_PERMS_BY_NAME(N'silver.pessoa_observacao', 'OBJECT', 'SELECT'), 0),
                COALESCE(HAS_PERMS_BY_NAME(N'ref.gestor', 'OBJECT', 'SELECT'), 0),
                CASE WHEN OBJECT_ID(N'silver.pessoa_observacao', 'U') IS NOT NULL
                          AND OBJECT_ID(N'ref.gestor', 'U') IS NOT NULL THEN 1 ELSE 0 END;
            """, connection) { CommandTimeout = 90 };
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)
            || !string.Equals(reader.GetString(0), expectedDatabase, StringComparison.Ordinal)
            || reader.GetInt32(1) != 0 || reader.GetInt32(2) != 1
            || reader.GetInt32(3) != 1 || reader.GetInt32(4) != 1)
            throw new InvalidOperationException(
                "TEST externo exige banco exato, schema Silver/gestor compatível e login exclusivamente SELECT.");
    }

    public static async Task<V9PhysicalCalibrationObservation[]> ReadExistingObservationsAsync(
        SqlConnection connection, CancellationToken ct = default)
    {
        await using var command = new SqlCommand(
            """
            SELECT TOP (@max_plus_one)
                po.pessoa_observacao_id, g.codigo, po.nome_completo,
                po.nome_mae, po.data_nascimento
            FROM silver.pessoa_observacao po
            JOIN ref.gestor g ON g.gestor_id=po.gestor_id
            WHERE po.cpf IS NULL
            ORDER BY po.pessoa_observacao_id;
            """, connection) { CommandTimeout = 900 };
        command.Parameters.Add("@max_plus_one", SqlDbType.Int).Value = MaximumObservations + 1;
        var rows = new List<V9PhysicalCalibrationObservation>();
        await using var reader = await command.ExecuteReaderAsync(
            CommandBehavior.SequentialAccess, ct);
        long lastDatabaseObservationId = -1;
        while (await reader.ReadAsync(ct))
        {
            if (rows.Count == MaximumObservations)
                throw new InvalidDataException(
                    "Database externo excede 100000 observações sem CPF; não truncar amostra nem atribuir métricas de cobertura.");
            var sourceId = reader.GetInt64(0);
            if (sourceId <= lastDatabaseObservationId)
                throw new InvalidDataException("Banco externo não apresenta observações ordenadas e únicas.");
            lastDatabaseObservationId = sourceId;
            var manager = reader.IsDBNull(1) ? null : reader.GetString(1);
            if (string.IsNullOrWhiteSpace(manager))
                throw new InvalidDataException("Observação do database externo sem gestor.");
            var name = reader.IsDBNull(2) ? null : reader.GetString(2);
            var mother = reader.IsDBNull(3) ? null : reader.GetString(3);
            var dob = reader.IsDBNull(4) ? (DateOnly?)null
                : DateOnly.FromDateTime(reader.GetDateTime(4));
            // This is a SOURCE identifier, NOT truth. Unique on purpose to
            // prevent accidental TP/FP claims from production citizen IDs.
            rows.Add(new V9PhysicalCalibrationObservation(
                rows.Count + 1, "UNLABELED_EXTERNAL_OBSERVATION_" + (rows.Count + 1),
                "TEST", manager, name, mother, dob));
        }
        if (rows.Count < 2)
            throw new InvalidDataException("TEST externo exige ao menos duas observações sem CPF.");
        return rows.ToArray();
    }

    /// <summary>
    /// Exact frozen-pass OR/AND key semantics, read-only memory projection.
    /// Refuse excessive fanout/keys before any silent truncation. Edges are
    /// NOT from production SQL materialization, hence noncertifying only.
    /// </summary>
    public static IReadOnlyList<(int A, int B)> BuildDiagnosticCandidateGraph(
        V9FittedSnapshot winner, IReadOnlyCollection<V9PhysicalCalibrationObservation> rows)
    {
        ArgumentNullException.ThrowIfNull(winner);
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count is < 2 or > MaximumObservations
            || rows.Any(x => x.Partition != "TEST" || x.ObservationId <= 0
                || string.IsNullOrWhiteSpace(x.Gestor))
            || rows.Select(x => x.ObservationId).Distinct().Count() != rows.Count)
            throw new InvalidDataException("Coorte TEST externa inválida.");
        // The Runner is a separate hosted project and cannot be referenced
        // from the Worker. Use the same public frozen Core projections instead.
        var version = winner.Payload.ProjectionSchemaVersion;
        int? prefix = null;
        HashSet<string>? supported = null;
        if (version == V9CalibrationBlockingProjection.SchemaVersion)
        {
            var matches = Enumerable.Range(1,
                V9CalibrationBlockingProjection.MaximumSupportedPrefixLength)
                .Where(n => V9CalibrationBlockingProjection.ComputeSchemaFingerprint(n)
                    .Equals(winner.Payload.ProjectionFingerprintSha256,
                        StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1)
                throw new InvalidDataException("Database TEST exige projeção V9 fixada válida.");
            prefix = matches[0];
            supported = V9CalibrationBlockingProjection.SupportedFeatures(prefix.Value)
                .ToHashSet(StringComparer.Ordinal);
        }
        else
        {
            PersonResolutionProjectionContract.ValidateSupported(
                version, winner.Payload.ProjectionFingerprintSha256);
        }
        foreach (var feature in winner.Payload.BlockingPasses.SelectMany(p => p.Fields))
            if (supported is not null && !supported.Contains(feature))
                throw new InvalidDataException("Feature fora do blocking V9 congelado.");
        var index = new Dictionary<string, List<V9PhysicalCalibrationObservation>>(
            StringComparer.Ordinal);
        var edges = new HashSet<(int A, int B)>();
        long expandedKeys = 0;
        foreach (var row in rows.OrderBy(x => x.ObservationId))
        {
            var observation = new IdentityObservation(null, "SEM_CPF",
                row.PersonName, row.BirthDate, row.MotherName);
            var projection = prefix is int selectedPrefix
                ? V9CalibrationBlockingProjection.Project(
                    observation.NomeCompleto, observation.NomeMae,
                    observation.DataNascimento, selectedPrefix)
                : BlockingProjectionKeyProjector.Project(
                    observation.NomeCompleto, observation.NomeMae,
                    observation.DataNascimento)
                    .Concat(PersonResolutionBlockingProjector.Project(
                        observation.ResolutionAttributes))
                    .Distinct().ToArray();
            var values = projection.GroupBy(k => k.Feature, StringComparer.Ordinal)
                .ToDictionary(g => g.Key,
                    g => g.Select(k => k.Value).Distinct(StringComparer.Ordinal)
                        .OrderBy(v => v, StringComparer.Ordinal).ToArray(),
                    StringComparer.Ordinal);
            foreach (var pass in winner.Payload.BlockingPasses)
            {
                if (!pass.Fields.All(values.ContainsKey))
                    continue;
                IEnumerable<string> combinations = [pass.PassId];
                foreach (var feature in pass.Fields)
                {
                    var featureName = feature;
                    combinations = combinations.SelectMany(
                        (string key) => values[featureName].Select(
                            (string value) => key + "\u001f" + featureName.Length
                                + ":" + featureName + "=" + value.Length + ":" + value));
                }
                foreach (var key in combinations)
                {
                    expandedKeys++;
                    if (expandedKeys > MaximumBlockingKeys)
                        throw new InvalidDataException(
                            "Fanout externo excedeu cap de chaves; não truncar candidatos.");
                    if (!index.TryGetValue(key, out var peers))
                    {
                        index.Add(key, [row]);
                        continue;
                    }
                    foreach (var peer in peers)
                    {
                        if (string.Equals(peer.Gestor, row.Gestor, StringComparison.Ordinal))
                            continue;
                        var edge = (Math.Min(peer.ObservationId, row.ObservationId),
                            Math.Max(peer.ObservationId, row.ObservationId));
                        if (edges.Add(edge) && edges.Count > MaximumCandidateEdges)
                            throw new InvalidDataException(
                                "Fanout externo excedeu cap de arestas; não amostrar pares.");
                    }
                    peers.Add(row);
                }
            }
        }
        return edges.OrderBy(x => x.Item1).ThenBy(x => x.Item2).ToArray();
    }

    /// <summary>
    /// Fast connection/schema/permissions admission: must run before any
    /// expensive 324-hypothesis physical TRAIN if a database TEST was selected.
    /// No SELECT of citizen data or model, and no database mutation.
    /// </summary>
    public static async Task PreflightAsync(string databaseName, CancellationToken ct = default)
    {
        var source = Environment.GetEnvironmentVariable("JORNADA_V9_EXTERNAL_TEST_SQL_CONNECTION")
            ?? throw new InvalidOperationException("TEST externo exige segredo SQL SELECT-only.");
        var builder = BuildReadOnlyConnection(source, databaseName);
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(ct);
        await VerifyReadOnlyPrincipalAsync(connection, databaseName, ct);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            status = "V9_EXTERNAL_DB_READONLY_PREFLIGHT_PASS_NO_PERSONAL_ROWS",
            databaseName, generatedSyntheticData = false, readOnly = true,
            certified = false
        }));
    }

    public static async Task RunAsync(
        string modelFile, string databaseName, CancellationToken ct = default)
    {
        var database = ValidateDatabaseName(databaseName);
        // No corpus generator, TEST lock or synthetic gabarito in this path.
        var output = Environment.GetEnvironmentVariable("JORNADA_V9_TEST_OUTPUT_DIR")
            ?? throw new InvalidOperationException("Saída privada do TEST externo não configurada.");
        var fullOutput = Path.GetFullPath(output);
        if (Directory.Exists(fullOutput) || File.Exists(fullOutput)
            || fullOutput.StartsWith(Path.GetDirectoryName(modelFile)! + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
            throw new InvalidDataException("Output externo precisa ser novo e separado do modelo.");
        var winner = await V9ValidatedOperationalModelFile.ReadPinnedAsync(modelFile, ct);
        var connectionSecret = Environment.GetEnvironmentVariable("JORNADA_V9_EXTERNAL_TEST_SQL_CONNECTION")
            ?? throw new InvalidOperationException("Conexão externa SELECT-only deve vir de segredo separado.");
        var builder = BuildReadOnlyConnection(connectionSecret, database);
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(ct);
        await VerifyReadOnlyPrincipalAsync(connection, database, ct);
        var rows = await ReadExistingObservationsAsync(connection, ct);
        // No INSERT, UPDATE, DELETE, CREATE, model materialization, Gold or
        // Serving operations: everything after the SQL SELECT stays in memory.
        var edges = BuildDiagnosticCandidateGraph(winner, rows);
        var replay = V9FrozenSourceResolveReplay.ReplayTest(winner, rows, edges);
        if (replay.SnapshotSha256 != winner.ContentSha256
            || replay.Decisions.Count != rows.Length
            || replay.IdentityByObservation.Count != rows.Length)
            throw new InvalidDataException("Diagnóstico externo não conciliou decisões e observações.");
        var resolved = replay.Decisions.Count(x =>
            x.Decision.Status == ResolutionStatus.RESOLVIDO);
        var conflicts = replay.Decisions.Count(x =>
            x.Decision.Status == ResolutionStatus.CONFLITO);
        var unresolved = replay.Decisions.Count - resolved - conflicts;
        var groups = replay.IdentityByObservation.Values.Distinct().Count();
        // SHA of rows' ordering/observation count without persisting PII, IDs,
        // names, CPF, Gold groups or source input. Avoid putting secrets in logs.
        var version = JsonSerializer.SerializeToUtf8Bytes(new
        {
            database, rows = rows.Length, winner = winner.ContentSha256,
            blockingEdges = edges.Count, resolved, unresolved, conflicts, groups
        });
        var aggregateHash = Convert.ToHexString(SHA256.HashData(version))
            .ToLowerInvariant();
        Directory.CreateDirectory(fullOutput);
        var report = new
        {
            schema = ReportSchema,
            status = "EXTERNAL_DATABASE_NONPUBLISHING_DIAGNOSTIC_NOT_SCIENTIFIC_TEST",
            databaseName = database,
            databaseOrigin = "EXISTING_DATABASE_NO_SYNTHETIC_GENERATION",
            winnerSnapshotSha256 = winner.ContentSha256,
            operationalModelFileName = Path.GetFileName(modelFile),
            operationalModelContract = V9OperationalSqlModelConverter.ContractVersion,
            observationCount = rows.Length, candidateEdgeCount = edges.Count,
            resolved, unresolved, conflicts, inferredGroups = groups,
            aggregateSha256 = aggregateHash,
            inputReadOnly = true, generatedSyntheticData = false,
            independentScientificTest = false, goldTruthAvailable = false,
            tpFpFnFdrRecallEvaluated = false,
            candidateGraphParityWithProductionCertified = false,
            productionRunnerFullPipelineParityCertified = false,
            operationallyCertified = false, testUsed = false, refitted = false,
            published = false, activated = false
        };
        await using var target = new FileStream(Path.Combine(fullOutput, "external-database-test.json"),
            FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await target.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(report, JsonOptions), ct);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            report.schema, report.status, report.databaseName,
            report.observationCount, report.winnerSnapshotSha256,
            report.operationallyCertified
        }));
    }
}
