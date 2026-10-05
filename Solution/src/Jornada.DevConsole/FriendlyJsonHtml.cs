using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

static class FriendlyJsonHtml
{
    const int MaxTableRows=200;
    const int MaxCards=100;

    static readonly Dictionary<string,string> Labels=new(StringComparer.OrdinalIgnoreCase)
    {
        ["generatedAt"]="Gerado em",
        ["receivedAt"]="Recebido em",
        ["checkedAt"]="Verificado em",
        ["startedAt"]="Iniciado em",
        ["finishedAt"]="Finalizado em",
        ["status"]="Status",
        ["mode"]="Modo",
        ["modelRole"]="Papel do modelo",
        ["bootstrapReference"]="Referência bootstrap",
        ["goldPeople"]="Pessoas Gold",
        ["bootstrapGoldPeople"]="Pessoas Gold do bootstrap",
        ["modelId"]="ID do modelo",
        ["pessoaUuid"]="UUID da pessoa",
        ["version"]="Versão",
        ["versao"]="Versão",
        ["sampleMethod"]="Método da amostra",
        ["modelConfigBundleVersion"]="Versão do bundle de configuração",
        ["modelConfigBundleFingerprintSha256"]="SHA-256 do bundle de configuração",
        ["database"]="Banco de dados",
        ["gestor"]="Gestor",
        ["zip"]="Arquivo ZIP",
        ["receipt"]="Recibo",
        ["entregaId"]="ID da entrega",
        ["codigoSistemaOrigem"]="Sistema de origem",
        ["codigoPessoaOrigem"]="Código da pessoa na origem",
        ["codigoRegistroOrigem"]="Código do registro na origem",
        ["nomeCompleto"]="Nome completo",
        ["nomeMae"]="Nome da mãe",
        ["dataNascimento"]="Data de nascimento",
        ["estadoIdentidade"]="Estado da identidade",
        ["estado_identidade"]="Estado da identidade",
        ["pessoa_uuid"]="UUID da pessoa",
        ["nome_completo"]="Nome completo",
        ["nome_mae"]="Nome da mãe",
        ["data_nascimento"]="Data de nascimento",
        ["runtimeHealth"]="Status dos serviços",
        ["activeLinkageModel"]="Modelo de linkage ativo",
        ["configurationBundleVersion"]="Versão do bundle",
        ["syntheticOnly"]="Somente dados sintéticos",
        ["people"]="Pessoas",
        ["nameDistribution"]="Distribuição de nomes",
        ["birthDistribution"]="Distribuição de nascimentos",
        ["seed"]="Seed",
        ["message"]="Mensagem",
        ["staleReceipt"]="Recibo de ambiente anterior"
    };

    public static string Render(string path,string json)
    {
        using var doc=JsonDocument.Parse(json);
        var fileName=Path.GetFileName(path);
        var root=doc.RootElement;
        var summary=root.ValueKind switch
        {
            JsonValueKind.Array=>$"{root.GetArrayLength():N0} registro(s)",
            JsonValueKind.Object=>$"{root.EnumerateObject().Count():N0} campo(s)",
            _=>"Valor"
        };

        var b=new StringBuilder(32_768);
        b.Append("<!doctype html><html lang=\"pt-BR\"><head><meta charset=\"utf-8\">")
         .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
         .Append("<title>").Append(E(fileName)).Append("</title>")
         .Append("""
<style>
:root{font-family:system-ui,-apple-system,"Segoe UI",sans-serif;color:#18212b;background:#f4f6f8}
*{box-sizing:border-box}body{margin:0}.page{max-width:1500px;margin:0 auto;padding:28px}
.header{background:white;border:1px solid #dce2e8;border-radius:12px;padding:22px 24px;margin-bottom:18px;box-shadow:0 1px 3px #0000000d}
h1{font-size:1.55rem;margin:0 0 6px}h2{font-size:1.08rem;margin:0 0 14px}h3{font-size:1rem;margin:0 0 10px}
.path{font-family:ui-monospace,Consolas,monospace;color:#66717d;font-size:.82rem;overflow-wrap:anywhere}
.summary{display:inline-block;margin-top:12px;padding:5px 10px;background:#eef3f7;border-radius:999px;font-size:.82rem;font-weight:650}
.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(230px,1fr));gap:12px}
.field{background:white;border:1px solid #dce2e8;border-radius:10px;padding:13px 15px;min-width:0}
.label{font-size:.76rem;text-transform:uppercase;letter-spacing:.045em;color:#697581;font-weight:700;margin-bottom:5px}
.value{font-size:.98rem;overflow-wrap:anywhere}.value code{font-size:.9em}
.group{background:white;border:1px solid #dce2e8;border-radius:12px;padding:18px;margin-top:14px}
.nested{grid-column:1/-1;background:#f9fafb;border:1px solid #e3e8ed;border-radius:9px;padding:14px}
.table-wrap{overflow:auto;border:1px solid #dce2e8;border-radius:10px;background:white}
table{width:100%;border-collapse:collapse;font-size:.9rem}th,td{text-align:left;padding:10px 12px;border-bottom:1px solid #e7ebef;vertical-align:top}
th{position:sticky;top:0;background:#f2f5f7;z-index:1;white-space:nowrap}tr:last-child td{border-bottom:0}
.badge{display:inline-block;padding:3px 8px;border-radius:999px;background:#edf2f6;font-size:.82rem;font-weight:650}
.true{background:#e8f5ec}.false{background:#f8ecec}.null{color:#89939c}.mono{font-family:ui-monospace,Consolas,monospace}
.cards{display:grid;grid-template-columns:repeat(auto-fit,minmax(300px,1fr));gap:12px}.card{border:1px solid #dce2e8;border-radius:10px;padding:14px;background:#fff}
.note{margin:12px 0 0;color:#687480;font-size:.84rem}.chips{display:flex;flex-wrap:wrap;gap:7px}.chip{padding:5px 9px;background:#eef3f7;border-radius:999px;font-size:.85rem}
@media(max-width:700px){.page{padding:14px}.header{padding:17px}.grid{grid-template-columns:1fr}th,td{padding:8px}}
</style>
""")
         .Append("</head><body><main class=\"page\"><header class=\"header\"><h1>")
         .Append(E(fileName)).Append("</h1><div class=\"path\">").Append(E(path))
         .Append("</div><span class=\"summary\">").Append(E(summary)).Append("</span></header>");

        RenderElement(b,root,null,0);
        b.Append("</main></body></html>");
        return b.ToString();
    }

