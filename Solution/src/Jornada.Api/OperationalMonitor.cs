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
    DateTimeOffset? OccurredAt,
    string StatisticalValidation,
    string RoundTripMethod,
    string RoundTripStatus);

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
    IReadOnlyList<LinkageModelTransitionStatus> LinkageModelTransitions,
    IReadOnlyList<LinkageRunStatus> LinkageRuns);

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

            SELECT TOP(20) lote_id,entrega_id,lote_seq,lote_total,qtd_pessoas,qtd_registros,status,lease_owner,lease_adquirido_em,heartbeat_em
            FROM ingestao.lote
            WHERE status IN(N'VALIDANDO',N'PROCESSANDO')
            ORDER BY lease_adquirido_em,lote_id;

            SELECT TOP(20) e.entrega_id,e.status,g.codigo,so.codigo,b.nome_arquivo,e.recebido_em,e.ultima_atualizacao
            FROM ingestao.entrega e
            JOIN ref.gestor g ON g.gestor_id=e.gestor_id
            JOIN ref.sistema_origem so ON so.sistema_origem_id=e.sistema_origem_id
            JOIN bronze.entrega_arquivo b ON b.entrega_id=e.entrega_id
            ORDER BY e.recebido_em DESC,e.entrega_id DESC;

            SELECT TOP(1) iniciado_em,finalizado_em,objetos_examinados,orfaos_removidos,temporarios_removidos,locks_nao_adquiridos,falhas_storage
            FROM controle.bronze_manutencao_ciclo
            ORDER BY finalizado_em DESC,ciclo_id DESC;

            SELECT TOP(5) linkage_run_id,tipo_run,status,modelo_versao,registros_elegiveis,avaliados,resolvidos,nao_resolvidos,conflitos,iniciado_em,finalizado_em
            FROM identidade.linkage_run
            ORDER BY iniciado_em DESC,linkage_run_id DESC;

            DECLARE @active_model_count INT=(SELECT COUNT(*) FROM identidade.modelo_linkage WHERE status=N'ATIVO');
            DECLARE @active_model_id UNIQUEIDENTIFIER=(
                SELECT TOP(1) modelo_id
                FROM identidade.modelo_linkage
                WHERE status=N'ATIVO'
                ORDER BY versao DESC);
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
                candidatos_avaliados,mesma_decisao_final,mesmo_top1,validacao_estatistica,ocorrido_em
            FROM auditoria.linkage_conferencia_evidencia
            WHERE modelo_id=@active_model_id
            ORDER BY linkage_conferencia_evidencia_id DESC;

            SELECT CONVERT(NVARCHAR(32),(
                SELECT value
                FROM sys.extended_properties
                WHERE class=0 AND name=N'Jornada.SolutionSchema'));
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
        LinkageConferenceGovernanceStatus conferenceGovernance = new(
            "SEM_MODELO_ATIVO", null, null, null, null, null, null, null, null, null,
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
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3), reader.GetInt64(4), reader.GetInt64(5),
                reader.GetInt64(6), reader.GetInt64(7), reader.GetInt64(8), ReadDateTimeOffset(reader, 9), ReadNullableDateTimeOffset(reader, 10)));
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
                ReadDateTimeOffset(reader, 10),
                reader.GetString(9) == "NOT_ASSESSED_ISSUE_31"
                    ? "PENDENTE_ISSUE_31"
                    : reader.GetString(9),
                "JORNADA_CALIBRATION_AUDIT_ROUNDTRIP_V1",
                "OBRIGATORIO_NO_EXPORT_NAO_PERSISTIDO");
        }
        else if (modelGovernance.ModelId is not null)
        {
            conferenceGovernance = conferenceGovernance with { Status = "SEM_EVIDENCIA_MODELO_ATIVO" };
        }

        await reader.NextResultAsync(ct);
        if (await reader.ReadAsync(ct) && !reader.IsDBNull(0))
            databaseSolutionSchema = reader.GetString(0);

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
            modelTransitions,
            linkageRuns);
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
