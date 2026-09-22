using System.Data;
using System.Globalization;
using System.Text;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;

namespace Jornada.Api;

internal static class SyntheticOperationalMonitorGate
{
    internal const string EnvironmentProperty = "Jornada.EnvironmentProfile";
    internal const string RequiredProfile = "Development";

    internal static bool IsResidentDevelopment(IOperationalSqlAdapter operationalSql)
    {
        try
        {
            using var connection = operationalSql.CreateConnection();
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT CONVERT(nvarchar(32),(SELECT value FROM sys.extended_properties WHERE class=0 AND name=N'Jornada.EnvironmentProfile'));";
            var value = Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            return string.Equals(value, RequiredProfile, StringComparison.Ordinal);
        }
        catch
        {
            // Falha fechada: sem prova residente de Development, a rota nem é mapeada.
            return false;
        }
    }
}

public sealed record SyntheticMonitorMetric(
    string Scope,
    string? Dimension,
    string Metric,
    decimal Value,
    string Unit);

public sealed record SyntheticMonitorDispersion(
    string Scope,
    string? Dimension,
    string Metric,
    string Unit,
    int SampleCount,
    decimal Minimum,
    decimal Mean,
    decimal Maximum,
    decimal PopulationStandardDeviation);

public sealed record SyntheticMonitorEvaluation(
    Guid EvaluationId,
    Guid RunGroupId,
    Guid ModelId,
    int ModelVersion,
    string ModelStatus,
    ulong Seed,
    string GeneratorVersion,
    string EvaluatorVersion,
    string EnvironmentProfile,
    string RulesetVersion,
    string ModelSnapshotSha256,
    string CorpusFingerprintSha256,
    string RulesetFingerprintSha256,
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset RecordedAtUtc);

public sealed record SyntheticMonitorGroup(
    Guid RunGroupId,
    string Status,
    IReadOnlyList<ulong> ExpectedSeeds,
    IReadOnlyList<ulong> CompletedSeeds,
    IReadOnlyList<ulong> MissingSeeds,
    IReadOnlyList<ulong> UnexpectedSeeds,
    IReadOnlyList<ulong> DuplicateSeeds,
    IReadOnlyList<SyntheticMonitorDispersion> Dispersion);

public sealed record SyntheticMonitorSnapshot(
    string Banner,
    SyntheticMonitorEvaluation? LatestEvaluation,
    SyntheticMonitorGroup? Group,
    IReadOnlyList<SyntheticMonitorMetric> Metrics);

public sealed class SyntheticOperationalMonitorService(IOperationalSqlAdapter operationalSql)
{
    public const string Banner = "SINTÉTICO — NÃO PROMOVÍVEL";

    public async Task<SyntheticMonitorSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        await using var connection = await operationalSql.OpenAsync(cancellationToken);
        var latest = await ReadLatestAsync(connection, cancellationToken);
        if (latest is null)
        {
            return new SyntheticMonitorSnapshot(
                Banner,
                null,
                null,
                Array.Empty<SyntheticMonitorMetric>());
        }

