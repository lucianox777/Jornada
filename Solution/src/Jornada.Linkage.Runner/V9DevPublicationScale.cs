using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Runner;

/// <summary>Only the explicitly admitted disposable DEV parity fixture.
/// Keep the exact guard predicates/THROW codes, but index the transition
/// relations before joining them. Never disable or bypass a trigger.</summary>
internal static class V9DevPublicationScale
{
    internal static bool Enabled =>
        V9DevE2EFullCommand.ScaleAdmitted &&
        Environment.GetEnvironmentVariable("JORNADA_V9_PHASE4_SCALE") == "optimized1k";

    private const string ResultGuard = """
        CREATE OR ALTER TRIGGER identidade.tr_linkage_resultado_publicacao_imutavel
        ON identidade.linkage_resultado
        AFTER UPDATE
        AS
        BEGIN
         SET NOCOUNT ON;
        
         IF EXISTS(
           SELECT 1
           FROM inserted i
           JOIN deleted d ON d.linkage_resultado_id=i.linkage_resultado_id
           WHERE i.linkage_run_id<>d.linkage_run_id
              OR i.modelo_id<>d.modelo_id
              OR i.modelo_versao<>d.modelo_versao
              OR i.pessoa_observacao_id<>d.pessoa_observacao_id
              OR ISNULL(CONVERT(NVARCHAR(36),i.pessoa_uuid_resolvido),N'')<>ISNULL(CONVERT(NVARCHAR(36),d.pessoa_uuid_resolvido),N'')
              OR ISNULL(CONVERT(NVARCHAR(36),i.melhor_candidato_uuid),N'')<>ISNULL(CONVERT(NVARCHAR(36),d.melhor_candidato_uuid),N'')
              OR i.score_melhor<>d.score_melhor
              OR ISNULL(CONVERT(NVARCHAR(36),i.segundo_candidato_uuid),N'')<>ISNULL(CONVERT(NVARCHAR(36),d.segundo_candidato_uuid),N'')
              OR ISNULL(i.score_segundo,CONVERT(DECIMAL(9,8),-1))<>ISNULL(d.score_segundo,CONVERT(DECIMAL(9,8),-1))
              OR ISNULL(i.margem,CONVERT(DECIMAL(18,8),-1))<>ISNULL(d.margem,CONVERT(DECIMAL(18,8),-1))
              OR i.status<>d.status
              OR ISNULL(i.motivo,N'')<>ISNULL(d.motivo,N'')
              OR i.calculado_em<>d.calculado_em)
           THROW 51822,'Evidência bruta de linkage_resultado é imutável.',1;
        
         IF EXISTS(
           SELECT 1
           FROM inserted i
           JOIN identidade.linkage_run lr ON lr.linkage_run_id=i.linkage_run_id
           WHERE lr.status<>N'EXECUTANDO')
           THROW 51823,'Envelope de publicação só pode ser finalizado enquanto o run está EXECUTANDO.',1;
        END;
        """;
    private const string OriginGuard = """
        CREATE OR ALTER TRIGGER identidade.tr_progressiva_origem_guard ON identidade.pessoa_origem_progressiva AFTER UPDATE,DELETE AS
        BEGIN
         SET NOCOUNT ON;
         IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.pessoa_origem_id=d.pessoa_origem_id WHERE i.pessoa_origem_id IS NULL)
           THROW 51110,'Referência inicial de origem não pode ser excluída ou transferida.',1;
         IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.pessoa_origem_id=i.pessoa_origem_id
          WHERE i.initial_uuid<>d.initial_uuid OR i.criado_em<>d.criado_em OR
                ISNULL(i.legacy_pessoa_uuid,'00000000-0000-0000-0000-000000000000')<>ISNULL(d.legacy_pessoa_uuid,'00000000-0000-0000-0000-000000000000'))
           THROW 51111,'UUID inicial, origem e referência legada são imutáveis.',1;
         IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.pessoa_origem_id=i.pessoa_origem_id
          WHERE i.versao<d.versao OR i.versao>d.versao+1 OR
            (i.versao=d.versao AND (i.estado<>d.estado OR
              ISNULL(i.canonical_uuid,'00000000-0000-0000-0000-000000000000')<>ISNULL(d.canonical_uuid,'00000000-0000-0000-0000-000000000000') OR
              ISNULL(i.ultimo_destino_externo_uuid,'00000000-0000-0000-0000-000000000000')<>ISNULL(d.ultimo_destino_externo_uuid,'00000000-0000-0000-0000-000000000000') OR
              ISNULL(i.ultima_resolucao_em,'0001-01-01')<>ISNULL(d.ultima_resolucao_em,'0001-01-01'))))
           THROW 51112,'Alteração de referência exige avanço de uma versão.',1;
         IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.pessoa_origem_id=i.pessoa_origem_id
          WHERE i.versao<>d.versao AND NOT EXISTS(
           SELECT 1 FROM identidade.pessoa_origem_progressiva_evento e
           WHERE e.pessoa_origem_id=i.pessoa_origem_id AND e.versao=i.versao AND e.tipo='RESOLUCAO' AND e.estado=i.estado
             AND (e.canonical_uuid=i.canonical_uuid OR (e.canonical_uuid IS NULL AND i.canonical_uuid IS NULL))
             AND e.ocorrido_em=i.ultima_resolucao_em))
           THROW 51117,'Avanço exige recibo de resolução correspondente.',1;
        END;
        """;

