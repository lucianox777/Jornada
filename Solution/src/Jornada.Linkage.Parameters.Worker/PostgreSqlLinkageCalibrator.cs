using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Jornada.Contracts;
using Jornada.Operational.Sql;
using Microsoft.Extensions.Configuration;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>Parâmetros técnicos da calibração. Valores reduzidos só são permitidos no banco sintético descartável.</summary>
public sealed record PostgreSqlCalibrationOptions(
    int SampleSize, int PoolSize, int MinimumIndependentMatchedPairs,
    decimal SmoothingAlpha, decimal Threshold, decimal ConflictMargin,
    int CommandTimeoutSeconds, bool Synthetic = false)
{
    public static PostgreSqlCalibrationOptions FromConfiguration(IConfiguration configuration) => new(
        configuration.GetValue("LinkageParameters:TrainingSampleSize", 250_000),
        configuration.GetValue("LinkageParameters:TrainingSamplePoolSize", 1_000_000),
        configuration.GetValue("LinkageParameters:MinimumIndependentMatchedPairs", 5_000),
        configuration.GetValue("LinkageParameters:SmoothingAlpha", 0.5m),
        configuration.GetValue("LinkageParameters:TLinkage", 0.95m),
        configuration.GetValue("LinkageParameters:ConflictMargin", 0.03m),
        configuration.GetValue("LinkageParameters:ReadCommandTimeoutSeconds", 900));

    public void Validate()
    {
        if (SampleSize < 1 || SampleSize > 1_000_000 || PoolSize < SampleSize || PoolSize > 5_000_000)
            throw new ArgumentOutOfRangeException(nameof(SampleSize), "Amostra e pool devem ser positivos, limitados e coerentes.");
        if (MinimumIndependentMatchedPairs < (Synthetic ? 1 : 100) || MinimumIndependentMatchedPairs > SampleSize)
            throw new ArgumentOutOfRangeException(nameof(MinimumIndependentMatchedPairs));
        if (SmoothingAlpha <= 0 || SmoothingAlpha > 1000 || Threshold < 0.5m || Threshold > 0.999999m ||
            ConflictMargin <= 0 || ConflictMargin > 0.5m || CommandTimeoutSeconds < 1 || CommandTimeoutSeconds > 3600)
            throw new ArgumentOutOfRangeException(nameof(SmoothingAlpha), "Parâmetros de calibração fora dos limites técnicos.");
    }
}

public sealed record PostgreSqlCalibrationDraft(Guid ModelId, int Version, long Population, int MatchedPairs, int UnmatchedPairs);

/// <summary>
/// Gera parâmetros usando o estimador canônico. Uma transação REPEATABLE READ READ ONLY
/// congela as leituras do corpus, sem bloquear o Processor nem copiar a Gold inteira.
/// Apenas RASCUNHO é produzido. VALIDATE é explícito e ACTIVATE não é implementado aqui.
/// </summary>
public sealed class PostgreSqlLinkageCalibrator
{
    private const string Algorithm = "FELLEGI_SUNTER_BIRTH_COMPONENTS_V2";
    private const string SampleMethod = "M_INTERGESTOR_U_GOLD_MVCC_V2";
    private readonly IOperationalDatabaseAdapter database;

    public PostgreSqlLinkageCalibrator(IOperationalDatabaseAdapter database)
    {
        if (database.Provider != OperationalDatabaseProviders.PostgreSql)
            throw new ArgumentException("O calibrador PostgreSQL exige o provider PostgreSql.", nameof(database));
        this.database = database;
    }