        var metrics = await ReadMetricsAsync(connection, latest.EvaluationId, cancellationToken);
        var group = await ReadGroupAsync(connection, latest.RunGroupId, cancellationToken);
        return new SyntheticMonitorSnapshot(Banner, latest, group, metrics);
    }

    private static async Task<SyntheticMonitorEvaluation?> ReadLatestAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            """
            SELECT TOP(1)
                e.avaliacao_id,e.grupo_execucao_id,e.modelo_id,e.modelo_versao,m.status,
                e.gerador_seed,e.gerador_versao,e.avaliador_versao,e.ambiente_perfil,e.ruleset_versao,
                e.modelo_snapshot_sha256,e.corpus_fingerprint_sha256,e.ruleset_fingerprint_sha256,
                e.relatorio_gerado_em,e.ocorrido_em
            FROM auditoria.v_linkage_avaliacao_sintetica e
            JOIN identidade.modelo_linkage m ON m.modelo_id=e.modelo_id
            ORDER BY e.linkage_avaliacao_sintetica_id DESC;
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var seed = reader.GetDecimal(5);
        if (seed != decimal.Truncate(seed) || seed < 0m || seed > ulong.MaxValue)
            throw new InvalidDataException("Seed sintética inválida no ledger.");

        return new SyntheticMonitorEvaluation(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetInt32(3),
            reader.GetString(4),
            decimal.ToUInt64(seed),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetString(9),
            Convert.ToHexString(reader.GetFieldValue<byte[]>(10)).ToLowerInvariant(),
            Convert.ToHexString(reader.GetFieldValue<byte[]>(11)).ToLowerInvariant(),
            Convert.ToHexString(reader.GetFieldValue<byte[]>(12)).ToLowerInvariant(),
            reader.GetFieldValue<DateTimeOffset>(13),
            reader.GetFieldValue<DateTimeOffset>(14));
    }

    private static async Task<IReadOnlyList<SyntheticMonitorMetric>> ReadMetricsAsync(
        SqlConnection connection,
        Guid evaluationId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            """
            SELECT escopo,dimensao,metrica,valor,unidade
            FROM auditoria.v_linkage_avaliacao_sintetica_metrica
            WHERE avaliacao_id=@avaliacao_id
            ORDER BY escopo,dimensao,metrica;
            """,
            connection);
        command.Parameters.Add("@avaliacao_id", SqlDbType.UniqueIdentifier).Value = evaluationId;

        var result = new List<SyntheticMonitorMetric>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new SyntheticMonitorMetric(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetString(2),
                reader.GetDecimal(3),
                reader.GetString(4)));
        }
        return result;
    }

    private static async Task<SyntheticMonitorGroup> ReadGroupAsync(
        SqlConnection connection,
        Guid runGroupId,
        CancellationToken cancellationToken)
    {
        var evaluations = new List<(Guid EvaluationId, ulong Seed)>();
        await using (var command = new SqlCommand(
                         """
                         SELECT avaliacao_id,gerador_seed
                         FROM auditoria.v_linkage_avaliacao_sintetica
                         WHERE grupo_execucao_id=@grupo
                         ORDER BY linkage_avaliacao_sintetica_id;
                         """,
                         connection))
        {
            command.Parameters.Add("@grupo", SqlDbType.UniqueIdentifier).Value = runGroupId;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var seed = reader.GetDecimal(1);
                if (seed != decimal.Truncate(seed) || seed < 0m || seed > ulong.MaxValue)
                    throw new InvalidDataException("Seed sintética inválida no grupo.");
                evaluations.Add((reader.GetGuid(0), decimal.ToUInt64(seed)));
            }
        }

        var expectedByEvaluation = new Dictionary<Guid, SortedSet<ulong>>();
        await using (var command = new SqlCommand(
                         """
                         SELECT e.avaliacao_id,m.dimensao
                         FROM auditoria.v_linkage_avaliacao_sintetica e
                         JOIN auditoria.v_linkage_avaliacao_sintetica_metrica m
                           ON m.avaliacao_id=e.avaliacao_id
                         WHERE e.grupo_execucao_id=@grupo
                           AND m.escopo=N'MULTI_SEED_EXPECTED'
                           AND m.metrica=N'EXPECTED'
                           AND m.valor=1
                         ORDER BY e.linkage_avaliacao_sintetica_id,m.dimensao;
                         """,
                         connection))
        {
            command.Parameters.Add("@grupo", SqlDbType.UniqueIdentifier).Value = runGroupId;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var evaluationId = reader.GetGuid(0);
                if (!ulong.TryParse(reader.GetString(1), NumberStyles.None, CultureInfo.InvariantCulture, out var seed))
                    throw new InvalidDataException("Seed esperada inválida no ledger.");
                if (!expectedByEvaluation.TryGetValue(evaluationId, out var set))
                {
                    set = [];
                    expectedByEvaluation.Add(evaluationId, set);
                }
                set.Add(seed);
            }
        }

        var canonical = evaluations.Count > 0
                        && expectedByEvaluation.TryGetValue(evaluations[0].EvaluationId, out var first)
            ? first
            : new SortedSet<ulong>();
        var consistent = canonical.Count > 0
                         && evaluations.All(item =>
                             expectedByEvaluation.TryGetValue(item.EvaluationId, out var expected)
                             && expected.SetEquals(canonical));

        var expectedSeeds = canonical.Order().ToArray();
        var completed = evaluations.Select(static item => item.Seed).Distinct().Order().ToArray();
        var duplicates = evaluations
            .GroupBy(static item => item.Seed)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .Order()
            .ToArray();
        var missing = expectedSeeds.Except(completed).Order().ToArray();
        var unexpected = completed.Except(expectedSeeds).Order().ToArray();
        var complete = consistent
                       && duplicates.Length == 0
                       && missing.Length == 0
                       && unexpected.Length == 0
                       && completed.Length == expectedSeeds.Length;
        var status = complete ? "CONCLUIDO" : consistent ? "INCOMPLETO" : "INCONSISTENTE";
        var dispersion = complete
            ? await ReadDispersionAsync(connection, runGroupId, expectedSeeds.Length, cancellationToken)
            : Array.Empty<SyntheticMonitorDispersion>();

        return new SyntheticMonitorGroup(
            runGroupId,
            status,
            expectedSeeds,
            completed,
            missing,
            unexpected,
            duplicates,
            dispersion);
    }

    private static async Task<SyntheticMonitorDispersion[]> ReadDispersionAsync(
        SqlConnection connection,
        Guid runGroupId,
        int sampleCount,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            """
            SELECT m.escopo,m.dimensao,m.metrica,m.unidade,m.valor
            FROM auditoria.v_linkage_avaliacao_sintetica e
            JOIN auditoria.v_linkage_avaliacao_sintetica_metrica m
              ON m.avaliacao_id=e.avaliacao_id
            WHERE e.grupo_execucao_id=@grupo
              AND m.escopo<>N'MULTI_SEED_EXPECTED'
            ORDER BY m.escopo,m.dimensao,m.metrica,e.gerador_seed;
            """,
            connection);
        command.Parameters.Add("@grupo", SqlDbType.UniqueIdentifier).Value = runGroupId;

        var values = new Dictionary<(string Scope, string? Dimension, string Metric, string Unit), List<decimal>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var key = (
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3));
            if (!values.TryGetValue(key, out var list))
            {
                list = [];
                values.Add(key, list);
            }
            list.Add(reader.GetDecimal(4));
        }

        return values
            .Where(pair => pair.Value.Count == sampleCount)
            .OrderBy(static pair => pair.Key.Scope, StringComparer.Ordinal)
            .ThenBy(static pair => pair.Key.Dimension, StringComparer.Ordinal)
            .ThenBy(static pair => pair.Key.Metric, StringComparer.Ordinal)
            .Select(pair =>
            {
                var mean = pair.Value.Average();
                var variance = pair.Value.Select(value =>
                {
                    var delta = (double)(value - mean);
                    return delta * delta;
                }).Average();
                return new SyntheticMonitorDispersion(
                    pair.Key.Scope,
                    pair.Key.Dimension,
                    pair.Key.Metric,
                    pair.Key.Unit,
                    pair.Value.Count,
                    pair.Value.Min(),
                    mean,
                    pair.Value.Max(),
                    (decimal)Math.Sqrt(variance));
            })
            .ToArray();
    }
}

internal static class SyntheticOperationalMonitorPage
{
    internal const string Html = """
