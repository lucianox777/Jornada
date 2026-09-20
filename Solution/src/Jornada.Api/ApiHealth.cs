using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Jornada.Api;

internal sealed record ApiOperationalPaths(
    string BronzeRootPath,
    string StagingRootPath,
    bool BronzeConfigurationValid,
    bool StagingConfigurationValid);

internal sealed record ApiReadinessCheck(string Name, bool Ready, string? Code = null);
internal sealed record ApiReadinessResult(bool Ready, IReadOnlyList<ApiReadinessCheck> Checks);

internal interface ISqlReadinessProbe
{
    Task<ApiReadinessCheck> CheckAsync(CancellationToken ct);
}

internal sealed class SqlSchemaReadinessProbe(IOperationalSqlAdapter connections) : ISqlReadinessProbe
{
    public async Task<ApiReadinessCheck> CheckAsync(CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            await using var connection = await connections.OpenAsync(timeout.Token);
            const string readinessSql = """
                DECLARE @base NVARCHAR(32)=CONVERT(NVARCHAR(32),(SELECT value FROM sys.extended_properties WHERE class=0 AND name=N'Jornada.BaseNormativa'));
                DECLARE @solution NVARCHAR(32)=CONVERT(NVARCHAR(32),(SELECT value FROM sys.extended_properties WHERE class=0 AND name=N'Jornada.SolutionSchema'));
                SELECT CASE WHEN
                    @base=N'3.62'
                    AND @solution=N'3.70'
                    AND OBJECT_ID(N'jornada.schema_migration',N'U') IS NOT NULL
                    AND OBJECT_ID(N'ref.gestor',N'U') IS NOT NULL
                    AND OBJECT_ID(N'ingestao.entrega',N'U') IS NOT NULL
                    AND OBJECT_ID(N'identidade.pessoa',N'U') IS NOT NULL
                    AND OBJECT_ID(N'identidade.cpf_ancora',N'U') IS NOT NULL
                    AND OBJECT_ID(N'identidade.pessoa_origem_progressiva',N'U') IS NOT NULL
                    AND OBJECT_ID(N'identidade.pessoa_origem_progressiva_evento',N'U') IS NOT NULL
                    AND OBJECT_ID(N'identidade.composicao_uuid_reserva',N'U') IS NOT NULL
                    AND OBJECT_ID(N'identidade.composicao_plano',N'U') IS NOT NULL
                    AND OBJECT_ID(N'identidade.composicao_aplicacao',N'U') IS NOT NULL
                    AND OBJECT_ID(N'identidade.composicao_historico_aplicado',N'U') IS NOT NULL
                    AND OBJECT_ID(N'identidade.composicao_recomposicao_plano',N'U') IS NOT NULL
                    AND OBJECT_ID(N'identidade.composicao_publicacao',N'U') IS NOT NULL
                    AND OBJECT_ID(N'identidade.blocking_chave',N'U') IS NOT NULL
                    AND OBJECT_ID(N'identidade.linkage_ruleset',N'U') IS NOT NULL
                    AND OBJECT_ID(N'identidade.linkage_ruleset_passe',N'U') IS NOT NULL
                    AND OBJECT_ID(N'identidade.linkage_ruleset_passe_campo',N'U') IS NOT NULL
                    AND OBJECT_ID(N'ref.frequencia_nome_versao',N'U') IS NOT NULL
                    AND OBJECT_ID(N'ref.frequencia_nome',N'U') IS NOT NULL
                    AND OBJECT_ID(N'ref.frequencia_nome_cobertura',N'U') IS NOT NULL
                    AND OBJECT_ID(N'identidade.linkage_quality_estimate',N'U') IS NOT NULL
                    AND OBJECT_ID(N'controle.runtime_componente',N'U') IS NOT NULL
                    AND OBJECT_ID(N'gold.pessoa',N'U') IS NOT NULL
                    AND OBJECT_ID(N'serving.registro_integrado',N'U') IS NOT NULL
                    AND OBJECT_ID(N'serving.v_bi_qualidade_resolucao_operacional',N'V') IS NOT NULL
                    AND OBJECT_ID(N'serving.v_bi_qualidade_resolucao_calibrada',N'V') IS NOT NULL
                    AND OBJECT_ID(N'serving.v_bi_qualidade_resolucao_operacional_origem',N'V') IS NOT NULL
                    AND OBJECT_ID(N'serving.v_bi_qualidade_resolucao_operacional_estrato',N'V') IS NOT NULL
                    AND OBJECT_ID(N'serving.v_bi_completude_pessoa',N'V') IS NOT NULL
                    AND OBJECT_ID(N'identidade.sp_recompor_gold_pessoa',N'P') IS NOT NULL
                    AND OBJECT_ID(N'ref.fn_email_canonico_v2',N'FN') IS NOT NULL
                    AND OBJECT_ID(N'ref.fn_telefone_br_canonico_v2',N'FN') IS NOT NULL
                    AND COL_LENGTH(N'gold.pessoa',N'nome_publicacao_normalizado') IS NOT NULL
                    AND COL_LENGTH(N'gold.pessoa',N'estado_identidade') IS NOT NULL
                    AND COL_LENGTH(N'gold.pessoa',N'completude_nucleo') IS NOT NULL
                    AND COL_LENGTH(N'identidade.modelo_linkage',N'frequencia_nome_versao_id') IS NOT NULL
                    AND COL_LENGTH(N'identidade.linkage_run',N'frequencia_nome_versao_id') IS NOT NULL
                    AND EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'silver.pessoa_observacao') AND name=N'nome_completo' AND is_nullable=1)
                    AND EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'silver.pessoa_observacao') AND name=N'nome_cmp' AND is_nullable=1)
                    AND EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'silver.pessoa_observacao') AND name=N'data_nascimento' AND is_nullable=1)
                    AND EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'silver.pessoa_observacao') AND name=N'nome_mae' AND is_nullable=1)
                    AND EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'silver.pessoa_observacao') AND name=N'nome_mae_cmp' AND is_nullable=1)
                    AND EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'gold.pessoa') AND name=N'nome_completo' AND is_nullable=1)
                    AND EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'gold.pessoa') AND name=N'data_nascimento' AND is_nullable=1)
                    AND EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'gold.pessoa') AND name=N'nome_mae' AND is_nullable=1)
                    AND EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'gold.pessoa') AND name=N'ck_gold_pessoa_estado_identidade')
                    AND EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'gold.pessoa') AND name=N'ck_gold_pessoa_completude_nucleo')
                THEN 1 ELSE 0 END;
                """;
            await using var command = new SqlCommand(readinessSql, connection) { CommandTimeout = 5 };
            var compatible = Convert.ToInt32(await command.ExecuteScalarAsync(timeout.Token), System.Globalization.CultureInfo.InvariantCulture) == 1;
            return compatible
                ? new ApiReadinessCheck("sql", true)
                : new ApiReadinessCheck("sql", false, "SQL_SCHEMA_INCOMPATIVEL");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new ApiReadinessCheck("sql", false, "SQL_TIMEOUT");
        }
        catch (SqlException)
        {
            return new ApiReadinessCheck("sql", false, "SQL_INDISPONIVEL");
        }
        catch (InvalidOperationException)
        {
            return new ApiReadinessCheck("sql", false, "SQL_CONFIGURACAO_INVALIDA");
        }
        catch (ArgumentException)
        {
            return new ApiReadinessCheck("sql", false, "SQL_CONFIGURACAO_INVALIDA");
        }
    }
}

