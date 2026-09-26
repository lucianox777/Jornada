using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;

namespace Jornada.Api;

internal sealed record RuntimeComponentStatus(
    string NodeId,
    string Component,
    string MachineName,
    string Status,
    bool Online,
    long HeartbeatAgeSeconds,
    DateTimeOffset StartedAt,
    DateTimeOffset HeartbeatAt,
    DateTimeOffset? StoppedAt,
    string? Version,
    string? ConfigurationBundleVersion,
    string? SolutionSchemaExpected);

internal sealed record QueueStatus(string Status, long Count);

internal sealed record ActiveProcessingStatus(
    Guid LotId,
    Guid DeliveryId,
    int LotSequence,
    int LotTotal,
    int People,
    int Records,
    string Status,
    string? LeaseOwner,
    DateTimeOffset? LeaseAcquiredAt,
    DateTimeOffset? HeartbeatAt);

internal sealed record RecentDeliveryStatus(
    Guid DeliveryId,
    string Status,
    string Manager,
    string SourceSystem,
    string FileName,
    DateTimeOffset ReceivedAt,
    DateTimeOffset UpdatedAt);

internal sealed record BronzeMaintenanceStatus(
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    int ObjectsExamined,
    int OrphansRemoved,
    int TemporariesRemoved,
    int LocksNotAcquired,
    int StorageFailures);

internal sealed record LinkageRunStatus(
    Guid RunId,
    Guid ModelId,
    string RunType,
    string Status,
    int ModelVersion,
    long Eligible,
    long Evaluated,
    long Resolved,
    long NotResolved,
    long Conflicts,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt);

internal sealed record LinkageModelGovernanceStatus(
    string Status,
    int ActiveModelCount,
    Guid? ModelId,
    int? ModelVersion,
    string? AlgorithmVersion,
    string? NormalizationVersion,
    DateTimeOffset? GeneratedAt,
    DateTimeOffset? ActivatedAt,
    string? FrequencyReferenceCode,
    string? FrequencyReferenceSha256,
    string NominalUNameSource,
    string NominalUMotherSource,
    string StatisticalValidation);

internal sealed record LinkageModelTransitionStatus(
    long EventId,
    Guid ModelId,
    int ModelVersion,
    string? PreviousStatus,
    string NewStatus,
    string Operation,
    string? Reason,
    string ExecutorApplication,
    DateTimeOffset OccurredAt);

internal sealed record LinkageConferenceGovernanceStatus(
    string Status,
    Guid? EvidenceId,
    int? ModelVersion,
    string? MethodVersion,
    string? Scope,
    string? ToleranceVersion,
    int? CandidatesEvaluated,
    bool? SameFinalDecision,
    bool? SameTop1,
    bool? SnapshotCurrent,
    DateTimeOffset? OccurredAt,
    string StatisticalValidation,
    string RoundTripMethod,
    string RoundTripStatus);

// Valores agregados por modelo; nao expoe scores, thresholds ou pessoa.
internal sealed record LinkageCalibrationSummary(
    string Status,
    Guid? ModelId,
    int? ModelVersion,
    string? ModelStatus,
    DateTimeOffset? GeneratedAt,
    string? FixedReferenceCode,
    int? ValidationFpBasisPoints,
    int? TestFpBasisPoints,
    decimal? ValidationFpLimit,
    decimal? TestFpLimit,
    decimal? ValidationPositive,
    decimal? ValidationNegative,
    decimal? TestPositive,
    decimal? TestNegative,
    decimal? ValidationFalsePositive,
    decimal? TestFalsePositive,
    decimal? ValidationFalseNegative,
    decimal? TestFalseNegative,
    decimal? ValidationInconclusive,
    decimal? TestInconclusive,
    decimal? TestWrongPersonFp,
    decimal? TestLeaveTruthOutFp,
    decimal? MatchedPairSample,
    decimal? CandidateUnionUSample,
    decimal? ValidationEffectiveCapBp,
    decimal? TestEffectiveCapBp);

internal sealed record IbgeReferenceReadiness(
    string Status,
    int ActiveVersions,
    string? ReferenceCode,
    string? ContentSha256);