<!doctype html>
<html lang="pt-BR">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Jornada — Monitor sintético DEV</title>
<style>
body{font-family:system-ui,sans-serif;margin:0;background:#f6f7f8;color:#18202a}
main{max-width:1200px;margin:auto;padding:24px}.banner{background:#fff3cd;border:1px solid #d6b756;padding:14px 18px;font-weight:800}
.card{background:#fff;border:1px solid #d9dee5;border-radius:8px;padding:16px;margin-top:16px}
.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:10px}
.k{font-size:.78rem;color:#59636f}.v{font-weight:650;word-break:break-all}
table{border-collapse:collapse;width:100%;font-size:.88rem}th,td{border-bottom:1px solid #e3e6ea;padding:7px;text-align:left}
.bad{font-weight:750}.muted{color:#68727e}code{font-size:.78rem}
</style>
</head>
<body><main>
<div class="banner">SINTÉTICO — NÃO PROMOVÍVEL</div>
<div id="state" class="card">Carregando evidência agregada…</div>
<div id="latest"></div><div id="quality"></div><div id="dispersion"></div>
</main>
<script>
const esc=s=>String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const fmt=v=>typeof v==='number'?v.toLocaleString('pt-BR',{maximumFractionDigits:8}):esc(v);
async function load(){
 const gestor=sessionStorage.getItem('jornada.monitor.gestor')||'';
 const key=sessionStorage.getItem('jornada.monitor.key')||'';
 const r=await fetch('/api/v1/monitor/synthetic',{headers:{'X-Jornada-Gestor':gestor,'X-Jornada-Access-Key':key}});
 if(!r.ok){document.getElementById('state').innerHTML='<b>Falha HTTP '+r.status+'</b>. Abra primeiro <a href="/monitor">/monitor</a> para autenticar o perfil DEV.';return}
 const x=await r.json(), e=x.latestEvaluation, g=x.group, m=x.metrics||[];
 document.getElementById('state').innerHTML='<b>'+esc(x.banner)+'</b> · grupo <span class="bad">'+esc(g?.status??'SEM_EVIDENCIA')+'</span>';
 if(!e){document.getElementById('latest').innerHTML='<div class="card">Nenhuma avaliação sintética persistida.</div>';return}
 document.getElementById('latest').innerHTML='<div class="card"><h2>Última avaliação</h2><div class="grid">'+
   [['Seed',e.seed],['Perfil residente',e.environmentProfile],['Modelo','v'+e.modelVersion+' · '+e.modelStatus],['Run group',e.runGroupId],['Gerador',e.generatorVersion],['Evaluator',e.evaluatorVersion],['Ruleset',e.rulesetVersion],['Corpus fingerprint',e.corpusFingerprintSha256],['Model snapshot',e.modelSnapshotSha256]]
   .map(([k,v])=>'<div><div class="k">'+esc(k)+'</div><div class="v">'+esc(v)+'</div></div>').join('')+'</div>'+
   '<p class="muted">Seeds esperadas: '+esc((g?.expectedSeeds||[]).join(', '))+' · concluídas: '+esc((g?.completedSeeds||[]).join(', '))+' · faltantes: '+esc((g?.missingSeeds||[]).join(', '))+'</p></div>';
 const focus=m.filter(z=>['DECISION_ORACLE','DECISION_QUALITY','BLOCKING','M_DISTANCE_REWEIGHTED','U_DISTANCE_REWEIGHTED'].includes(z.scope));
 document.getElementById('quality').innerHTML='<div class="card"><h2>Oracle, blocking e qualidade</h2><table><thead><tr><th>Escopo</th><th>Dimensão</th><th>Métrica</th><th>Valor</th></tr></thead><tbody>'+
   focus.map(z=>'<tr><td>'+esc(z.scope)+'</td><td>'+esc(z.dimension||'—')+'</td><td>'+esc(z.metric)+'</td><td>'+fmt(z.value)+' '+esc(z.unit)+'</td></tr>').join('')+'</tbody></table></div>';
 const d=g?.dispersion||[];
 document.getElementById('dispersion').innerHTML='<div class="card"><h2>Dispersão multi-seed</h2>'+
   (g?.status!=='CONCLUIDO'?'<p class="muted">Oculta enquanto o grupo não estiver CONCLUIDO; nenhum subconjunto é agregado.</p>':
   '<table><thead><tr><th>Métrica</th><th>N</th><th>Mín</th><th>Média</th><th>Máx</th><th>DP</th></tr></thead><tbody>'+
   d.map(z=>'<tr><td>'+esc(z.scope)+' / '+esc(z.dimension||'—')+' / '+esc(z.metric)+'</td><td>'+z.sampleCount+'</td><td>'+fmt(z.minimum)+'</td><td>'+fmt(z.mean)+'</td><td>'+fmt(z.maximum)+'</td><td>'+fmt(z.populationStandardDeviation)+'</td></tr>').join('')+'</tbody></table>')+'</div>';
}
load().catch(e=>document.getElementById('state').textContent=e.message);
</script></body></html>
""";
}
