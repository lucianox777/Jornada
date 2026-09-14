using System.Data.Common;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jornada.Ensaio;

public sealed record Bloco(string Chave,string Descricao,IReadOnlyList<string> Colunas,IReadOnlyList<IReadOnlyList<string?>> Linhas,string? Erro=null);
public sealed record Checkpoint(string Etapa,string Descricao,DateTimeOffset CapturadoEm,string BaselineSha,IReadOnlyList<Bloco> Blocos);

public sealed class CheckpointCollector(Func<DbConnection> abrirConexao)
{
    private static readonly (string Chave,string Descricao,string Sql)[] Consultas =
    [
        ("modo_carga_inicial","Modo de carga inicial","SELECT TOP 1 CAST(ativo AS INT) AS ativo, ativado_em FROM controle.modo_carga_inicial ORDER BY ativado_em DESC"),
        ("volumes","Volume por camada","SELECT 'silver.pessoa_origem' camada,COUNT(*) n FROM silver.pessoa_origem UNION ALL SELECT 'silver.pessoa_observacao',COUNT(*) FROM silver.pessoa_observacao UNION ALL SELECT 'identidade.pessoa',COUNT(*) FROM identidade.pessoa UNION ALL SELECT 'gold.pessoa',COUNT(*) FROM gold.pessoa UNION ALL SELECT 'gold.beneficio_concedido',COUNT(*) FROM gold.beneficio_concedido UNION ALL SELECT 'gold.servico_prestado',COUNT(*) FROM gold.servico_prestado"),
        ("entregas","Entregas por Gestor","SELECT g.codigo gestor,COUNT(*) entregas,MAX(e.recebido_em) ultima FROM ingestao.entrega e LEFT JOIN ref.gestor g ON g.gestor_id=e.gestor_id GROUP BY g.codigo ORDER BY COUNT(*) DESC"),
        ("itens_processados","Itens processados por status","SELECT status,COUNT(*) n FROM ingestao.item_processado GROUP BY status ORDER BY COUNT(*) DESC"),
        ("gold_status_cpf","Gold: status de CPF","SELECT status_cpf,COUNT(*) n FROM gold.pessoa GROUP BY status_cpf ORDER BY COUNT(*) DESC"),
        ("gold_fontes","Gold: Gestores distintos por pessoa","SELECT fontes_distintas,COUNT(*) n FROM gold.pessoa GROUP BY fontes_distintas ORDER BY fontes_distintas"),
        ("gold_concordancia","Gold: estado de concordância","SELECT estado_concordancia,COUNT(*) n FROM gold.pessoa GROUP BY estado_concordancia ORDER BY COUNT(*) DESC"),
        ("gold_preenchimento","Gold: preenchimento dos campos de linkage (%)","SELECT COUNT(*) pessoas,CAST(100.0*SUM(CASE WHEN cpf IS NOT NULL THEN 1 ELSE 0 END)/NULLIF(COUNT(*),0) AS DECIMAL(5,2)) pct_cpf,CAST(100.0*SUM(CASE WHEN nome_mae IS NOT NULL THEN 1 ELSE 0 END)/NULLIF(COUNT(*),0) AS DECIMAL(5,2)) pct_nome_mae,CAST(100.0*SUM(CASE WHEN data_nascimento IS NOT NULL THEN 1 ELSE 0 END)/NULLIF(COUNT(*),0) AS DECIMAL(5,2)) pct_nascimento FROM gold.pessoa"),
        ("vinculos","Vínculos ativos por status e método","SELECT status,metodo_resolucao,COUNT(*) n FROM identidade.vinculo_fonte WHERE ativo=1 GROUP BY status,metodo_resolucao ORDER BY COUNT(*) DESC"),
        ("pares_m","Pares m disponíveis (mesma pessoa determinística, Gestores distintos)",";WITH obs AS (SELECT v.pessoa_uuid,o.gestor_id FROM identidade.vinculo_fonte v JOIN silver.pessoa_observacao o ON o.pessoa_observacao_id=v.pessoa_observacao_id WHERE v.ativo=1 AND v.status='RESOLVIDO' AND v.metodo_resolucao='CPF_DETERMINISTICO' AND v.pessoa_uuid IS NOT NULL GROUP BY v.pessoa_uuid,o.gestor_id), multi AS (SELECT pessoa_uuid,COUNT(DISTINCT gestor_id) g FROM obs GROUP BY pessoa_uuid HAVING COUNT(DISTINCT gestor_id)>1) SELECT COUNT(*) pessoas_multi_gestor,ISNULL(SUM(g*(g-1)/2),0) pares_possiveis FROM multi"),
        ("independencia","Independência intra-Gestor entre sistemas de origem",";WITH obs AS (SELECT v.pessoa_uuid,o.gestor_id,p.sistema_origem_id,o.nome_completo,o.nome_mae,o.data_nascimento,ROW_NUMBER() OVER(PARTITION BY v.pessoa_uuid,p.sistema_origem_id ORDER BY o.pessoa_observacao_id DESC) rn FROM identidade.vinculo_fonte v JOIN silver.pessoa_observacao o ON o.pessoa_observacao_id=v.pessoa_observacao_id JOIN silver.pessoa_origem p ON p.pessoa_origem_id=o.pessoa_origem_id WHERE v.ativo=1 AND v.status='RESOLVIDO' AND v.metodo_resolucao='CPF_DETERMINISTICO' AND v.pessoa_uuid IS NOT NULL),u AS(SELECT * FROM obs WHERE rn=1) SELECT COUNT(*) pares,CAST(100.0*SUM(CASE WHEN a.nome_completo=b.nome_completo OR(a.nome_completo IS NULL AND b.nome_completo IS NULL) THEN 1 ELSE 0 END)/NULLIF(COUNT(*),0) AS DECIMAL(5,2)) pct_nome_exato FROM u a JOIN u b ON a.pessoa_uuid=b.pessoa_uuid AND a.gestor_id=b.gestor_id AND a.sistema_origem_id<b.sistema_origem_id"),
        ("modelos","Modelos de linkage","SELECT TOP 10 versao,status,CASE WHEN base_referencia='CI_LINKAGE_SYNTHETIC' THEN 1 ELSE 0 END sintetico,base_referencia,gerado_em FROM identidade.modelo_linkage ORDER BY gerado_em DESC"),
        ("parametros_ativos","Parâmetros do modelo ATIVO","SELECT p.nome chave,p.valor FROM identidade.parametro_linkage p JOIN identidade.modelo_linkage m ON m.modelo_id=p.modelo_id WHERE m.status='ATIVO' ORDER BY p.nome"),
        ("estatistica_linkage","Estatísticas de auditoria da calibração","SELECT TOP 40 e.nome chave,e.valor FROM identidade.estatistica_linkage e JOIN identidade.modelo_linkage m ON m.modelo_id=e.modelo_id ORDER BY m.gerado_em DESC,e.nome"),
        ("linkage_runs","Execuções de linkage","SELECT TOP 10 modo,status,avaliados,iniciado_em FROM identidade.linkage_run ORDER BY iniciado_em DESC"),
        ("qualidade_estimada","Qualidade calibrada (PPV / sensibilidade)","SELECT estrato,tamanho_amostra_referencia,ppv_estimado,ppv_ic_inferior,ppv_ic_superior,sensibilidade_estimada,sensibilidade_ic_inferior,sensibilidade_ic_superior,medido_em FROM identidade.linkage_quality_estimate ORDER BY medido_em DESC")
    ];