internal sealed class ApiReadinessProbe(ISqlReadinessProbe sqlProbe,
    ApiOperationalPaths paths,
    IServiceProvider? services = null)
{
    public async Task<ApiReadinessResult> CheckAsync(CancellationToken ct)
    {
        var checks = new List<ApiReadinessCheck>(4)
        {
            await sqlProbe.CheckAsync(ct),
            CheckCorporateIdentityReadiness(),
            paths.BronzeConfigurationValid
                ? CheckWritableDirectory("bronze", paths.BronzeRootPath)
                : new ApiReadinessCheck("bronze", false, "BRONZE_CONFIGURACAO_INVALIDA"),
            paths.StagingConfigurationValid
                ? CheckWritableDirectory("staging", paths.StagingRootPath)
                : new ApiReadinessCheck("staging", false, "STAGING_CONFIGURACAO_INVALIDA")
        };
        return new ApiReadinessResult(checks.All(x => x.Ready), checks);
    }

    private ApiReadinessCheck CheckCorporateIdentityReadiness()
    {
        // Em runtime o container fornece o ambiente real. Development usa apenas credenciais sintéticas locais.
        // Fora de Development, esta distribuição permanece deliberadamente fail-closed até que o wiring
        // de identidade corporativa e autorização operacional substitua os componentes pendentes em Program.cs.
        var environment = services?.GetService<IHostEnvironment>();
        if (environment is null || environment.IsDevelopment())
            return new ApiReadinessCheck("corporate-identity", true);

        return new ApiReadinessCheck(
            "corporate-identity",
            false,
            "CORPORATE_IDENTITY_AUTHORIZATION_PENDING");
    }

    private static ApiReadinessCheck CheckWritableDirectory(string name, string path)
    {
        string? probe = null;
        try
        {
            Directory.CreateDirectory(path);
            probe = Path.Combine(path, $".jornada-readiness-{Guid.NewGuid():N}.tmp");
            using (var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.WriteThrough))
            {
                stream.WriteByte(0x4a);
                stream.Flush(flushToDisk: true);
            }
            File.Delete(probe);
            return new ApiReadinessCheck(name, true);
        }
        catch (UnauthorizedAccessException)
        {
            return new ApiReadinessCheck(name, false, $"{name.ToUpperInvariant()}_SEM_ESCRITA");
        }
        catch (IOException)
        {
            return new ApiReadinessCheck(name, false, $"{name.ToUpperInvariant()}_INDISPONIVEL");
        }
        finally
        {
            if (probe is not null)
            {
                try { if (File.Exists(probe)) File.Delete(probe); }
                catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("Falha best-effort ao remover probe de readiness; tipo={0}", ex.GetType().Name); }
            }
        }
    }
}