    public async Task<PostgreSqlCalibrationDraft> GenerateDraftAsync(PostgreSqlCalibrationOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        if (options.Synthetic)
            await RequireDisposableDatabaseAsync(ct);
        var modelId = Guid.NewGuid();
        var version = await ReserveModelAsync(modelId, options, ct);
        try
        {
            var capture = await CaptureAsync(options, ct);
            if (capture.Population.Population <= 0 || capture.Population.DistinctBirthDates <= 0)
                throw new InvalidOperationException("A população Gold de referência está vazia ou inválida.");
            if (capture.M.Pairs.Count < options.MinimumIndependentMatchedPairs)
                throw new InvalidOperationException("Amostra m independente insuficiente; o modelo não será validado nem ativado.");
            if (capture.U.Pairs.Count == 0)
                throw new InvalidOperationException("Amostra u condicionada ao blocking vazia.");

            var estimated = LinkageParameterEstimator.Estimate(
                capture.M.Pairs, capture.U.Pairs, capture.Population.Population,
                capture.Population.DistinctBirthDates, options.SmoothingAlpha, options.Threshold, options.ConflictMargin);
            var parameters = new Dictionary<string, decimal>(estimated, StringComparer.Ordinal)
            {
                ["POPULATION_SIZE"] = capture.Population.Population,
                ["POPULATION_WITH_CPF"] = capture.Population.WithCpf,
                ["DISTINCT_FULL_NAME_APPROX"] = capture.Population.DistinctNames,
                ["DISTINCT_MOTHER_NAME_APPROX"] = capture.Population.DistinctMothers,
                ["DISTINCT_BIRTH_DATE"] = capture.Population.DistinctBirthDates,
                ["TRAINING_SAMPLE_POOL_SIZE"] = options.PoolSize,
                ["MIN_M_INDEPENDENT_PAIRS"] = options.MinimumIndependentMatchedPairs
            };
            // O banco é NUMERIC(30,12). O fingerprint deve representar exatamente o valor persistido.
            var rounded = parameters.ToDictionary(p => p.Key,
                p => decimal.Round(p.Value, 12, MidpointRounding.AwayFromZero), StringComparer.Ordinal);
            await PersistDraftAsync(modelId, capture, rounded, options, ct);
            return new PostgreSqlCalibrationDraft(modelId, version, capture.Population.Population,
                capture.M.Pairs.Count, capture.U.Pairs.Count);
        }
        catch (Exception ex)
        {
            // Não persistir textos de exceção que possam conter dados pessoais ou parâmetros de conexão.
            await MarkFailedBestEffortAsync(modelId, ex.GetType().Name);
            throw;
        }
    }

