using System.Data;
using System.Globalization;
using Jornada.Contracts;
using Jornada.Pipeline.Coordination;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Gera, valida e ativa os parâmetros estatísticos do fallback probabilístico.
///
/// Escala: não carrega a Gold inteira em memória e não persiste uma cópia de frequências
/// de alta cardinalidade por versão. A população é perfilada no SQL Server e m/u são
/// estimados a partir de amostras limitadas e reprodutíveis por ordenação da PK UUID.
/// As probabilidades m usam pares entre observações de Gestores distintos ligadas pelo
/// mesmo CPF/UUID determinístico, evitando circularidade Silver→Gold→treino.
///
/// Concorrência Fase 1: GENERATE_DRAFT obtém janela exclusiva do corpus integrado. A API continua
/// recebendo Entregas/Bronze, mas o Processor termina o lote corrente e não inicia outro até o fim
/// da geração. Assim estatísticas e amostras leem um corpus imóvel sem depender de SQL SNAPSHOT.
/// VALIDATE/ACTIVATE não precisam do gate do corpus e mantêm seus locks transacionais específicos.
/// </summary>
public sealed class LinkageParametersWorker(
    ILogger<LinkageParametersWorker> logger,
    IConfiguration configuration,
    IOperationalSqlAdapter operationalSql,
    SqlPipelineCoordinator pipelineCoordinator,
    IHostApplicationLifetime applicationLifetime) : BackgroundService
{
    private const string DraftOperation = "GENERATE_DRAFT";
    private const string ValidateOperation = "VALIDATE";
    private const string ActivateOperation = "ACTIVATE";
    private const string CurrentAlgorithmVersion = LinkageParameterCatalog.SemanticBirthAlgorithmVersion;
    private const string SqlServerSampleMethod = "M_INTERGESTOR_U_BLOCKING_RULESET_V3";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var operation = configuration.GetValue("LinkageParameters:Operation", DraftOperation)!
            .Trim()
            .ToUpperInvariant();

        logger.LogInformation("Linkage Parameters Worker iniciado. Operação={Operation}; corpus=gold.pessoa", operation);

        if (operation is ValidateOperation or ActivateOperation)
        {
            try
            {
                var targetVersion = configuration.GetValue<int?>("LinkageParameters:TargetVersion")
                    ?? throw new InvalidOperationException(
                        "LinkageParameters:TargetVersion é obrigatório para VALIDATE/ACTIVATE.");

                if (operation == ValidateOperation)
                    await ValidateDraftAsync(targetVersion, stoppingToken);
                else
                    await ActivateValidatedAsync(targetVersion, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha na operação explícita {Operation} dos parâmetros de linkage.", operation);
                Environment.ExitCode = 1;
            }
            finally
            {
                applicationLifetime.StopApplication();
            }
            return;
        }

        if (operation != DraftOperation)
            throw new InvalidOperationException(
                $"Operação de parâmetros desconhecida: {operation}. Use {DraftOperation}, {ValidateOperation} ou {ActivateOperation}.");

        var runOnce = configuration.GetValue("LinkageParameters:RunOnce", true);
        var intervalMinutes = Math.Max(1, configuration.GetValue("LinkageParameters:RefreshMinutes", 10_080));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await IsInitialLoadModeActiveAsync(stoppingToken))
                    logger.LogWarning("GENERATE_DRAFT não executado porque controle.modo_carga_inicial está ativo.");
                else
                    await GenerateDraftFromGoldAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha ao gerar versão RASCUNHO dos parâmetros de linkage a partir da Gold.");
                Environment.ExitCode = 1;
            }

            if (runOnce)
            {
                applicationLifetime.StopApplication();
                return;
            }

            await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
        }
    }

    private async Task<bool> IsInitialLoadModeActiveAsync(CancellationToken ct)
    {
        await using var connection = await operationalSql.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ativo FROM controle.modo_carga_inicial WHERE estado_id=1;";
        return Convert.ToBoolean(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
    }

    private async Task GenerateDraftFromGoldAsync(CancellationToken cancellationToken)
    {
        var algorithmVersion = configuration.GetValue("LinkageParameters:AlgorithmVersion", CurrentAlgorithmVersion)!.Trim();
        if (!string.Equals(algorithmVersion, CurrentAlgorithmVersion, StringComparison.Ordinal))
            throw new InvalidOperationException($"Novos modelos SQL Server devem declarar AlgorithmVersion={CurrentAlgorithmVersion}; recebido={algorithmVersion}.");

        var normalizationVersion = configuration.GetValue("LinkageParameters:NormalizationVersion", "IDENTITY_NORMALIZATION_V1")!;
        var sampleSize = Math.Max(1_000, configuration.GetValue("LinkageParameters:TrainingSampleSize", 250_000));
        var samplePoolSize = Math.Max(sampleSize, configuration.GetValue("LinkageParameters:TrainingSamplePoolSize", 1_000_000));
        var minimumIndependentMatchedPairs = Math.Max(100, configuration.GetValue("LinkageParameters:MinimumIndependentMatchedPairs", 5_000));
        var smoothingAlpha = Math.Max(0.0001m, configuration.GetValue("LinkageParameters:SmoothingAlpha", 0.5m));
        var threshold = Math.Clamp(configuration.GetValue("LinkageParameters:TLinkage", 0.95m), 0.5m, 0.999999m);
        var conflictMargin = Math.Clamp(configuration.GetValue("LinkageParameters:ConflictMargin", 0.03m), 0.0001m, 0.5m);
        var blockingSearchOptions = BlockingRuleSetSearchConfiguration.FromConfiguration(configuration);
        var readCommandTimeoutSeconds = Math.Max(30, configuration.GetValue("LinkageParameters:ReadCommandTimeoutSeconds", 900));

        await using var connection = await operationalSql.OpenAsync(cancellationToken);
        var drainTimeoutSeconds = Math.Max(30, configuration.GetValue("PipelineCoordination:CurrentBatchDrainTimeoutSeconds", 900));
        await using var pipelineLease = await pipelineCoordinator.AcquireExclusiveJobAsync(
            "Jornada.Linkage.Parameters.GENERATE_DRAFT", TimeSpan.FromSeconds(drainTimeoutSeconds), cancellationToken);
        using var workCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, pipelineLease.LostToken);
        var workCt = workCts.Token;
        var modelId = Guid.NewGuid();

        try
        {
            var version = await CreateGeneratingModelAsync(connection, modelId, algorithmVersion, normalizationVersion, samplePoolSize, workCt);
            PopulationStatistics statistics;
            IReadOnlyList<IdentityTrainingPair> matchedPairs;
            IReadOnlyList<IdentityTrainingPair> unmatchedCandidatePairs;
            DateTime corpusCapturedAtUtc;

            try
            {
                corpusCapturedAtUtc = await ReadCorpusTimestampAsync(connection, workCt);
                statistics = await ReadPopulationStatisticsAsync(connection, workCt);
                if (statistics.PopulationSize <= 0)
                    throw new InvalidOperationException("Gold Pessoas vazia. A primeira ingestão cadastral elegível deve formar o baseline da Gold antes da geração de parâmetros.");

                matchedPairs = await ReadDeterministicMatchedPairsAsync(connection, sampleSize, samplePoolSize, workCt);
                unmatchedCandidatePairs = await ReadGoldUnmatchedPairsAsync(connection, normalizationVersion, sampleSize, samplePoolSize, workCt);

                if (matchedPairs.Count < minimumIndependentMatchedPairs)
                    throw new InvalidOperationException($"Amostra m independente insuficiente: {matchedPairs.Count} pares inter-Gestores; mínimo={minimumIndependentMatchedPairs}. O modelo permanece sem publicação até existir evidência independente suficiente.");
                if (unmatchedCandidatePairs.Count == 0)
                    throw new InvalidOperationException("Amostra u do universo de chaves de blocking vazia. O modelo permanece sem publicação.");
            }
            catch (SqlException ex) when (ex.Number == -2)
            {
                throw new TimeoutException($"Uma consulta de captura do Parameters Worker excedeu {readCommandTimeoutSeconds}s. Nenhum modelo foi publicado; verifique carga/índices e calibre o timeout homologado antes de reagendar GENERATE_DRAFT.", ex);
            }

            var blockingObservations = BlockingFeatureObservationFactory.Create(matchedPairs, unmatchedCandidatePairs);
            var blocking = BlockingRuleSetSearch.SearchBest(blockingObservations, BlockingCandidateFeatureCatalog.RequiredCalibratorCandidates, blockingSearchOptions);

            var unmatchedSample = await BlockingConditionedUnmatchedPairReader.ReadAsync(
                connection, normalizationVersion, blocking.Passes, sampleSize, samplePoolSize, readCommandTimeoutSeconds, workCt);
            var unmatchedPairs = unmatchedSample.Pairs;
            if (unmatchedPairs.Count == 0)
                throw new InvalidOperationException("Amostra u vazia no universo do ruleset vencedor. O modelo permanece sem publicação.");

            var modelParameters = LinkageParameterEstimator.Estimate(
                matchedPairs, unmatchedPairs, statistics.PopulationSize, statistics.DistinctBirthDates, smoothingAlpha, threshold, conflictMargin);

            var persistedParameters = BuildPersistedParameters(
                modelParameters, statistics, samplePoolSize, minimumIndependentMatchedPairs,
                unmatchedCandidatePairs.Count, unmatchedSample.SemanticBirthPoolSupport, unmatchedSample.CandidatePoolSize);

            var ruleSet = LinkageDynamicRuleSet.CreateWithPasses($"MODEL_{version}_BLOCKING_V1", algorithmVersion, blocking.Passes, persistedParameters);
            await PublishDraftModelAsync(connection, modelId, corpusCapturedAtUtc, statistics, matchedPairs, unmatchedPairs.Count, persistedParameters, ruleSet, workCt);

            logger.LogInformation(
                "Modelo probabilístico v{Version} criado em RASCUNHO com ruleset {RuleSetVersion}. População={Population}; m={M}; u_candidatos={UCandidates}; u_condicionado={UConditioned}; u_pool_ruleset={UPool}; corpus_capturado_em={CorpusCapturedAt:O}; amostra={SampleMethod}; pool={Pool}.",
                version, ruleSet.RuleSetVersion, statistics.PopulationSize, matchedPairs.Count, unmatchedCandidatePairs.Count,
                unmatchedPairs.Count, unmatchedSample.CandidatePoolSize, corpusCapturedAtUtc, SqlServerSampleMethod, samplePoolSize);
        }
        catch (OperationCanceledException) when (pipelineLease.IsLost)
        {
            await MarkModelFailedAsync(connection, modelId, "PIPELINE_COORDINATION_LOST", CancellationToken.None);
            throw new InvalidOperationException($"Sessão coordenadora do pipeline foi perdida durante GENERATE_DRAFT (SPID={pipelineLease.SessionId}); geração abortada fail-closed.");
        }
        catch (Exception ex)
        {
            await MarkModelFailedAsync(connection, modelId, ex.Message, CancellationToken.None);
            throw;
        }
    }

    private static IReadOnlyDictionary<string, decimal> BuildPersistedParameters(
        IReadOnlyDictionary<string, decimal> estimatedParameters,
        PopulationStatistics statistics,
        int samplePoolSize,
        int minimumIndependentMatchedPairs,
        int unmatchedCandidateSampleSize,
        IReadOnlyDictionary<string, long> semanticBirthPoolSupport,
        long conditionedCandidatePoolSize)
    {
        var parameters = new Dictionary<string, decimal>(estimatedParameters, StringComparer.Ordinal)
        {
            ["POPULATION_SIZE"] = statistics.PopulationSize,
            ["POPULATION_WITH_CPF"] = statistics.WithCpf,
            ["DISTINCT_FULL_NAME_APPROX"] = statistics.DistinctFullNames,
            ["DISTINCT_MOTHER_NAME_APPROX"] = statistics.DistinctMotherNames,
            ["DISTINCT_BIRTH_DATE"] = statistics.DistinctBirthDates,
            ["TRAINING_SAMPLE_POOL_SIZE"] = samplePoolSize,
            ["MIN_M_INDEPENDENT_PAIRS"] = minimumIndependentMatchedPairs,
            ["BLOCKING_U_CANDIDATE_SAMPLE_SIZE"] = unmatchedCandidateSampleSize,
            ["BLOCKING_U_CONDITIONED_CANDIDATE_POOL_SIZE"] = conditionedCandidatePoolSize
        };
        foreach (var state in BirthDateSemanticEvidence.States)
            parameters[$"POOL_SUPPORT_U_NASCIMENTO_SEMANTICO_{state}"] = semanticBirthPoolSupport.TryGetValue(state, out var count) ? count : 0;

        return parameters.ToDictionary(
            static pair => pair.Key,
            static pair => decimal.Round(pair.Value, 12, MidpointRounding.AwayFromZero),
            StringComparer.Ordinal);
    }

    private async Task<int> CreateGeneratingModelAsync(SqlConnection connection, Guid modelId, string algorithmVersion, string normalizationVersion, int samplePoolSize, CancellationToken cancellationToken)
    {
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var command = new SqlCommand(
                """
                DECLARE @lock_result INT;
                EXEC @lock_result = sys.sp_getapplock @Resource = 'Jornada.Linkage.Parameters.Version', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 60000;
                IF @lock_result < 0 THROW 51009, 'Não foi possível obter lock para versionamento do modelo.', 1;
                DECLARE @versao INT = (SELECT ISNULL(MAX(versao),0)+1 FROM identidade.modelo_linkage WITH (UPDLOCK,HOLDLOCK));
                INSERT identidade.modelo_linkage(modelo_id,versao,status,algoritmo_versao,normalizacao_versao,deduplicacao_metodo,base_referencia,snapshot_referencia,registros_lidos,pessoas_unicas,gerado_em,ativado_em,snapshot_capturado_em,amostra_metodo,amostra_pool_tamanho,amostra_m_tamanho,amostra_u_tamanho,falha_resumo)
                VALUES(@modelo_id,@versao,'GERANDO',@algoritmo,@normalizacao,'GOLD_PESSOA_UUID_PK','gold.pessoa',NULL,NULL,NULL,SYSDATETIMEOFFSET(),NULL,NULL,@amostra_metodo,@pool,NULL,NULL,NULL);
                SELECT @versao;
                """, connection, transaction);
            command.Parameters.Add("@modelo_id", SqlDbType.UniqueIdentifier).Value = modelId;
            command.Parameters.Add("@algoritmo", SqlDbType.NVarChar, 80).Value = algorithmVersion;
            command.Parameters.Add("@normalizacao", SqlDbType.NVarChar, 80).Value = normalizationVersion;
            command.Parameters.Add("@amostra_metodo", SqlDbType.NVarChar, 80).Value = SqlServerSampleMethod;
            command.Parameters.Add("@pool", SqlDbType.Int).Value = samplePoolSize;
            var version = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            await transaction.CommitAsync(cancellationToken);
            return version;
        }
        catch { await transaction.RollbackAsync(cancellationToken); throw; }
    }

    private async Task<DateTime> ReadCorpusTimestampAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var command = new SqlCommand("SELECT SYSUTCDATETIME();", connection);
        return Convert.ToDateTime(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private async Task<PopulationStatistics> ReadPopulationStatisticsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var command = new SqlCommand(
            """
            SELECT COUNT_BIG(*) AS population_size,
                   SUM(CONVERT(BIGINT,CASE WHEN cpf IS NOT NULL THEN 1 ELSE 0 END)) AS with_cpf,
                   APPROX_COUNT_DISTINCT(nome_completo) AS distinct_full_name,
                   APPROX_COUNT_DISTINCT(nome_mae) AS distinct_mother_name,
                   APPROX_COUNT_DISTINCT(data_nascimento) AS distinct_birth_date,
                   MAX(atualizado_em) AS max_updated_at
            FROM gold.pessoa;
            """, connection)
        { CommandTimeout = Math.Max(30, configuration.GetValue("LinkageParameters:ReadCommandTimeoutSeconds", 900)) };
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return new PopulationStatistics(0, 0, 0, 0, 0, null);
        return new PopulationStatistics(reader.GetInt64(0), reader.IsDBNull(1) ? 0 : reader.GetInt64(1),
            Convert.ToInt64(reader.GetValue(2), CultureInfo.InvariantCulture), Convert.ToInt64(reader.GetValue(3), CultureInfo.InvariantCulture),
            Convert.ToInt64(reader.GetValue(4), CultureInfo.InvariantCulture), reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5));
    }

    private async Task<IReadOnlyList<IdentityTrainingPair>> ReadDeterministicMatchedPairsAsync(SqlConnection connection, int sampleSize, int samplePoolSize, CancellationToken cancellationToken)
    {
        var command = new SqlCommand(
            """
            WITH gold_sample AS (
                SELECT TOP (@pool_size) pessoa_uuid FROM gold.pessoa ORDER BY pessoa_uuid
            ), obs_por_gestor AS (
                SELECT vf.pessoa_uuid,po.gestor_id,po.pessoa_observacao_id,po.nome_completo,po.data_nascimento,po.nome_mae,
                       ROW_NUMBER() OVER (PARTITION BY vf.pessoa_uuid,po.gestor_id ORDER BY po.source_as_of DESC,po.pessoa_observacao_id DESC) AS rn_gestor
                FROM gold_sample gs
                JOIN identidade.vinculo_fonte vf ON vf.pessoa_uuid=gs.pessoa_uuid AND vf.ativo=1 AND vf.status='RESOLVIDO' AND vf.metodo_resolucao='CPF_DETERMINISTICO'
                JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=vf.pessoa_observacao_id
                WHERE po.cpf IS NOT NULL
            ), fontes_independentes AS (
                SELECT opg.pessoa_uuid,opg.gestor_id,g.codigo AS gestor_codigo,opg.pessoa_observacao_id,opg.nome_completo,opg.data_nascimento,opg.nome_mae,
                       ROW_NUMBER() OVER (PARTITION BY opg.pessoa_uuid ORDER BY HASHBYTES('SHA2_256',CONCAT(CONVERT(nvarchar(36),opg.pessoa_uuid),':',CONVERT(nvarchar(20),opg.gestor_id))),opg.gestor_id,opg.pessoa_observacao_id) AS rn_fonte,
                       COUNT_BIG(*) OVER (PARTITION BY opg.pessoa_uuid) AS qtd_fontes
                FROM obs_por_gestor opg JOIN ref.gestor g ON g.gestor_id=opg.gestor_id WHERE opg.rn_gestor=1
            )
            SELECT TOP (@sample_size) a.nome_completo,a.data_nascimento,a.nome_mae,b.nome_completo,b.data_nascimento,b.nome_mae,a.gestor_codigo,b.gestor_codigo
            FROM fontes_independentes a JOIN fontes_independentes b ON b.pessoa_uuid=a.pessoa_uuid AND a.rn_fonte=1 AND b.rn_fonte=2
            WHERE a.qtd_fontes>=2 AND a.gestor_id<>b.gestor_id
            ORDER BY HASHBYTES('SHA2_256',CONVERT(nvarchar(36),a.pessoa_uuid)),a.pessoa_uuid;
            """, connection)
        { CommandTimeout = Math.Max(30, configuration.GetValue("LinkageParameters:ReadCommandTimeoutSeconds", 900)) };
        command.Parameters.Add("@sample_size", SqlDbType.Int).Value = sampleSize;
        command.Parameters.Add("@pool_size", SqlDbType.Int).Value = samplePoolSize;
        return await ReadTrainingPairsAsync(command, cancellationToken);
    }

    private async Task<IReadOnlyList<IdentityTrainingPair>> ReadGoldUnmatchedPairsAsync(SqlConnection connection, string normalizationVersion, int sampleSize, int samplePoolSize, CancellationToken cancellationToken)
    {
        var projection = BlockingCandidateFeatureCatalog.CurrentResolutionProjectionPlan;
        var features = BlockingCandidateFeatureCatalog.RequiredCalibratorCandidates;
        var featureParameters = features.Select((_, index) => $"@feature_{index}").ToArray();
        var command = new SqlCommand(
            $"""
            WITH gold_sample AS (
                SELECT TOP (@pool_size) pessoa_uuid,nome_completo,data_nascimento,nome_mae FROM gold.pessoa ORDER BY pessoa_uuid
            ), eligible_keys AS (
                SELECT DISTINCT k.pessoa_uuid,k.atributo,k.valor_normalizado
                FROM identidade.blocking_chave k JOIN gold_sample gs ON gs.pessoa_uuid=k.pessoa_uuid
                WHERE k.normalizacao_versao=@normalizacao AND k.projection_schema_version=@projection_schema
                  AND k.projection_fingerprint_sha256=@projection_fingerprint AND k.atributo IN ({string.Join(",", featureParameters)})
                  AND (k.semantica_temporal<>'STABLE_IDENTITY_DATUM' OR k.vigencia_fim IS NULL)
            ), ranked_keys AS (
                SELECT pessoa_uuid,atributo,valor_normalizado,
                       ROW_NUMBER() OVER (PARTITION BY atributo,valor_normalizado ORDER BY HASHBYTES('SHA2_256',CONVERT(nvarchar(36),pessoa_uuid)),pessoa_uuid) AS rn
                FROM eligible_keys
            ), candidate_pairs AS (
                SELECT DISTINCT a.pessoa_uuid AS a_uuid,b.pessoa_uuid AS b_uuid
                FROM ranked_keys a JOIN ranked_keys b ON b.atributo=a.atributo AND b.valor_normalizado=a.valor_normalizado AND b.rn=a.rn+1
                WHERE a.rn % 2=1 AND a.pessoa_uuid<>b.pessoa_uuid
            )
            SELECT TOP (@sample_size) a.nome_completo,a.data_nascimento,a.nome_mae,b.nome_completo,b.data_nascimento,b.nome_mae
            FROM candidate_pairs p JOIN gold_sample a ON a.pessoa_uuid=p.a_uuid JOIN gold_sample b ON b.pessoa_uuid=p.b_uuid
            ORDER BY HASHBYTES('SHA2_256',CONCAT(CONVERT(nvarchar(36),p.a_uuid),':',CONVERT(nvarchar(36),p.b_uuid))),p.a_uuid,p.b_uuid;
            """, connection)
        { CommandTimeout = Math.Max(30, configuration.GetValue("LinkageParameters:ReadCommandTimeoutSeconds", 900)) };
        command.Parameters.Add("@sample_size", SqlDbType.Int).Value = sampleSize;
        command.Parameters.Add("@pool_size", SqlDbType.Int).Value = samplePoolSize;
        command.Parameters.Add("@normalizacao", SqlDbType.NVarChar, 80).Value = normalizationVersion;
        command.Parameters.Add("@projection_schema", SqlDbType.NVarChar, 120).Value = projection.SchemaVersion;
        command.Parameters.Add("@projection_fingerprint", SqlDbType.Char, 64).Value = projection.Fingerprint;
        for (var index = 0; index < features.Count; index++) command.Parameters.Add(featureParameters[index], SqlDbType.NVarChar, 80).Value = features[index];
        return await ReadTrainingPairsAsync(command, cancellationToken);
    }

    private static async Task<IReadOnlyList<IdentityTrainingPair>> ReadTrainingPairsAsync(SqlCommand command, CancellationToken cancellationToken)
    {
        var result = new List<IdentityTrainingPair>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new IdentityTrainingPair(reader.GetString(0), DateOnly.FromDateTime(reader.GetDateTime(1)), reader.GetString(2), reader.GetString(3), DateOnly.FromDateTime(reader.GetDateTime(4)), reader.GetString(5), reader.FieldCount > 6 && !reader.IsDBNull(6) ? reader.GetString(6) : null, reader.FieldCount > 7 && !reader.IsDBNull(7) ? reader.GetString(7) : null));
        return result;
    }

    private async Task PublishDraftModelAsync(SqlConnection connection, Guid modelId, DateTime corpusCapturedAtUtc, PopulationStatistics statistics, IReadOnlyList<IdentityTrainingPair> matchedPairs, int unmatchedSampleSize, IReadOnlyDictionary<string, decimal> parameters, LinkageDynamicRuleSet ruleSet, CancellationToken cancellationToken)
    {
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            foreach (var (name, value) in parameters)
            {
                var command = new SqlCommand("INSERT identidade.parametro_linkage(modelo_id,nome,valor) VALUES(@modelo_id,@nome,@valor);", connection, transaction);
                command.Parameters.Add("@modelo_id", SqlDbType.UniqueIdentifier).Value = modelId;
                command.Parameters.Add("@nome", SqlDbType.NVarChar, 100).Value = name;
                var valueParameter = command.Parameters.Add("@valor", SqlDbType.Decimal); valueParameter.Precision = 30; valueParameter.Scale = 12; valueParameter.Value = value;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            var statisticsRows = new (string Name, decimal Value, string Method)[]
            {
                ("POPULATION_SIZE", statistics.PopulationSize, "COUNT_BIG"),
                ("POPULATION_WITH_CPF", statistics.WithCpf, "SUM_CASE"),
                ("DISTINCT_FULL_NAME", statistics.DistinctFullNames, "APPROX_COUNT_DISTINCT"),
                ("DISTINCT_MOTHER_NAME", statistics.DistinctMotherNames, "APPROX_COUNT_DISTINCT"),
                ("DISTINCT_BIRTH_DATE", statistics.DistinctBirthDates, "APPROX_COUNT_DISTINCT")
            };
            foreach (var row in statisticsRows)
            {
                var command = new SqlCommand("INSERT identidade.estatistica_linkage(modelo_id,nome,valor,metodo) VALUES(@modelo_id,@nome,@valor,@metodo);", connection, transaction);
                command.Parameters.Add("@modelo_id", SqlDbType.UniqueIdentifier).Value = modelId;
                command.Parameters.Add("@nome", SqlDbType.NVarChar, 100).Value = row.Name;
                var valueParameter = command.Parameters.Add("@valor", SqlDbType.Decimal); valueParameter.Precision = 30; valueParameter.Scale = 6; valueParameter.Value = row.Value;
                command.Parameters.Add("@metodo", SqlDbType.NVarChar, 80).Value = row.Method;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            var gestorPairDistribution = matchedPairs.Where(p => !string.IsNullOrWhiteSpace(p.LeftSourceCode) && !string.IsNullOrWhiteSpace(p.RightSourceCode))
                .GroupBy(p => string.CompareOrdinal(p.LeftSourceCode, p.RightSourceCode) <= 0 ? $"{p.LeftSourceCode}__{p.RightSourceCode}" : $"{p.RightSourceCode}__{p.LeftSourceCode}", StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal);
            foreach (var pair in gestorPairDistribution)
            {
                var command = new SqlCommand("INSERT identidade.estatistica_linkage(modelo_id,nome,valor,metodo) VALUES(@modelo_id,@nome,@valor,@metodo);", connection, transaction);
                command.Parameters.Add("@modelo_id", SqlDbType.UniqueIdentifier).Value = modelId;
                command.Parameters.Add("@nome", SqlDbType.NVarChar, 100).Value = $"M_GESTOR_PAIR_{pair.Key}";
                var valueParameter = command.Parameters.Add("@valor", SqlDbType.Decimal); valueParameter.Precision = 30; valueParameter.Scale = 6; valueParameter.Value = pair.LongCount();
                command.Parameters.Add("@metodo", SqlDbType.NVarChar, 80).Value = "STABLE_HASH_PAIR_SAMPLE";
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await LinkageRuleSetWriter.WriteAsync(connection, transaction, modelId, ruleSet, cancellationToken);
            var snapshotReference = statistics.MaxGoldUpdatedAt is null ? $"gold.pessoa;corpus_utc={corpusCapturedAtUtc:O}" : $"gold.pessoa;corpus_utc={corpusCapturedAtUtc:O};max_atualizado={statistics.MaxGoldUpdatedAt:O}";
            var update = new SqlCommand(
                """
                UPDATE identidade.modelo_linkage
                SET status='RASCUNHO',snapshot_referencia=@snapshot_referencia,registros_lidos=@population,pessoas_unicas=@population,
                    snapshot_capturado_em=@snapshot_capturado_em,amostra_m_tamanho=@m_size,amostra_u_tamanho=@u_size,falha_resumo=NULL
                WHERE modelo_id=@modelo_id AND status='GERANDO';
                IF @@ROWCOUNT<>1 THROW 51020, 'Modelo deixou de estar em GERANDO antes da publicação do RASCUNHO.', 1;
                """, connection, transaction);
            update.Parameters.Add("@modelo_id", SqlDbType.UniqueIdentifier).Value = modelId;
            update.Parameters.Add("@snapshot_referencia", SqlDbType.NVarChar, 300).Value = snapshotReference;
            update.Parameters.Add("@population", SqlDbType.BigInt).Value = statistics.PopulationSize;
            update.Parameters.Add("@snapshot_capturado_em", SqlDbType.DateTime2).Value = corpusCapturedAtUtc;
            update.Parameters.Add("@m_size", SqlDbType.Int).Value = matchedPairs.Count;
            update.Parameters.Add("@u_size", SqlDbType.Int).Value = unmatchedSampleSize;
            await update.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch { await transaction.RollbackAsync(cancellationToken); throw; }
    }

    private async Task MarkModelFailedAsync(SqlConnection connection, Guid modelId, string message, CancellationToken cancellationToken)
    {
        if (connection.State != ConnectionState.Open) return;
        var summary = message.Length <= 500 ? message : message[..500];
        var command = new SqlCommand("UPDATE identidade.modelo_linkage SET status='FALHOU',falha_resumo=@falha WHERE modelo_id=@modelo_id AND status='GERANDO';", connection);
        command.Parameters.Add("@modelo_id", SqlDbType.UniqueIdentifier).Value = modelId;
        command.Parameters.Add("@falha", SqlDbType.NVarChar, 500).Value = summary;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task ValidateDraftAsync(int version, CancellationToken cancellationToken)
    {
        await using var connection = await operationalSql.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var command = new SqlCommand(
                """
                DECLARE @modelo_id UNIQUEIDENTIFIER; DECLARE @status NVARCHAR(20); DECLARE @registros BIGINT; DECLARE @pessoas BIGINT; DECLARE @amostra_metodo NVARCHAR(80); DECLARE @algoritmo_versao NVARCHAR(80);
                SELECT @modelo_id=modelo_id,@status=status,@registros=registros_lidos,@pessoas=pessoas_unicas,@amostra_metodo=amostra_metodo,@algoritmo_versao=algoritmo_versao FROM identidade.modelo_linkage WITH (UPDLOCK,HOLDLOCK) WHERE versao=@versao;
                IF @modelo_id IS NULL THROW 51001, 'Modelo de linkage não encontrado.', 1;
                IF @status<>'RASCUNHO' THROW 51002, 'Somente modelo RASCUNHO pode ser validado.', 1;
                IF @pessoas IS NULL OR @pessoas<=0 OR @registros IS NULL OR @registros<>@pessoas THROW 51003, 'População de referência inválida.', 1;
                IF NOT EXISTS(SELECT 1 FROM identidade.estatistica_linkage WHERE modelo_id=@modelo_id) THROW 51004, 'Modelo sem estatísticas populacionais persistidas.', 1;
                IF EXISTS (SELECT req.nome FROM (VALUES
                    ('M_NOME_EXACT'),('M_NOME_HIGH'),('M_NOME_MEDIUM'),('M_NOME_LOW'),('U_NOME_EXACT'),('U_NOME_HIGH'),('U_NOME_MEDIUM'),('U_NOME_LOW'),
                    ('M_NOME_MAE_EXACT'),('M_NOME_MAE_HIGH'),('M_NOME_MAE_MEDIUM'),('M_NOME_MAE_LOW'),('U_NOME_MAE_EXACT'),('U_NOME_MAE_HIGH'),('U_NOME_MAE_MEDIUM'),('U_NOME_MAE_LOW'),
                    ('PRIOR_MATCH_PROBABILITY'),('PRIOR_BLOCK_MIN'),('PRIOR_BLOCK_MAX'),('T_LINKAGE'),('CONFLICT_MARGIN'),('M_SAMPLE_SIZE'),('U_SAMPLE_SIZE'),('POPULATION_SIZE'),('DISTINCT_BIRTH_DATE')) req(nome)
                    WHERE NOT EXISTS (SELECT 1 FROM identidade.parametro_linkage p WHERE p.modelo_id=@modelo_id AND p.nome=req.nome))
                    THROW 51010, 'Modelo probabilístico incompleto: parâmetros obrigatórios ausentes.', 1;
                IF @algoritmo_versao=@semantic_algorithm_version
                BEGIN
                    IF NOT EXISTS(SELECT 1 FROM identidade.parametro_linkage WHERE modelo_id=@modelo_id AND nome='SCORING_BIRTH_SEMANTIC_EVIDENCE_V5' AND valor>=1) THROW 51015, 'Modelo V5 sem scoring semântico de nascimento habilitado.', 1;
                    IF EXISTS (SELECT req.nome FROM (VALUES
                        ('M_NASCIMENTO_SEMANTICO_EXACT'),('M_NASCIMENTO_SEMANTICO_DAY_MONTH_SWAP'),('M_NASCIMENTO_SEMANTICO_CENTURY_SHIFT'),('M_NASCIMENTO_SEMANTICO_ONE_DIGIT_ERROR'),('M_NASCIMENTO_SEMANTICO_TWO_DIGIT_ERROR'),('M_NASCIMENTO_SEMANTICO_PARTIAL_COMPONENT_AGREEMENT'),('M_NASCIMENTO_SEMANTICO_OTHER_DISAGREEMENT'),
                        ('U_NASCIMENTO_SEMANTICO_EXACT'),('U_NASCIMENTO_SEMANTICO_DAY_MONTH_SWAP'),('U_NASCIMENTO_SEMANTICO_CENTURY_SHIFT'),('U_NASCIMENTO_SEMANTICO_ONE_DIGIT_ERROR'),('U_NASCIMENTO_SEMANTICO_TWO_DIGIT_ERROR'),('U_NASCIMENTO_SEMANTICO_PARTIAL_COMPONENT_AGREEMENT'),('U_NASCIMENTO_SEMANTICO_OTHER_DISAGREEMENT')) req(nome)
                        WHERE NOT EXISTS (SELECT 1 FROM identidade.parametro_linkage p WHERE p.modelo_id=@modelo_id AND p.nome=req.nome)) THROW 51016, 'Modelo V5 sem distribuição semântica de nascimento completa.', 1;
                END
                IF EXISTS (SELECT 1 FROM identidade.parametro_linkage WHERE modelo_id=@modelo_id AND (((((nome LIKE 'M[_]%' OR nome LIKE 'U[_]%') AND nome NOT IN('M_SAMPLE_SIZE','U_SAMPLE_SIZE')) OR nome IN('PRIOR_MATCH_PROBABILITY','PRIOR_BLOCK_MIN','PRIOR_BLOCK_MAX')) AND (valor<=0 OR valor>=1)) OR (nome='T_LINKAGE' AND (valor<=0 OR valor>1)) OR (nome='CONFLICT_MARGIN' AND (valor<=0 OR valor>=1)))) THROW 51011, 'Parâmetros probabilísticos fora do domínio esperado.', 1;
                IF (SELECT valor FROM identidade.parametro_linkage WHERE modelo_id=@modelo_id AND nome='PRIOR_BLOCK_MIN') > (SELECT valor FROM identidade.parametro_linkage WHERE modelo_id=@modelo_id AND nome='PRIOR_BLOCK_MAX') THROW 51012, 'PRIOR_BLOCK_MIN não pode ser maior que PRIOR_BLOCK_MAX.', 1;
                IF @amostra_metodo=@sqlserver_amostra_metodo AND NOT EXISTS(SELECT 1 FROM identidade.linkage_ruleset r WHERE r.modelo_id=@modelo_id AND EXISTS(SELECT 1 FROM identidade.linkage_ruleset_passe rp WHERE rp.ruleset_id=r.ruleset_id) AND NOT EXISTS(SELECT 1 FROM identidade.linkage_ruleset_passe rp WHERE rp.ruleset_id=r.ruleset_id AND NOT EXISTS(SELECT 1 FROM identidade.linkage_ruleset_passe_campo rc WHERE rc.ruleset_id=rp.ruleset_id AND rc.passe_ordem=rp.passe_ordem))) THROW 51013, 'Modelo SQL Server sem ruleset dinâmico completo.', 1;
                UPDATE identidade.modelo_linkage SET status='VALIDADO' WHERE modelo_id=@modelo_id;
                """, connection, transaction);
            command.Parameters.Add("@versao", SqlDbType.Int).Value = version;
            command.Parameters.Add("@sqlserver_amostra_metodo", SqlDbType.NVarChar, 80).Value = SqlServerSampleMethod;
            command.Parameters.Add("@semantic_algorithm_version", SqlDbType.NVarChar, 80).Value = CurrentAlgorithmVersion;
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation("Modelo de linkage v{Version} validado explicitamente.", version);
        }
        catch { await transaction.RollbackAsync(cancellationToken); throw; }
    }

    private async Task ActivateValidatedAsync(int version, CancellationToken cancellationToken)
    {
        await using var connection = await operationalSql.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var command = new SqlCommand(
                """
                DECLARE @lock_result INT; EXEC @lock_result=sys.sp_getapplock @Resource='Jornada.Linkage.Parameters.Activation',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=60000;
                IF @lock_result<0 THROW 51006, 'Não foi possível obter lock para ativação do modelo.', 1;
                DECLARE @modelo_id UNIQUEIDENTIFIER; DECLARE @status NVARCHAR(20); DECLARE @amostra_metodo NVARCHAR(80); DECLARE @algoritmo_versao NVARCHAR(80);
                SELECT @modelo_id=modelo_id,@status=status,@amostra_metodo=amostra_metodo,@algoritmo_versao=algoritmo_versao FROM identidade.modelo_linkage WITH (UPDLOCK,HOLDLOCK) WHERE versao=@versao;
                IF @modelo_id IS NULL THROW 51007, 'Modelo de linkage não encontrado.', 1; IF @status='ATIVO' RETURN; IF @status<>'VALIDADO' THROW 51008, 'Somente modelo VALIDADO pode ser ativado.', 1;
                IF @algoritmo_versao=@semantic_algorithm_version
                BEGIN
                    IF NOT EXISTS(SELECT 1 FROM identidade.parametro_linkage WHERE modelo_id=@modelo_id AND nome='SCORING_BIRTH_SEMANTIC_EVIDENCE_V5' AND valor>=1) THROW 51017, 'Modelo V5 validado sem scoring semântico de nascimento habilitado.', 1;
                    IF EXISTS (SELECT req.nome FROM (VALUES
                        ('M_NASCIMENTO_SEMANTICO_EXACT'),('M_NASCIMENTO_SEMANTICO_DAY_MONTH_SWAP'),('M_NASCIMENTO_SEMANTICO_CENTURY_SHIFT'),('M_NASCIMENTO_SEMANTICO_ONE_DIGIT_ERROR'),('M_NASCIMENTO_SEMANTICO_TWO_DIGIT_ERROR'),('M_NASCIMENTO_SEMANTICO_PARTIAL_COMPONENT_AGREEMENT'),('M_NASCIMENTO_SEMANTICO_OTHER_DISAGREEMENT'),
                        ('U_NASCIMENTO_SEMANTICO_EXACT'),('U_NASCIMENTO_SEMANTICO_DAY_MONTH_SWAP'),('U_NASCIMENTO_SEMANTICO_CENTURY_SHIFT'),('U_NASCIMENTO_SEMANTICO_ONE_DIGIT_ERROR'),('U_NASCIMENTO_SEMANTICO_TWO_DIGIT_ERROR'),('U_NASCIMENTO_SEMANTICO_PARTIAL_COMPONENT_AGREEMENT'),('U_NASCIMENTO_SEMANTICO_OTHER_DISAGREEMENT')) req(nome)
                        WHERE NOT EXISTS (SELECT 1 FROM identidade.parametro_linkage p WHERE p.modelo_id=@modelo_id AND p.nome=req.nome)) THROW 51018, 'Modelo V5 validado sem distribuição semântica de nascimento completa.', 1;
                END
                IF @amostra_metodo=@sqlserver_amostra_metodo AND NOT EXISTS(SELECT 1 FROM identidade.linkage_ruleset r WHERE r.modelo_id=@modelo_id AND EXISTS(SELECT 1 FROM identidade.linkage_ruleset_passe rp WHERE rp.ruleset_id=r.ruleset_id) AND NOT EXISTS(SELECT 1 FROM identidade.linkage_ruleset_passe rp WHERE rp.ruleset_id=r.ruleset_id AND NOT EXISTS(SELECT 1 FROM identidade.linkage_ruleset_passe_campo rc WHERE rc.ruleset_id=rp.ruleset_id AND rc.passe_ordem=rp.passe_ordem))) THROW 51014, 'Modelo SQL Server validado sem ruleset dinâmico completo.', 1;
                UPDATE identidade.modelo_linkage SET status='INATIVO' WHERE status='ATIVO' AND modelo_id<>@modelo_id;
                UPDATE identidade.modelo_linkage SET status='ATIVO',ativado_em=SYSDATETIMEOFFSET() WHERE modelo_id=@modelo_id;
                """, connection, transaction);
            command.Parameters.Add("@versao", SqlDbType.Int).Value = version;
            command.Parameters.Add("@sqlserver_amostra_metodo", SqlDbType.NVarChar, 80).Value = SqlServerSampleMethod;
            command.Parameters.Add("@semantic_algorithm_version", SqlDbType.NVarChar, 80).Value = CurrentAlgorithmVersion;
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation("Modelo de linkage v{Version} ativado explicitamente; modelo anterior foi preservado como INATIVO.", version);
        }
        catch { await transaction.RollbackAsync(cancellationToken); throw; }
    }

    private sealed record PopulationStatistics(long PopulationSize, long WithCpf, long DistinctFullNames, long DistinctMotherNames, long DistinctBirthDates, DateTimeOffset? MaxGoldUpdatedAt);
}