    public async Task<Checkpoint> ColetarAsync(EnsaioEtapa etapa,string baselineSha,CancellationToken ct)
    {
        var blocos=new List<Bloco>(); await using var conn=abrirConexao(); await conn.OpenAsync(ct);
        foreach(var (chave,descricao,sql) in Consultas) try { await using var cmd=conn.CreateCommand(); cmd.CommandText=sql; cmd.CommandTimeout=120; await using var r=await cmd.ExecuteReaderAsync(ct); var cols=Enumerable.Range(0,r.FieldCount).Select(r.GetName).ToArray(); var linhas=new List<IReadOnlyList<string?>>(); while(await r.ReadAsync(ct)){var l=new string?[r.FieldCount];for(var i=0;i<r.FieldCount;i++)l[i]=await r.IsDBNullAsync(i,ct)?null:Convert.ToString(r.GetValue(i),System.Globalization.CultureInfo.InvariantCulture);linhas.Add(l);} blocos.Add(new(chave,descricao,cols,linhas)); } catch(Exception ex){blocos.Add(new(chave,descricao,[],[],ex.Message.Split('\n')[0]));}
        return new(etapa.Codigo,etapa.Descricao,DateTimeOffset.Now,baselineSha,blocos);
    }

    private static readonly JsonSerializerOptions Json=new(){WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,DefaultIgnoreCondition=JsonIgnoreCondition.WhenWritingNull};
    public static async Task GravarAsync(Checkpoint cp,string dir,CancellationToken ct){Directory.CreateDirectory(dir);await File.WriteAllTextAsync(Path.Combine(dir,$"{cp.Etapa}.json"),JsonSerializer.Serialize(cp,Json),ct);var sb=new StringBuilder($"# {cp.Etapa} — {cp.Descricao}\n\nCapturado: `{cp.CapturadoEm:O}`  \nBaseline: `{cp.BaselineSha}`\n\n");foreach(var b in cp.Blocos){sb.AppendLine($"## {b.Descricao}").AppendLine();if(b.Erro is not null){sb.AppendLine($"_indisponível: {b.Erro}_\n");continue;}if(b.Linhas.Count==0){sb.AppendLine("_sem linhas_\n");continue;}sb.AppendLine("| "+string.Join(" | ",b.Colunas)+" |").AppendLine("|"+string.Join("|",b.Colunas.Select(_=>"---"))+"|");foreach(var l in b.Linhas)sb.AppendLine("| "+string.Join(" | ",l.Select(x=>x??""))+" |");sb.AppendLine();}await File.WriteAllTextAsync(Path.Combine(dir,$"{cp.Etapa}.md"),sb.ToString(),ct);}
}