    private static string Normalize(string definition) => Regex.Replace(
        Regex.Replace(definition.Trim(), @"^CREATE(?: OR ALTER)? TRIGGER|^ALTER TRIGGER", "TRIGGER",
            RegexOptions.IgnoreCase), @"\s+", " ");

    internal static string OptimizeGuard(string original, string key)
    {
        if (key is not ("linkage_resultado_id" or "pessoa_origem_id"))
            throw new InvalidDataException("RUNNER_V9_DEV_SCALE_GUARD_KEY");
        const string marker = " SET NOCOUNT ON;";
        if (!original.Contains(marker, StringComparison.Ordinal))
            throw new InvalidDataException("RUNNER_V9_DEV_SCALE_GUARD_SHAPE");
        var body = Regex.Replace(original, @"\b(inserted|deleted)\b",
            m => m.Value == "inserted" ? "#v9_guard_inserted" : "#v9_guard_deleted",
            RegexOptions.CultureInvariant);
        return body.Replace(marker, marker + $"""
            
             SELECT * INTO #v9_guard_inserted FROM inserted;
             SELECT * INTO #v9_guard_deleted FROM deleted;
             CREATE UNIQUE CLUSTERED INDEX IX_v9_guard_inserted ON #v9_guard_inserted({key});
             CREATE UNIQUE CLUSTERED INDEX IX_v9_guard_deleted ON #v9_guard_deleted({key});
            """, StringComparison.Ordinal);
    }

    internal static async Task InstallAsync(SqlConnection connection, Guid attempt, CancellationToken ct)
    {
        if (!Enabled) throw new InvalidOperationException("RUNNER_V9_DEV_SCALE_GUARD_ADMISSION");
        await V9ControlledDevSelection.VerifyDisposableAttemptDatabaseAsync(connection, attempt, ct);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        foreach (var (name, key, definition) in new[] {
            ("identidade.tr_linkage_resultado_publicacao_imutavel","linkage_resultado_id",ResultGuard),
            ("identidade.tr_progressiva_origem_guard","pessoa_origem_id",OriginGuard)
        })
        {
            await using var check = new SqlCommand("SELECT OBJECT_DEFINITION(OBJECT_ID(@name));", connection, tx);
            check.Parameters.Add("@name", System.Data.SqlDbType.NVarChar, 256).Value = name;
            if (await check.ExecuteScalarAsync(ct) is not string actual ||
                !string.Equals(Normalize(actual), Normalize(definition), StringComparison.Ordinal))
                throw new InvalidDataException("RUNNER_V9_DEV_SCALE_GUARD_DEFINITION_DRIFT");
            await using var change = new SqlCommand(OptimizeGuard(definition,key), connection, tx);
            await change.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    // Preserve every row and all precedence of the existing view; read it
    // once inside the original transaction. Do not push target predicates
    // below ROW_NUMBER or replace protected-link precedence.
    internal static string MaterializeCurrentLinks(string original, string marker)
    {
        if (!original.Contains(marker, StringComparison.Ordinal) ||
            !original.Contains("identidade.v_vinculo_corrente", StringComparison.Ordinal))
            throw new InvalidDataException("RUNNER_V9_DEV_SCALE_VIEW_SHAPE");
        const string snapshot = """
            SELECT pessoa_observacao_id,pessoa_uuid,status,metodo_resolucao
            INTO #v9_current_links FROM identidade.v_vinculo_corrente;
            CREATE INDEX IX_v9_current_uuid ON #v9_current_links(pessoa_uuid,status,pessoa_observacao_id);
            CREATE INDEX IX_v9_current_obs ON #v9_current_links(pessoa_observacao_id);
            """;
        return original.Replace("identidade.v_vinculo_corrente", "#v9_current_links", StringComparison.Ordinal)
            .Replace(marker, snapshot + "\n" + marker, StringComparison.Ordinal);
    }
}