    static void RenderElement(StringBuilder b,JsonElement element,string? heading,int depth)
    {
        if(heading is not null)
            b.Append("<section class=\"group\"><h2>").Append(E(Label(heading))).Append("</h2>");

        switch(element.ValueKind)
        {
            case JsonValueKind.Object:
                RenderObject(b,element,depth);
                break;
            case JsonValueKind.Array:
                RenderArray(b,element,depth);
                break;
            default:
                b.Append("<div class=\"field\"><div class=\"value\">").Append(RenderScalar(element)).Append("</div></div>");
                break;
        }

        if(heading is not null)b.Append("</section>");
    }

    static void RenderObject(StringBuilder b,JsonElement obj,int depth)
    {
        b.Append("<div class=\"grid\">");
        foreach(var p in obj.EnumerateObject())
        {
            if(p.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            {
                b.Append("<div class=\"nested\"><h3>").Append(E(Label(p.Name))).Append("</h3>");
                if(p.Value.ValueKind==JsonValueKind.Object)RenderObject(b,p.Value,depth+1);
                else RenderArray(b,p.Value,depth+1);
                b.Append("</div>");
            }
            else
            {
                b.Append("<div class=\"field\"><div class=\"label\">").Append(E(Label(p.Name)))
                 .Append("</div><div class=\"value\">").Append(RenderScalar(p.Value)).Append("</div></div>");
            }
        }
        b.Append("</div>");
    }

    static void RenderArray(StringBuilder b,JsonElement array,int depth)
    {
        var count=array.GetArrayLength();
        if(count==0)
        {
            b.Append("<div class=\"field\"><span class=\"null\">Nenhum registro.</span></div>");
            return;
        }

        var first=array.EnumerateArray().First();
        if(first.ValueKind==JsonValueKind.Object)
        {
            RenderObjectArrayTable(b,array,count);
            return;
        }

        var primitive=array.EnumerateArray().All(x=>x.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array));
        if(primitive)
        {
            b.Append("<div class=\"chips\">");
            var shown=0;
            foreach(var item in array.EnumerateArray())
            {
                if(shown++>=MaxCards)break;
                b.Append("<span class=\"chip\">").Append(RenderScalar(item)).Append("</span>");
            }
            b.Append("</div>");
            if(count>MaxCards)AppendLimitNote(b,MaxCards,count);
            return;
        }

        b.Append("<div class=\"cards\">");
        var index=0;
        foreach(var item in array.EnumerateArray())
        {
            if(index>=MaxCards)break;
            b.Append("<article class=\"card\"><h3>Item ").Append(index+1).Append("</h3>");
            if(item.ValueKind==JsonValueKind.Object)RenderObject(b,item,depth+1);
            else if(item.ValueKind==JsonValueKind.Array)RenderArray(b,item,depth+1);
            else b.Append("<div class=\"value\">").Append(RenderScalar(item)).Append("</div>");
            b.Append("</article>");
            index++;
        }
        b.Append("</div>");
        if(count>MaxCards)AppendLimitNote(b,MaxCards,count);
    }

