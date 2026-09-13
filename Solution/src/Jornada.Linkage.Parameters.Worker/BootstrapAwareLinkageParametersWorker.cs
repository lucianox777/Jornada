using System.Data;
using System.Globalization;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Porta de entrada bootstrap-aware para o worker SQL Server.
/// Abaixo do mínimo de pares CPF independentes cria um RASCUNHO PRIOR_BOOTSTRAP;
/// ao atingir o mínimo delega integralmente ao LinkageParametersWorker empírico existente.
/// VALIDATE/ACTIVATE também são delegados, e o banco bloqueia ativação de bootstrap.
/// </summary>
public sealed class BootstrapAwareLinkageParametersWorker(
    ILogger<BootstrapAwareLinkageParametersWorker> logger,
    IConfiguration configuration,
    IOperationalSqlAdapter operationalSql,
    LinkageParametersWorker empiricalWorker,
    IHostApplicationLifetime applicationLifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var operation = configuration.GetValue("LinkageParameters:Operation", "GENERATE_DRAFT")!
            .Trim().ToUpperInvariant();

        if (operation != "GENERATE_DRAFT")
        {
            await DelegateToEmpiricalWorkerAsync(stoppingToken);
            return;
        }

        var minimum = Math.Max(100, configuration.GetValue("LinkageParameters:MinimumIndependentMatchedPairs", 5_000));
        await using var connection = await operationalSql.OpenAsync(stoppingToken);
        var available = await CountIndependentCpfPairsAsync(connection, stoppingToken);

        if (available >= minimum)
        {
            logger.LogInformation(
                "Cold start encerrado: pares CPF independentes={Available} >= mínimo={Minimum}. Delegando ao estimador empírico.",
                available, minimum);
            await DelegateToEmpiricalWorkerAsync(stoppingToken);
            return;
        }

        logger.LogWarning(
            "Cold start: pares CPF independentes={Available} < mínimo={Minimum}. Gerando PRIOR_BOOTSTRAP não ativável.",
            available, minimum);

        await GenerateBootstrapDraftAsync(connection, available, minimum, stoppingToken);
        applicationLifetime.StopApplication();
    }

    private async Task DelegateToEmpiricalWorkerAsync(CancellationToken ct)
    {
        await empiricalWorker.StartAsync(ct);
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // encerramento normal do host, inclusive quando o worker delegado chama StopApplication.
        }
        finally
        {
            await empiricalWorker.StopAsync(CancellationToken.None);
        }
    }

    private async Task GenerateBootstrapDraftAsync(
        SqlConnection connection,
        long independentPairsAvailable,
        int minimumIndependentPairs,
        CancellationToken ct)
    {
        var threshold = Math.Clamp(configuration.GetValue("LinkageParameters:TLinkage", 0.95m), 0.5m, 0.999999m);
        var conflictMargin = Math.Clamp(configuration.GetValue("LinkageParameters:ConflictMargin", 0.03m), 0.0001m, 0.5m);
        var algorithmVersion = configuration.GetValue("LinkageParameters:AlgorithmVersion", "FELLEGI_SUNTER_ANCHORED_V1")!;
        var normalizationVersion = configuration.GetValue("LinkageParameters:NormalizationVersion", "IDENTITY_NORMALIZATION_V1")!;

        var (population, distinctBirthDates) = await ReadPopulationAsync(connection, ct);
        if (population <= 0)
            throw new InvalidOperationException("Bootstrap exige ao menos uma Pessoa na Gold.");

        var (collision, publishedMass) = await ReadActiveIbgeCollisionAsync(connection, ct);
        var parameters = BootstrapLinkageParameterEstimator.EstimateFromIbgeCollision(
            collision,
            publishedMass,
            population,
            distinctBirthDates,
            threshold,
            conflictMargin);

        var modelId = Guid.NewGuid();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var version = await InsertGeneratingModelAsync(
                connection, transaction, modelId, algorithmVersion, normalizationVersion,
                independentPairsAvailable, ct);

            foreach (var pair in parameters.Parameters.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                await using var insert = new SqlCommand(
                    "INSERT identidade.parametro_linkage(modelo_id,nome,valor) VALUES(@id,@nome,@valor);",
                    connection, transaction);
                insert.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = modelId;
                insert.Parameters.Add("@nome", SqlDbType.NVarChar, 100).Value = pair.Key;
                var value = insert.Parameters.Add("@valor", SqlDbType.Decimal);
                value.Precision = 30;
                value.Scale = 12;
                value.Value = pair.Value;
                await insert.ExecuteNonQueryAsync(ct);
            }

            await using (var finish = new SqlCommand(
                """
                UPDATE identidade.modelo_linkage
                   SET status='RASCUNHO',
                       estagio_parametro='PRIOR_BOOTSTRAP',
                       m_origem='PRIOR_INSTITUCIONAL',
                       u_nome_origem='IBGE_CENSO2022_COLISAO_EXATA_MASSA_PUBLICADA',
                       promocao_automatica_permitida=0,
                       snapshot_capturado_em=SYSDATETIMEOFFSET(),
                       amostra_m_tamanho=@m,
                       amostra_u_tamanho=NULL,
                       registros_lidos=@population,
                       pessoas_unicas=@population,
                       falha_resumo=NULL
                 WHERE modelo_id=@id;
                """, connection, transaction))
            {
                finish.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = modelId;
                finish.Parameters.Add("@m", SqlDbType.Int).Value = checked((int)Math.Min(independentPairsAvailable, int.MaxValue));
                finish.Parameters.Add("@population", SqlDbType.BigInt).Value = population;
                await finish.ExecuteNonQueryAsync(ct);
            }

            await transaction.CommitAsync(ct);
            logger.LogWarning(
                "Modelo bootstrap v{Version} criado em RASCUNHO. m=PRIOR_INSTITUCIONAL; u_nome_exact=IBGE; paresCPF={Pairs}/{Minimum}; ativação bloqueada.",
                version, independentPairsAvailable, minimumIndependentPairs);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    private static async Task<int> InsertGeneratingModelAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid modelId,
        string algorithmVersion,
        string normalizationVersion,
        long independentPairsAvailable,
        CancellationToken ct)
    {
        await using var command = new SqlCommand(
            """
            DECLARE @lock_result INT;
            EXEC @lock_result=sys.sp_getapplock
                @Resource='Jornada.Linkage.Parameters.Version',
                @LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=60000;
            IF @lock_result<0 THROW 51672,'Não foi possível obter lock de versão para bootstrap.',1;
            DECLARE @versao INT=(SELECT ISNULL(MAX(versao),0)+1 FROM identidade.modelo_linkage WITH(UPDLOCK,HOLDLOCK));
            INSERT identidade.modelo_linkage(
                modelo_id,versao,status,algoritmo_versao,normalizacao_versao,
                deduplicacao_metodo,base_referencia,snapshot_referencia,
                registros_lidos,pessoas_unicas,gerado_em,ativado_em,
                snapshot_capturado_em,amostra_metodo,amostra_pool_tamanho,
                amostra_m_tamanho,amostra_u_tamanho,falha_resumo,
                estagio_parametro,m_origem,u_nome_origem,promocao_automatica_permitida)
            VALUES(
                @id,@versao,'GERANDO',@algoritmo,@normalizacao,
                'GOLD_PESSOA_UUID_PK','gold.pessoa',NULL,
                NULL,NULL,SYSDATETIMEOFFSET(),NULL,
                NULL,'M_PRIOR_U_IBGE_COLD_START',NULL,@m,NULL,NULL,
                'PRIOR_BOOTSTRAP','PRIOR_INSTITUCIONAL','IBGE_CENSO2022_COLISAO_EXATA_MASSA_PUBLICADA',0);
            SELECT @versao;
            """, connection, transaction);
        command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = modelId;
        command.Parameters.Add("@algoritmo", SqlDbType.NVarChar, 80).Value = algorithmVersion;
        command.Parameters.Add("@normalizacao", SqlDbType.NVarChar, 80).Value = normalizationVersion;
        command.Parameters.Add("@m", SqlDbType.Int).Value = checked((int)Math.Min(independentPairsAvailable, int.MaxValue));
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
    }

    private static async Task<long> CountIndependentCpfPairsAsync(SqlConnection connection, CancellationToken ct)
    {
        await using var command = new SqlCommand(
            """
            WITH fontes AS (
                SELECT vf.pessoa_uuid,COUNT(DISTINCT po.gestor_id) AS qtd_gestores
                FROM identidade.vinculo_fonte vf
                JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=vf.pessoa_observacao_id
                WHERE vf.ativo=1 AND vf.status='RESOLVIDO'
                  AND vf.metodo_resolucao='CPF_DETERMINISTICO'
                  AND po.cpf IS NOT NULL
                GROUP BY vf.pessoa_uuid
            )
            SELECT COUNT_BIG(*) FROM fontes WHERE qtd_gestores>=2;
            """, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
    }

    private static async Task<(long Population, long DistinctBirthDates)> ReadPopulationAsync(SqlConnection connection, CancellationToken ct)
    {
        await using var command = new SqlCommand(
            "SELECT COUNT_BIG(*),APPROX_COUNT_DISTINCT(data_nascimento) FROM gold.pessoa;", connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return (0, 0);
        return (
            reader.GetInt64(0),
            reader.IsDBNull(1) ? 0 : Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture));
    }

    private static async Task<(decimal Collision, long PublishedMass)> ReadActiveIbgeCollisionAsync(SqlConnection connection, CancellationToken ct)
    {
        await using var command = new SqlCommand(
            "SELECT u_nome_exact_colisao,massa_publicada FROM ref.v_frequencia_nome_ibge_colisao_ativa;", connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new InvalidOperationException("Bootstrap exige versão ATIVA da referência IBGE de nomes.");
        var result = (reader.GetDecimal(0), reader.GetInt64(1));
        if (await reader.ReadAsync(ct))
            throw new InvalidOperationException("Referência IBGE ativa retornou mais de uma linha nacional de colisão.");
        return result;
    }
}