    public async Task<Guid> ValidateDraftAsync(int version, CancellationToken ct)
    {
        if (version <= 0) throw new ArgumentOutOfRangeException(nameof(version));
        await using var connection = await database.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            var result = await ScalarAsync(connection, transaction,
                "SELECT identidade.validar_modelo_linkage_pg(@version);", 60, ct,
                P("version", DbType.Int32, version));
            var modelId = (Guid)(result ?? throw new InvalidOperationException("Validação sem modelo retornado."));
            await transaction.CommitAsync(ct);
            return modelId;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task RequireDisposableDatabaseAsync(CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        var name = (string?)await ScalarAsync(connection, null, "SELECT current_database();", 30, ct);
        if (name != "JornadaPgCalibrationTest" ||
            Environment.GetEnvironmentVariable("JORNADA_POSTGRESQL_CALIBRATION_TESTS") != "1")
            throw new InvalidOperationException("Amostras sintéticas reduzidas exigem opt-in e JornadaPgCalibrationTest.");
    }

    private async Task<int> ReserveModelAsync(Guid modelId, PostgreSqlCalibrationOptions options, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            // Versionamento serializado por lock transacional, sem MAX+1 concorrente desprotegido.
            await ScalarAsync(connection, transaction, "SELECT pg_advisory_xact_lock(741020,1);", 60, ct);
            var version = Convert.ToInt32(await ScalarAsync(connection, transaction,
                "SELECT COALESCE(MAX(versao),0)+1 FROM identidade.modelo_linkage;", 60, ct), CultureInfo.InvariantCulture);
            await ExecuteAsync(connection, transaction, """
                INSERT INTO identidade.modelo_linkage(
                    modelo_id,versao,status,algoritmo_versao,normalizacao_versao,deduplicacao_metodo,
                    base_referencia,gerado_em,amostra_metodo,amostra_pool_tamanho)
                VALUES(@id,@version,'GERANDO',@algorithm,@normalization,'GOLD_PESSOA_UUID_PK',
                    @reference,CURRENT_TIMESTAMP,@method,@pool);
                """, 60, ct,
                P("id", DbType.Guid, modelId), P("version", DbType.Int32, version),
                P("algorithm", DbType.String, Algorithm), P("normalization", DbType.String, IdentityComparison.NormalizationVersion),
                P("reference", DbType.String, options.Synthetic ? "CI_LINKAGE_SYNTHETIC" : "gold.pessoa"),
                P("method", DbType.String, SampleMethod), P("pool", DbType.Int32, options.PoolSize));
            await transaction.CommitAsync(ct);
            return version;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<Capture> CaptureAsync(PostgreSqlCalibrationOptions options, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        try
        {
            await ExecuteAsync(connection, transaction, "SET TRANSACTION READ ONLY;", options.CommandTimeoutSeconds, ct);
            await using var snapshotCommand = Command(connection, transaction,
                "SELECT txid_current_snapshot()::text, clock_timestamp();", options.CommandTimeoutSeconds);
            string token;
            DateTimeOffset capturedAt;
            await using (var reader = await snapshotCommand.ExecuteReaderAsync(ct))
            {
                if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Snapshot PostgreSQL indisponível.");
                token = reader.GetString(0);
                capturedAt = reader.GetFieldValue<DateTimeOffset>(1);
            }
            await using var profileCommand = Command(connection, transaction, """
                SELECT COUNT(*)::bigint,COUNT(*) FILTER (WHERE cpf IS NOT NULL)::bigint,
                       COUNT(DISTINCT nome_completo)::bigint,COUNT(DISTINCT nome_mae)::bigint,
                       COUNT(DISTINCT data_nascimento)::bigint,MAX(atualizado_em)
                  FROM gold.pessoa;
                """, options.CommandTimeoutSeconds);
            Population population;
            await using (var reader = await profileCommand.ExecuteReaderAsync(ct))
            {
                if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Perfil populacional indisponível.");
                population = new Population(reader.GetInt64(0),reader.GetInt64(1),reader.GetInt64(2),
                    reader.GetInt64(3),reader.GetInt64(4),reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5));
            }
            // As consultas retornam no máximo SampleSize pares. UUIDs/IDs compõem somente fingerprints.
            var m = await ReadPairsAsync(connection, transaction, MatchedSql, options, ct);
            var u = await ReadPairsAsync(connection, transaction, UnmatchedSql, options, ct);
            var frequencies = await ReadFrequenciesAsync(connection, transaction, population.Population, options, ct);
            await transaction.CommitAsync(ct);
            var fingerprint = HashText(string.Join("\n", token, population.Population.ToString(CultureInfo.InvariantCulture),
                population.WithCpf.ToString(CultureInfo.InvariantCulture), m.Hash, u.Hash,
                capturedAt.ToString("O", CultureInfo.InvariantCulture)) + "\n");
            return new Capture(token, capturedAt, fingerprint, population, m, u, frequencies);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private const string MatchedSql = """
        WITH gold_sample AS (
            SELECT g.pessoa_uuid,g.cpf FROM gold.pessoa g
            JOIN identidade.pessoa ip ON ip.pessoa_uuid=g.pessoa_uuid AND ip.status='ATIVO'
            WHERE g.cpf IS NOT NULL ORDER BY g.pessoa_uuid LIMIT @pool
        ), obs_por_gestor AS (
            SELECT vf.pessoa_uuid,po.gestor_id,po.pessoa_observacao_id,
                   po.nome_completo,po.data_nascimento,po.nome_mae,
                   ROW_NUMBER() OVER(PARTITION BY vf.pessoa_uuid,po.gestor_id
                       ORDER BY po.source_as_of DESC,po.pessoa_observacao_id DESC) rn_gestor
            FROM gold_sample gs
            JOIN identidade.vinculo_fonte vf ON vf.pessoa_uuid=gs.pessoa_uuid
                 AND vf.ativo AND vf.status='RESOLVIDO' AND vf.metodo_resolucao='CPF_DETERMINISTICO'
            JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=vf.pessoa_observacao_id
            WHERE po.cpf IS NOT NULL AND po.cpf=gs.cpf
        ), fontes AS (
            SELECT o.*,g.codigo gestor_codigo,
                   ROW_NUMBER() OVER(PARTITION BY o.pessoa_uuid
                       ORDER BY md5(o.pessoa_uuid::text||':'||o.gestor_id::text),o.gestor_id,o.pessoa_observacao_id) rn_fonte
            FROM obs_por_gestor o JOIN ref.gestor g ON g.gestor_id=o.gestor_id
            WHERE o.rn_gestor=1
        )
        SELECT a.pessoa_uuid,a.pessoa_uuid,a.pessoa_observacao_id,b.pessoa_observacao_id,
               a.nome_completo,a.data_nascimento,a.nome_mae,
               b.nome_completo,b.data_nascimento,b.nome_mae,a.gestor_codigo,b.gestor_codigo
        FROM fontes a JOIN fontes b ON b.pessoa_uuid=a.pessoa_uuid AND b.rn_fonte=2
        WHERE a.rn_fonte=1 AND a.gestor_id<>b.gestor_id
        ORDER BY md5(a.pessoa_uuid::text),a.pessoa_uuid LIMIT @sample;
        """;

    private const string UnmatchedSql = """
        WITH gold_sample AS (
            SELECT g.pessoa_uuid,g.nome_completo,g.data_nascimento,g.nome_mae
            FROM gold.pessoa g JOIN identidade.pessoa ip ON ip.pessoa_uuid=g.pessoa_uuid AND ip.status='ATIVO'
            WHERE g.cpf IS NOT NULL ORDER BY g.pessoa_uuid LIMIT @pool
        ), ranked AS (
            SELECT *,ROW_NUMBER() OVER(PARTITION BY data_nascimento ORDER BY pessoa_uuid) rn FROM gold_sample
        ), pares AS (
            SELECT a.pessoa_uuid a_uuid,b.pessoa_uuid b_uuid,
                   a.nome_completo a_nome,a.data_nascimento a_nascimento,a.nome_mae a_mae,
                   b.nome_completo b_nome,b.data_nascimento b_nascimento,b.nome_mae b_mae
            FROM ranked a JOIN ranked b ON b.data_nascimento=a.data_nascimento AND b.rn=a.rn+1
            WHERE a.rn%2=1 AND a.pessoa_uuid<>b.pessoa_uuid
        )
        SELECT a_uuid,b_uuid,NULL::bigint,NULL::bigint,a_nome,a_nascimento,a_mae,
               b_nome,b_nascimento,b_mae,NULL::text,NULL::text
        FROM pares ORDER BY a_uuid,b_uuid LIMIT @sample;
        """;

    private static async Task<PairSample> ReadPairsAsync(DbConnection connection, DbTransaction transaction,
        string sql, PostgreSqlCalibrationOptions options, CancellationToken ct)
    {
        var pairs = new List<IdentityTrainingPair>();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var command = Command(connection, transaction, sql, options.CommandTimeoutSeconds,
            P("pool",DbType.Int32,options.PoolSize),P("sample",DbType.Int32,options.SampleSize));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var key = string.Join("|",reader.GetGuid(0).ToString("D"),reader.GetGuid(1).ToString("D"),
                reader.IsDBNull(2) ? "" : reader.GetInt64(2).ToString(CultureInfo.InvariantCulture),
                reader.IsDBNull(3) ? "" : reader.GetInt64(3).ToString(CultureInfo.InvariantCulture)) + "\n";
            hash.AppendData(Encoding.UTF8.GetBytes(key));
            pairs.Add(new IdentityTrainingPair(reader.GetString(4),DateOnly.FromDateTime(reader.GetDateTime(5)),reader.GetString(6),
                reader.GetString(7),DateOnly.FromDateTime(reader.GetDateTime(8)),reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),reader.IsDBNull(11) ? null : reader.GetString(11)));
        }
        return new PairSample(pairs,Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }

    private static async Task<IReadOnlyList<Frequency>> ReadFrequenciesAsync(DbConnection connection,DbTransaction transaction,
        long population,PostgreSqlCalibrationOptions options,CancellationToken ct)
    {
        if (population<=0) return Array.Empty<Frequency>();
        var rows = new List<Frequency>();
        await using var command = Command(connection,transaction,"""
            SELECT atributo,valor,ocorrencias FROM (
                SELECT 'NASC_DIA'::text atributo,EXTRACT(DAY FROM data_nascimento)::int valor,COUNT(*)::bigint ocorrencias FROM gold.pessoa GROUP BY 2
                UNION ALL
                SELECT 'NASC_MES',EXTRACT(MONTH FROM data_nascimento)::int,COUNT(*)::bigint FROM gold.pessoa GROUP BY 2
                UNION ALL
                SELECT 'NASC_ANO',EXTRACT(YEAR FROM data_nascimento)::int,COUNT(*)::bigint FROM gold.pessoa GROUP BY 2
            ) f ORDER BY atributo,valor;
            """,options.CommandTimeoutSeconds);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rows.Add(new Frequency(reader.GetString(0),reader.GetInt32(1).ToString(CultureInfo.InvariantCulture),reader.GetInt64(2)));
        return rows;
    }

    private async Task PersistDraftAsync(Guid modelId,Capture capture,IReadOnlyDictionary<string,decimal> parameters,
        PostgreSqlCalibrationOptions options,CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted,ct);
        try
        {
            var status = (string?)await ScalarAsync(connection,transaction,
                "SELECT status FROM identidade.modelo_linkage WHERE modelo_id=@id FOR UPDATE;",60,ct,P("id",DbType.Guid,modelId));
            if (status!="GERANDO") throw new InvalidOperationException("O modelo deixou o estado GERANDO.");
            foreach (var (name,value) in parameters.OrderBy(p=>p.Key,StringComparer.Ordinal))
                await ExecuteAsync(connection,transaction,"INSERT INTO identidade.parametro_linkage(modelo_id,nome,valor) VALUES(@id,@name,@value);",60,ct,
                    P("id",DbType.Guid,modelId),P("name",DbType.String,name),P("value",DbType.Decimal,value));
            var p = capture.Population;
            foreach (var (name,value,method) in new (string,decimal,string)[] {
                ("POPULATION_SIZE",p.Population,"COUNT"),("POPULATION_WITH_CPF",p.WithCpf,"COUNT_FILTER"),
                ("DISTINCT_FULL_NAME",p.DistinctNames,"COUNT_DISTINCT"),("DISTINCT_MOTHER_NAME",p.DistinctMothers,"COUNT_DISTINCT"),
                ("DISTINCT_BIRTH_DATE",p.DistinctBirthDates,"COUNT_DISTINCT"),("M_SAMPLE_SIZE",capture.M.Pairs.Count,"STABLE_HASH_PAIR_SAMPLE"),
                ("U_SAMPLE_SIZE",capture.U.Pairs.Count,"EXACT_BIRTH_GOLD_PAIR_SAMPLE") })
                await ExecuteAsync(connection,transaction,"INSERT INTO identidade.estatistica_linkage(modelo_id,nome,valor,metodo) VALUES(@id,@name,@value,@method);",60,ct,
                    P("id",DbType.Guid,modelId),P("name",DbType.String,name),P("value",DbType.Decimal,value),P("method",DbType.String,method));
            foreach (var f in capture.Frequencies)
                await ExecuteAsync(connection,transaction,"""
                    INSERT INTO identidade.frequencia_linkage(modelo_id,atributo,valor_normalizado,ocorrencias,populacao_referencia,frequencia)
                    VALUES(@id,@attribute,@value,@count,@population,@frequency);
                    """,60,ct,P("id",DbType.Guid,modelId),P("attribute",DbType.String,f.Attribute),P("value",DbType.String,f.Value),
                    P("count",DbType.Int64,f.Count),P("population",DbType.Int64,p.Population),
                    P("frequency",DbType.Decimal,decimal.Round((decimal)f.Count/p.Population,12,MidpointRounding.AwayFromZero)));
            await ExecuteAsync(connection,transaction,"""
                INSERT INTO identidade.calibracao_linkage(modelo_id,metodo_amostragem,snapshot_token,snapshot_sha256,
                    amostra_m_sha256,amostra_u_sha256,parametros_sha256,amostra_minima_m,sintetico,capturado_em)
                VALUES(@id,@method,@token,@snapshot_hash,@m_hash,@u_hash,@p_hash,@minimum,@synthetic,@captured);
                """,60,ct,P("id",DbType.Guid,modelId),P("method",DbType.String,SampleMethod),P("token",DbType.String,capture.Token),
                P("snapshot_hash",DbType.String,capture.Hash),P("m_hash",DbType.String,capture.M.Hash),P("u_hash",DbType.String,capture.U.Hash),
                P("p_hash",DbType.String,ParameterHash(parameters)),P("minimum",DbType.Int32,options.MinimumIndependentMatchedPairs),
                P("synthetic",DbType.Boolean,options.Synthetic),P("captured",DbType.DateTimeOffset,capture.CapturedAt));
            var reference = $"gold.pessoa;pg_snapshot_sha256={capture.Hash};corpus_utc={capture.CapturedAt:O}";
            var changed = await ExecuteAsync(connection,transaction,"""
                UPDATE identidade.modelo_linkage SET status='RASCUNHO',snapshot_referencia=@reference,
                    registros_lidos=@population,pessoas_unicas=@population,snapshot_capturado_em=@captured,
                    amostra_m_tamanho=@m,amostra_u_tamanho=@u,falha_resumo=NULL
                WHERE modelo_id=@id AND status='GERANDO';
                """,60,ct,P("id",DbType.Guid,modelId),P("reference",DbType.String,reference),
                P("population",DbType.Int64,p.Population),P("captured",DbType.DateTime2,DateTime.SpecifyKind(capture.CapturedAt.UtcDateTime,DateTimeKind.Unspecified)),
                P("m",DbType.Int32,capture.M.Pairs.Count),P("u",DbType.Int32,capture.U.Pairs.Count));
            if (changed!=1) throw new InvalidOperationException("A publicação atômica do rascunho não atualizou exatamente um modelo.");
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task MarkFailedBestEffortAsync(Guid modelId,string errorCode)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await using var connection = await database.OpenAsync(timeout.Token);
            await ExecuteAsync(connection,null,"""
                UPDATE identidade.modelo_linkage SET status='FALHOU',falha_resumo=@reason
                WHERE modelo_id=@id AND status='GERANDO';
                """,15,timeout.Token,P("id",DbType.Guid,modelId),P("reason",DbType.String,errorCode));
        }
        catch (Exception) { /* A falha original prevalece; o GERANDO remanescente requer recuperação explícita. */ }
    }

    public static string ParameterHash(IReadOnlyDictionary<string,decimal> parameters) => HashText(
        string.Join("\n",parameters.OrderBy(p=>p.Key,StringComparer.Ordinal)
            .Select(p=>p.Key+"="+p.Value.ToString("0.000000000000",CultureInfo.InvariantCulture)))+"\n");

    private static string HashText(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static (string Name,DbType Type,object? Value) P(string name,DbType type,object? value) => (name,type,value);
    private static DbCommand Command(DbConnection connection,DbTransaction? transaction,string sql,int timeout,
        params (string Name,DbType Type,object? Value)[] parameters)
    {
        var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText=sql;command.CommandTimeout=timeout;
        foreach(var (name,type,value) in parameters)
        {
            var p=command.CreateParameter();p.ParameterName=name;p.DbType=type;p.Value=value??DBNull.Value;
            if(type==DbType.Decimal){p.Precision=30;p.Scale=12;}
            command.Parameters.Add(p);
        }
        return command;
    }
    private static async Task<object?> ScalarAsync(DbConnection connection,DbTransaction? transaction,string sql,int timeout,CancellationToken ct,
        params (string Name,DbType Type,object? Value)[] parameters)
    {
        await using var command=Command(connection,transaction,sql,timeout,parameters);
        return await command.ExecuteScalarAsync(ct);
    }
    private static async Task<int> ExecuteAsync(DbConnection connection,DbTransaction? transaction,string sql,int timeout,CancellationToken ct,
        params (string Name,DbType Type,object? Value)[] parameters)
    {
        await using var command=Command(connection,transaction,sql,timeout,parameters);
        return await command.ExecuteNonQueryAsync(ct);
    }

    private sealed record Population(long Population,long WithCpf,long DistinctNames,long DistinctMothers,long DistinctBirthDates,DateTimeOffset? MaxUpdatedAt);
    private sealed record PairSample(IReadOnlyList<IdentityTrainingPair> Pairs,string Hash);
    private sealed record Frequency(string Attribute,string Value,long Count);
    private sealed record Capture(string Token,DateTimeOffset CapturedAt,string Hash,Population Population,PairSample M,PairSample U,IReadOnlyList<Frequency> Frequencies);
}