internal sealed record LinkageBlockingPassSupportStatus(
    int PassOrder,
    string PassId,
    long SampleSize,
    long MotherNamePresentSupport,
    long MinimumRequiredPerPass,
    bool NameSufficient,
    bool MotherNameSufficient);

internal sealed record ConfigurationBundleHealthStatus(
    string Status,
    string? ExpectedBundleVersion,
    string? ExpectedSolutionSchema,
    string? DatabaseSolutionSchema,
    IReadOnlyList<string> OnlineBundleVersions,
    IReadOnlyList<string> OnlineExpectedSchemas,
    string Detail);

internal sealed record OperationalMonitorSnapshot(
    DateTimeOffset GeneratedAt,
    string OverallStatus,
    ConfigurationBundleHealthStatus ConfigurationHealth,
    IReadOnlyList<RuntimeComponentStatus> Components,
    IReadOnlyList<QueueStatus> Queue,
    IReadOnlyList<ActiveProcessingStatus> Processing,
    IReadOnlyList<RecentDeliveryStatus> RecentDeliveries,
    BronzeMaintenanceStatus? BronzeMaintenance,
    LinkageModelGovernanceStatus LinkageModelGovernance,
    LinkageConferenceGovernanceStatus LinkageConferenceGovernance,
    IReadOnlyList<LinkageBlockingPassSupportStatus> LinkageBlockingPassSupport,
    IReadOnlyList<LinkageModelTransitionStatus> LinkageModelTransitions,
    IReadOnlyList<LinkageRunStatus> LinkageRuns,
    LinkageCalibrationSummary LinkageCalibration,
    IbgeReferenceReadiness IbgeReference);