    static void RenderObjectArrayTable(StringBuilder b,JsonElement array,int count)
    {
        var columns=new List<string>();
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scanned=0;
        foreach(var row in array.EnumerateArray())
        {
            if(row.ValueKind!=JsonValueKind.Object)continue;
            foreach(var p in row.EnumerateObject())
                if(seen.Add(p.Name))columns.Add(p.Name);
            if(++scanned>=50)break;
        }

        b.Append("<div class=\"table-wrap\"><table><thead><tr>");
        foreach(var column in columns)b.Append("<th>").Append(E(Label(column))).Append("</th>");
        b.Append("</tr></thead><tbody>");

        var shown=0;
        foreach(var row in array.EnumerateArray())
        {
            if(shown++>=MaxTableRows)break;
            b.Append("<tr>");
            foreach(var column in columns)
            {
                b.Append("<td>");
                if(row.ValueKind==JsonValueKind.Object&&row.TryGetProperty(column,out var value))
                {
                    if(value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                        b.Append("<span class=\"badge\">").Append(value.ValueKind==JsonValueKind.Array?$"{value.GetArrayLength():N0} item(ns)":"Detalhes").Append("</span>");
                    else
                        b.Append(RenderScalar(value));
                }
                else b.Append("<span class=\"null\">—</span>");
                b.Append("</td>");
            }
            b.Append("</tr>");
        }
        b.Append("</tbody></table></div>");
        if(count>MaxTableRows)AppendLimitNote(b,MaxTableRows,count);
    }

    static void AppendLimitNote(StringBuilder b,int shown,int total)=>
        b.Append("<p class=\"note\">Exibindo ").Append(shown.ToString("N0",CultureInfo.GetCultureInfo("pt-BR")))
         .Append(" de ").Append(total.ToString("N0",CultureInfo.GetCultureInfo("pt-BR")))
         .Append(" registros. O arquivo completo continua disponível pela opção “Abrir resultado”.</p>");

    static string RenderScalar(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined=>"<span class=\"null\">—</span>",
            JsonValueKind.True=>"<span class=\"badge true\">Sim</span>",
            JsonValueKind.False=>"<span class=\"badge false\">Não</span>",
            JsonValueKind.Number=>E(value.ToString()),
            JsonValueKind.String=>RenderString(value.GetString()??""),
            _=>E(value.ToString())
        };
    }

    static string RenderString(string value)
    {
        if(string.IsNullOrWhiteSpace(value))return "<span class=\"null\">—</span>";

        if(DateOnly.TryParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date))
            return $"<time datetime=\"{E(value)}\">{date:dd/MM/yyyy}</time>";

        if(value.Contains('T')&&DateTimeOffset.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out var dateTime))
            return $"<time datetime=\"{E(value)}\">{dateTime.ToLocalTime():dd/MM/yyyy HH:mm:ss}</time>";

        var encoded=E(value);
        if(Guid.TryParse(value,out _))return $"<code>{encoded}</code>";
        if(value.Length>=40&&value.All(Uri.IsHexDigit))return $"<code>{encoded}</code>";
        if(value.Contains('\\')||value.Contains('/'))return $"<code>{encoded}</code>";
        return encoded;
    }

    static string Label(string name)
    {
        if(Labels.TryGetValue(name,out var known))return known;
        var b=new StringBuilder(name.Length+8);
        for(var i=0;i<name.Length;i++)
        {
            var c=name[i];
            if(c is '_' or '-')
            {
                if(b.Length>0&&b[^1]!=' ')b.Append(' ');
                continue;
            }
            if(i>0&&char.IsUpper(c)&&char.IsLower(name[i-1]))b.Append(' ');
            b.Append(c);
        }
        var words=b.ToString().Split(' ',StringSplitOptions.RemoveEmptyEntries);
        for(var i=0;i<words.Length;i++)
        {
            words[i]=words[i].ToUpperInvariant() switch
            {
                "ID"=>"ID",
                "UUID"=>"UUID",
                "CPF"=>"CPF",
                "IBGE"=>"IBGE",
                "SHA256"=>"SHA-256",
                "SQL"=>"SQL",
                "API"=>"API",
                "ZIP"=>"ZIP",
                _=>i==0
                    ?char.ToUpper(words[i][0],CultureInfo.GetCultureInfo("pt-BR"))+words[i][1..]
                    :words[i].ToLower(CultureInfo.GetCultureInfo("pt-BR"))
            };
        }
        return string.Join(' ',words);
    }

    static string E(string? value)=>WebUtility.HtmlEncode(value??"");
}