internal sealed class OperationalMonitorService(IOperationalSqlAdapter connections)
{
    public async Task<OperationalMonitorSnapshot> GetAsync(CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        await using var command = new SqlCommand("""
            DECLARE @agora DATETIMEOFFSET(7)=SYSUTCDATETIME();

            SELECT node_id,componente,machine_name,status,
                   CASE WHEN status=N'RUNNING' AND heartbeat_em>=DATEADD(SECOND,-35,@agora) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END online,
                   CASE WHEN heartbeat_em>@agora THEN 0 ELSE DATEDIFF_BIG(SECOND,heartbeat_em,@agora) END heartbeat_age_seconds,
                   iniciado_em,heartbeat_em,encerrado_em,versao,configuration_bundle_version,solution_schema_expected
            FROM controle.runtime_componente
            ORDER BY node_id,componente;

            SELECT status,COUNT_BIG(*) quantidade
            FROM ingestao.lote
            GROUP BY status
            ORDER BY status;

            -- O heartbeat vivo fica separado do Lote para nao disputar locks com
            -- a transacao Serializable do Processor. O Lote guarda apenas o snapshot
            -- inicial e serve de fallback para leases antigos sem linha correspondente.
            SELECT TOP(20) l.lote_id,l.entrega_id,l.lote_seq,l.lote_total,l.qtd_pessoas,l.qtd_registros,
                   l.status,l.lease_owner,l.lease_adquirido_em,
                   COALESCE(h.heartbeat_em,l.heartbeat_em) AS heartbeat_em
            FROM ingestao.lote l
            LEFT JOIN ingestao.lote_heartbeat h
              ON h.lote_id=l.lote_id AND h.lease_id=l.lease_id AND h.lease_owner=l.lease_owner
            WHERE l.status IN(N'VALIDANDO',N'PROCESSANDO')
            ORDER BY l.lease_adquirido_em,l.lote_id;

            SELECT TOP(20) e.entrega_id,e.status,g.codigo,so.codigo,b.nome_arquivo,e.recebido_em,e.ultima_atualizacao
            FROM ingestao.entrega e
            JOIN ref.gestor g ON g.gestor_id=e.gestor_id
            JOIN ref.sistema_origem so ON so.sistema_origem_id=e.sistema_origem_id
            JOIN bronze.entrega_arquivo b ON b.entrega_id=e.entrega_id
            ORDER BY e.recebido_em DESC,e.entrega_id DESC;

            SELECT TOP(1) iniciado_em,finalizado_em,objetos_examinados,orfaos_removidos,temporarios_removidos,locks_nao_adquiridos,falhas_storage
            FROM controle.bronze_manutencao_ciclo
            ORDER BY finalizado_em DESC,ciclo_id DESC;

            SELECT TOP(5) linkage_run_id,modelo_id,tipo_run,status,modelo_versao,registros_elegiveis,avaliados,resolvidos,nao_resolvidos,conflitos,iniciado_em,finalizado_em
            FROM identidade.linkage_run
            ORDER BY iniciado_em DESC,linkage_run_id DESC;

            DECLARE @active_model_count INT=(SELECT COUNT(*) FROM identidade.modelo_linkage WHERE status=N'ATIVO');
            DECLARE @active_model_id UNIQUEIDENTIFIER=(
                SELECT TOP(1) modelo_id
                FROM identidade.modelo_linkage
                WHERE status=N'ATIVO'
                ORDER BY versao DESC);
            DECLARE @active_model_snapshot_sha256 BINARY(32)=NULL;
            IF @active_model_id IS NOT NULL
                EXEC auditoria.sp_calcular_fingerprint_modelo_linkage
                    @modelo_id=@active_model_id,
                    @fingerprint=@active_model_snapshot_sha256 OUTPUT;
            SELECT
                @active_model_count active_model_count,
                m.modelo_id,m.versao,m.algoritmo_versao,m.normalizacao_versao,m.gerado_em,m.ativado_em,
                v.codigo,
                CASE WHEN v.conteudo_sha256 IS NULL THEN NULL ELSE CONVERT(VARCHAR(64),v.conteudo_sha256,2) END referencia_sha256,
                CASE
                  WHEN p.nome_condicionado>=1 THEN N'BLOCKING_CONDITIONED'
                  WHEN p.nome_ibge>=1 THEN N'IBGE_BOOTSTRAP'
                  ELSE N'NAO_DECLARADO'
                END nome_u_source,
                CASE
                  WHEN p.mae_condicionado>=1 THEN N'BLOCKING_CONDITIONED'
                  WHEN p.mae_ibge>=1 THEN N'IBGE_BOOTSTRAP'
                  ELSE N'NAO_DECLARADO'
                END mae_u_source
            FROM (SELECT @active_model_count active_model_count) c
            LEFT JOIN (
                SELECT TOP(1) *
                FROM identidade.modelo_linkage
                WHERE modelo_id=@active_model_id
            ) m ON 1=1
            LEFT JOIN ref.frequencia_nome_versao v
              ON v.frequencia_nome_versao_id=m.frequencia_nome_versao_id
            OUTER APPLY (
                SELECT
                  MAX(CASE WHEN nome=N'NOMINAL_U_NOME_SOURCE_BLOCKING_CONDITIONED' THEN valor END) nome_condicionado,
                  MAX(CASE WHEN nome=N'IBGE_MC_NOMINAL_U_APPLIED_NOME' THEN valor END) nome_ibge,
                  MAX(CASE WHEN nome=N'NOMINAL_U_NOME_MAE_SOURCE_BLOCKING_CONDITIONED' THEN valor END) mae_condicionado,
                  MAX(CASE WHEN nome=N'IBGE_MC_NOMINAL_U_APPLIED_NOME_MAE' THEN valor END) mae_ibge
                FROM identidade.parametro_linkage p0
                WHERE p0.modelo_id=m.modelo_id
            ) p;

            SELECT TOP(10)
                modelo_linkage_estado_evento_id,modelo_id,modelo_versao,status_anterior,status_novo,
                operacao_codigo,motivo,executor_aplicacao,ocorrido_em
            FROM auditoria.modelo_linkage_estado_evento
            ORDER BY modelo_linkage_estado_evento_id DESC;

            SELECT TOP(1)
                evidencia_id,modelo_versao,metodo_versao,escopo,tolerancia_versao,status,
                candidatos_avaliados,mesma_decisao_final,mesmo_top1,
                CASE WHEN modelo_snapshot_sha256=@active_model_snapshot_sha256 THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END snapshot_current,
                validacao_estatistica,ocorrido_em
            FROM auditoria.linkage_conferencia_evidencia
            WHERE modelo_id=@active_model_id
            ORDER BY linkage_conferencia_evidencia_id DESC;

            ;WITH active_ruleset AS (
                SELECT TOP(1) ruleset_id
                FROM identidade.linkage_ruleset
                WHERE modelo_id=@active_model_id
                ORDER BY ruleset_id
            ),
            min_support AS (
                SELECT CONVERT(BIGINT,COALESCE(MAX(CASE
                    WHEN nome=N'NOMINAL_U_MIN_CONDITIONED_PAIRS_PER_PASS' THEN valor END),0)) minimo
                FROM identidade.parametro_linkage
                WHERE modelo_id=@active_model_id
            )
            SELECT
                rp.passe_ordem+1 passe_ordem_exibida,
                rp.passe_id,
                CONVERT(BIGINT,COALESCE(MAX(CASE WHEN p.nome=
                    N'BLOCKING_PASS_U_'+RIGHT(N'00'+CONVERT(NVARCHAR(10),rp.passe_ordem+1),2)+N'_SAMPLE_SIZE'
                    THEN p.valor END),0)) sample_size,
                CONVERT(BIGINT,COALESCE(SUM(CASE WHEN p.nome IN(
                    N'BLOCKING_PASS_U_'+RIGHT(N'00'+CONVERT(NVARCHAR(10),rp.passe_ordem+1),2)+N'_NOME_MAE_EXACT',
                    N'BLOCKING_PASS_U_'+RIGHT(N'00'+CONVERT(NVARCHAR(10),rp.passe_ordem+1),2)+N'_NOME_MAE_HIGH',
                    N'BLOCKING_PASS_U_'+RIGHT(N'00'+CONVERT(NVARCHAR(10),rp.passe_ordem+1),2)+N'_NOME_MAE_MEDIUM',
                    N'BLOCKING_PASS_U_'+RIGHT(N'00'+CONVERT(NVARCHAR(10),rp.passe_ordem+1),2)+N'_NOME_MAE_LOW')
                    THEN p.valor ELSE 0 END),0)) mother_present_support,
                ms.minimo,
                CAST(CASE WHEN COALESCE(MAX(CASE WHEN p.nome=
                    N'BLOCKING_PASS_U_'+RIGHT(N'00'+CONVERT(NVARCHAR(10),rp.passe_ordem+1),2)+N'_SAMPLE_SIZE'
                    THEN p.valor END),0)>=ms.minimo AND ms.minimo>0 THEN 1 ELSE 0 END AS BIT) name_sufficient,
                CAST(CASE WHEN COALESCE(SUM(CASE WHEN p.nome IN(
                    N'BLOCKING_PASS_U_'+RIGHT(N'00'+CONVERT(NVARCHAR(10),rp.passe_ordem+1),2)+N'_NOME_MAE_EXACT',
                    N'BLOCKING_PASS_U_'+RIGHT(N'00'+CONVERT(NVARCHAR(10),rp.passe_ordem+1),2)+N'_NOME_MAE_HIGH',
                    N'BLOCKING_PASS_U_'+RIGHT(N'00'+CONVERT(NVARCHAR(10),rp.passe_ordem+1),2)+N'_NOME_MAE_MEDIUM',
                    N'BLOCKING_PASS_U_'+RIGHT(N'00'+CONVERT(NVARCHAR(10),rp.passe_ordem+1),2)+N'_NOME_MAE_LOW')
                    THEN p.valor ELSE 0 END),0)>=ms.minimo AND ms.minimo>0 THEN 1 ELSE 0 END AS BIT) mother_sufficient
            FROM active_ruleset ar
            JOIN identidade.linkage_ruleset_passe rp ON rp.ruleset_id=ar.ruleset_id
            CROSS JOIN min_support ms
            LEFT JOIN identidade.parametro_linkage p
              ON p.modelo_id=@active_model_id
             AND LEFT(p.nome,LEN(N'BLOCKING_PASS_U_'+RIGHT(N'00'+CONVERT(NVARCHAR(10),rp.passe_ordem+1),2)+N'_'))=
                 N'BLOCKING_PASS_U_'+RIGHT(N'00'+CONVERT(NVARCHAR(10),rp.passe_ordem+1),2)+N'_'
            GROUP BY rp.passe_ordem,rp.passe_id,ms.minimo
            ORDER BY rp.passe_ordem;

            SELECT CONVERT(NVARCHAR(32),(
                SELECT value
                FROM sys.extended_properties
                WHERE class=0 AND name=N'Jornada.SolutionSchema'));

            -- Referencia pronta independe de modelo ATIVO: necessaria antes da primeira entrega.
            SELECT v.codigo,
                   CASE WHEN v.conteudo_sha256 IS NULL THEN NULL
                        ELSE CONVERT(VARCHAR(64),v.conteudo_sha256,2) END conteudo_sha256,
                   CASE WHEN EXISTS(SELECT 1 FROM ref.frequencia_nome f
                                    WHERE f.frequencia_nome_versao_id=v.frequencia_nome_versao_id)
                        THEN 1 ELSE 0 END tem_linhas
            FROM ref.frequencia_nome_versao v
            WHERE v.status=N'ATIVA'
            ORDER BY v.frequencia_nome_versao_id DESC;

            -- Ultimo modelo gerado, inclusive RASCUNHO/FALHOU, sem confundi-lo com ATIVO.
            SELECT m.modelo_id,m.versao,m.status,m.gerado_em,refv.codigo,
                   p.validation_bp,p.test_bp,p.validation_limit,p.test_limit,
                   p.validation_positive,p.validation_negative,p.test_positive,p.test_negative,
                   p.validation_fp,p.test_fp,p.validation_fn,p.test_fn,
                   p.validation_inconclusive,p.test_inconclusive,
                   p.test_wrong_person_fp,p.test_leave_truth_out_fp,
                   p.m_sample,p.u_sample,p.validation_effective_cap_bp,p.test_effective_cap_bp
            FROM (
                SELECT TOP(1) modelo_id,versao,status,gerado_em,frequencia_nome_versao_id
                FROM identidade.modelo_linkage ORDER BY versao DESC
            ) m
            LEFT JOIN ref.frequencia_nome_versao refv
                   ON refv.frequencia_nome_versao_id=m.frequencia_nome_versao_id
            OUTER APPLY (
                SELECT
                    MAX(CASE WHEN nome=N'FS_DECISION_CALIBRATION_MAX_FP_VALIDATION_BP' THEN valor END) validation_bp,
                    MAX(CASE WHEN nome=N'FS_DECISION_CALIBRATION_MAX_FP_TEST_BP' THEN valor END) test_bp,
                    MAX(CASE WHEN nome=N'FS_DECISION_CALIBRATION_VALIDATION_FP_LIMIT' THEN valor END) validation_limit,
                    MAX(CASE WHEN nome=N'FS_DECISION_CALIBRATION_TEST_FP_LIMIT' THEN valor END) test_limit,
                    MAX(CASE WHEN nome=N'FS_DECISION_CALIBRATION_VALIDATION_POSITIVE' THEN valor END) validation_positive,
                    MAX(CASE WHEN nome=N'FS_DECISION_CALIBRATION_VALIDATION_NEGATIVE' THEN valor END) validation_negative,
                    MAX(CASE WHEN nome=N'FS_DECISION_CALIBRATION_TEST_POSITIVE' THEN valor END) test_positive,
                    MAX(CASE WHEN nome=N'FS_DECISION_CALIBRATION_TEST_NEGATIVE' THEN valor END) test_negative,
                    MAX(CASE WHEN nome=N'FS_DECISION_CALIBRATION_VALIDATION_FP' THEN valor END) validation_fp,
                    MAX(CASE WHEN nome=N'FS_DECISION_CALIBRATION_TEST_FP' THEN valor END) test_fp,
                    MAX(CASE WHEN nome=N'FS_DECISION_CALIBRATION_VALIDATION_FN' THEN valor END) validation_fn,
                    MAX(CASE WHEN nome=N'FS_DECISION_CALIBRATION_TEST_FN' THEN valor END) test_fn,
                    MAX(CASE WHEN nome=N'FS_DECISION_CALIBRATION_VALIDATION_INCONCLUSIVE' THEN valor END) validation_inconclusive,
                    MAX(CASE WHEN nome=N'FS_DECISION_CALIBRATION_TEST_INCONCLUSIVE' THEN valor END) test_inconclusive,
                    MAX(CASE WHEN nome=N'FS_DECISION_CALIBRATION_TEST_FP_WRONG_PERSON' THEN valor END) test_wrong_person_fp,
                    MAX(CASE WHEN nome=N'FS_DECISION_CALIBRATION_TEST_FP_LEAVE_TRUTH_OUT' THEN valor END) test_leave_truth_out_fp,
                    MAX(CASE WHEN nome=N'M_SAMPLE_SIZE' THEN valor END) m_sample,
                    MAX(CASE WHEN nome=N'U_SAMPLE_SIZE' THEN valor END) u_sample,
                    MAX(CASE WHEN nome=N'FS_DECISION_CALIBRATION_VALIDATION_FP_EFFECTIVE_CAP_BP' THEN valor END) validation_effective_cap_bp,
                    MAX(CASE WHEN nome=N'FS_DECISION_CALIBRATION_TEST_FP_EFFECTIVE_CAP_BP' THEN valor END) test_effective_cap_bp
                FROM identidade.parametro_linkage WHERE modelo_id=m.modelo_id
            ) p;
            """, connection)
        {
            CommandTimeout = 5
        };

        var components = new List<RuntimeComponentStatus>();
        var queue = new List<QueueStatus>();
        var processing = new List<ActiveProcessingStatus>();
        var deliveries = new List<RecentDeliveryStatus>();
        BronzeMaintenanceStatus? bronze = null;
        var linkageRuns = new List<LinkageRunStatus>();
        LinkageModelGovernanceStatus modelGovernance = new(
            "SEM_MODELO_ATIVO", 0, null, null, null, null, null, null, null, null,
            "NAO_DECLARADO", "NAO_DECLARADO", "PENDENTE_ISSUE_31");
        var modelTransitions = new List<LinkageModelTransitionStatus>();
        var blockingPassSupport = new List<LinkageBlockingPassSupportStatus>();
        var ibgeReference = new IbgeReferenceReadiness("AUSENTE", 0, null, null);
        var calibration = new LinkageCalibrationSummary(
            "SEM_MODELO", null, null, null, null, null,
            null, null, null, null, null, null, null, null,
            null, null, null, null, null, null, null, null,
            null, null, null, null, null);
        LinkageConferenceGovernanceStatus conferenceGovernance = new(
            "SEM_MODELO_ATIVO", null, null, null, null, null, null, null, null, null, null,
            "PENDENTE_ISSUE_31",
            "JORNADA_CALIBRATION_AUDIT_ROUNDTRIP_V1",
            "OBRIGATORIO_NO_EXPORT_NAO_PERSISTIDO");
        string? databaseSolutionSchema = null;

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            components.Add(new RuntimeComponentStatus(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetBoolean(4),
                reader.GetInt64(5),
                ReadDateTimeOffset(reader, 6),
                ReadDateTimeOffset(reader, 7),
                ReadNullableDateTimeOffset(reader, 8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11)));
        }

        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct))
            queue.Add(new QueueStatus(reader.GetString(0), reader.GetInt64(1)));

        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            processing.Add(new ActiveProcessingStatus(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5),
                reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7), ReadNullableDateTimeOffset(reader, 8), ReadNullableDateTimeOffset(reader, 9)));
        }

        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            deliveries.Add(new RecentDeliveryStatus(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                ReadDateTimeOffset(reader, 5), ReadDateTimeOffset(reader, 6)));
        }

        await reader.NextResultAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            bronze = new BronzeMaintenanceStatus(
                ReadDateTimeOffset(reader, 0), ReadDateTimeOffset(reader, 1), reader.GetInt32(2), reader.GetInt32(3),
                reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6));
        }

        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            linkageRuns.Add(new LinkageRunStatus(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4), reader.GetInt64(5),
                reader.GetInt64(6), reader.GetInt64(7), reader.GetInt64(8), reader.GetInt64(9), ReadDateTimeOffset(reader, 10), ReadNullableDateTimeOffset(reader, 11)));
        }

        await reader.NextResultAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            var activeCount = reader.GetInt32(0);
            Guid? modelId = reader.IsDBNull(1) ? null : reader.GetGuid(1);
            modelGovernance = new LinkageModelGovernanceStatus(
                activeCount == 1 ? "OK" : activeCount == 0 ? "SEM_MODELO_ATIVO" : "DIVERGENTE",
                activeCount,
                modelId,
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                ReadNullableDateTimeOffset(reader, 5),
                ReadNullableDateTimeOffset(reader, 6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? "NAO_DECLARADO" : reader.GetString(9),
                reader.IsDBNull(10) ? "NAO_DECLARADO" : reader.GetString(10),
                "PENDENTE_ISSUE_31");
        }

        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            modelTransitions.Add(new LinkageModelTransitionStatus(
                reader.GetInt64(0),
                reader.GetGuid(1),
                reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetString(7),
                ReadDateTimeOffset(reader, 8)));
        }

        await reader.NextResultAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            conferenceGovernance = new LinkageConferenceGovernanceStatus(
                reader.GetString(5),
                reader.GetGuid(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt32(6),
                reader.GetBoolean(7),
                reader.GetBoolean(8),
                reader.GetBoolean(9),
                ReadDateTimeOffset(reader, 11),
                reader.GetString(10) == "NOT_ASSESSED_ISSUE_31"
                    ? "PENDENTE_ISSUE_31"
                    : reader.GetString(10),
                "JORNADA_CALIBRATION_AUDIT_ROUNDTRIP_V1",
                "OBRIGATORIO_NO_EXPORT_NAO_PERSISTIDO");
        }
        else if (modelGovernance.ModelId is not null)
        {
            conferenceGovernance = conferenceGovernance with { Status = "SEM_EVIDENCIA_MODELO_ATIVO" };
        }

        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            blockingPassSupport.Add(new LinkageBlockingPassSupportStatus(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetInt64(2),
                reader.GetInt64(3),
                reader.GetInt64(4),
                reader.GetBoolean(5),
                reader.GetBoolean(6)));
        }

        await reader.NextResultAsync(ct);
        if (await reader.ReadAsync(ct) && !reader.IsDBNull(0))
            databaseSolutionSchema = reader.GetString(0);

        await reader.NextResultAsync(ct);
        var activeReferences = new List<(string Code, string? Sha, bool HasRows)>();
        while (await reader.ReadAsync(ct))
            activeReferences.Add((
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetInt32(2) == 1));
        ibgeReference = activeReferences.Count switch
        {
            0 => new("AUSENTE", 0, null, null),
            1 when activeReferences[0].HasRows && activeReferences[0].Sha is not null
                => new("PRONTA", 1, activeReferences[0].Code, activeReferences[0].Sha),
            1 => new("INCOMPLETA", 1, activeReferences[0].Code, activeReferences[0].Sha),
            _ => new("DIVERGENTE", activeReferences.Count, null, null)
        };

        await reader.NextResultAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            decimal? Dec(int index) => reader.IsDBNull(index) ? null : reader.GetDecimal(index);
            calibration = new LinkageCalibrationSummary(
                "MODELO_ENCONTRADO",
                reader.GetGuid(0),
                reader.GetInt32(1),
                reader.GetString(2),
                ReadNullableDateTimeOffset(reader, 3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                Dec(5) is { } vb ? checked((int)vb) : null,
                Dec(6) is { } tb ? checked((int)tb) : null,
                Dec(7), Dec(8), Dec(9), Dec(10), Dec(11), Dec(12),
                Dec(13), Dec(14), Dec(15), Dec(16), Dec(17), Dec(18),
                Dec(19), Dec(20), Dec(21), Dec(22), Dec(23), Dec(24));
        }

        var configurationHealth = EvaluateConfigurationHealth(components, databaseSolutionSchema);
        var onlineCount = components.Count(x => x.Online);
        var overall = components.Count == 0 || onlineCount == 0
            ? "FALHA"
            : onlineCount == components.Count
                ? "NORMAL"
                : "ATENCAO";
        if (configurationHealth.Status == "DIVERGENTE")
            overall = "FALHA";

        return new OperationalMonitorSnapshot(
            DateTimeOffset.UtcNow,
            overall,
            configurationHealth,
            components,
            queue,
            processing,
            deliveries,
            bronze,
            modelGovernance,
            conferenceGovernance,
            blockingPassSupport,
            modelTransitions,
            linkageRuns,
            calibration,
            ibgeReference);
    }

    public static bool IsSchemaUnavailable(SqlException ex) => ex.Number is 207 or 208;

    private static ConfigurationBundleHealthStatus EvaluateConfigurationHealth(
        IReadOnlyCollection<RuntimeComponentStatus> components,
        string? databaseSolutionSchema)
    {
        var expectedBundle = NormalizeOptional(Environment.GetEnvironmentVariable("JORNADA_CONFIGURATION_BUNDLE_VERSION"));
        var expectedSchema = NormalizeOptional(Environment.GetEnvironmentVariable("JORNADA_SOLUTION_SCHEMA_VERSION"));
        var online = components.Where(x => x.Online).ToArray();
        var bundleVersions = online
            .Select(x => NormalizeOptional(x.ConfigurationBundleVersion) ?? "NAO_INFORMADO")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var expectedSchemas = online
            .Select(x => NormalizeOptional(x.SolutionSchemaExpected) ?? "NAO_INFORMADO")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (expectedBundle is null || expectedSchema is null)
        {
            return new ConfigurationBundleHealthStatus(
                "NAO_CONFIGURADO",
                expectedBundle,
                expectedSchema,
                databaseSolutionSchema,
                bundleVersions,
                expectedSchemas,
                "A API não recebeu JORNADA_CONFIGURATION_BUNDLE_VERSION/JORNADA_SOLUTION_SCHEMA_VERSION; a checagem de paridade permanece informativa até a instalação cluster versionada.");
        }

        var mismatchedComponent = online.Any(x =>
            !string.Equals(NormalizeOptional(x.ConfigurationBundleVersion), expectedBundle, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(NormalizeOptional(x.SolutionSchemaExpected), expectedSchema, StringComparison.OrdinalIgnoreCase));
        var mismatchedDatabase = !string.Equals(databaseSolutionSchema, expectedSchema, StringComparison.OrdinalIgnoreCase);

        if (mismatchedComponent || mismatchedDatabase)
        {
            var reason = mismatchedComponent && mismatchedDatabase
                ? "Processos residentes e SQL divergem do bundle esperado."
                : mismatchedComponent
                    ? "Ao menos um processo residente está em bundle/schema diferente do esperado."
                    : "Jornada.SolutionSchema no SQL diverge do schema esperado pelo bundle.";
            return new ConfigurationBundleHealthStatus(
                "DIVERGENTE",
                expectedBundle,
                expectedSchema,
                databaseSolutionSchema,
                bundleVersions,
                expectedSchemas,
                reason);
        }

        return new ConfigurationBundleHealthStatus(
            "OK",
            expectedBundle,
            expectedSchema,
            databaseSolutionSchema,
            bundleVersions,
            expectedSchemas,
            online.Length == 0
                ? "Bundle configurado; aguardando heartbeat de processos residentes."
                : "Todos os processos online e o SQL reportam a identidade técnica esperada.");
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static DateTimeOffset ReadDateTimeOffset(System.Data.Common.DbDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);
        return value switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
            _ => throw new InvalidDataException($"Valor temporal inesperado no ordinal {ordinal}: {value.GetType().FullName}.")
        };
    }

    private static DateTimeOffset? ReadNullableDateTimeOffset(System.Data.Common.DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ReadDateTimeOffset(reader, ordinal);
}
