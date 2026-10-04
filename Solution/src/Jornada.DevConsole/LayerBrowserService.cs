using System.Diagnostics;
using System.Text;

sealed record LayerPage(
    string Layer,
    int Page,
    int PageSize,
    long Total,
    int TotalPages,
    string Search,
    IReadOnlyList<string> Columns,
    IReadOnlyList<string?[]> Rows);

sealed record LayerDefinition(string Id,string FromClause,string OrderBy,string[] Columns,string[] Expressions);

sealed class LayerBrowserService(IWebHostEnvironment env)
{
    const int DefaultPageSize=50;
    const int MaxPageSize=100;

    static readonly LayerDefinition Gold=new(
        "gold",
        "gold.pessoa",
        "atualizado_em DESC,pessoa_uuid",
        ["pessoa_uuid","cpf","status_cpf","nome_completo","data_nascimento","nome_mae","fontes_distintas","estado_concordancia","estado_identidade","completude_nucleo","atualizado_em"],
        ["pessoa_uuid","cpf","status_cpf","nome_completo","data_nascimento","nome_mae","fontes_distintas","estado_concordancia","estado_identidade","completude_nucleo","atualizado_em"]);

    static readonly LayerDefinition Bronze=new(
        "bronze",
        "bronze.entrega_arquivo",
        "recebido_em DESC,entrega_id",
        ["entrega_id","nome_arquivo","content_type","objeto_chave","payload_sha256","tamanho_bytes","estado_armazenamento","recebido_em"],
        ["entrega_id","nome_arquivo","content_type","objeto_chave","payload_sha256","tamanho_bytes","estado_armazenamento","recebido_em"]);

    static readonly LayerDefinition Silver=new(
        "silver",
        "silver.pessoa_observacao po JOIN ingestao.lote l ON l.lote_id=po.lote_id JOIN ingestao.entrega e ON e.entrega_id=l.entrega_id",
        "po.source_as_of DESC,po.pessoa_observacao_id",
        ["entrega_id","pessoa_observacao_id","codigo_pessoa_origem","cpf","nome_completo","data_nascimento","nome_mae","source_as_of"],
        ["e.entrega_id","po.pessoa_observacao_id","po.codigo_pessoa_origem","po.cpf","po.nome_completo","po.data_nascimento","po.nome_mae","po.source_as_of"]);

    static readonly LayerDefinition Identity=new(
        "identity",
        "identidade.v_vinculo_corrente vc JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=vc.pessoa_observacao_id JOIN ingestao.lote l ON l.lote_id=po.lote_id",
        "vc.atualizado_em DESC,vc.pessoa_observacao_id",
        ["entrega_id","pessoa_observacao_id","status","metodo_resolucao","pessoa_uuid","linkage_run_id","atualizado_em"],
        ["l.entrega_id","vc.pessoa_observacao_id","vc.status","vc.metodo_resolucao","vc.pessoa_uuid","vc.linkage_run_id","vc.atualizado_em"]);

    public async Task<LayerPage> BrowseAsync(string layer,int page,int pageSize,string? search,CancellationToken ct)
    {
        var definition=(layer??string.Empty).Trim().ToLowerInvariant() switch
        {
            "gold"=>Gold,
            "bronze"=>Bronze,
            "silver"=>Silver,
            "identity"=>Identity,
            _=>throw new ArgumentException("Camada deve ser 'bronze', 'silver', 'identity' ou 'gold'.")
        };

        page=Math.Max(1,page);
        pageSize=pageSize<=0?DefaultPageSize:Math.Clamp(pageSize,10,MaxPageSize);
        var term=(search??string.Empty).Trim();
        if(term.Length>160)term=term[..160];

        var root=DevConsolePaths.FindSolutionRoot(env.ContentRootPath);
        var envFile=Path.Combine(root,".env.devconsole");
        if(!File.Exists(envFile))throw new InvalidOperationException(".env.devconsole ausente. Suba a infraestrutura DEV primeiro.");
        var vars=LoadEnv(envFile);
        var db=vars.TryGetValue("JORNADA_SQL_DATABASE",out var dbValue)&&!string.IsNullOrWhiteSpace(dbValue)?dbValue:"JornadaSyntheticDev";
        if(!vars.TryGetValue("JORNADA_SQL_SA_PASSWORD",out var password)||string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("JORNADA_SQL_SA_PASSWORD ausente.");

        var predicate=BuildPredicate(definition,term);
        var countSql=$"SET NOCOUNT ON; SELECT COUNT_BIG(*) FROM {definition.FromClause}{predicate};";
        var countLines=await RunSqlAsync(root,envFile,db,password,countSql,ct);
        var countText=countLines.LastOrDefault(x=>!string.IsNullOrWhiteSpace(x))?.Trim();
        if(!long.TryParse(countText,System.Globalization.NumberStyles.Integer,System.Globalization.CultureInfo.InvariantCulture,out var total))
            throw new InvalidOperationException($"Contagem da camada {definition.Id} inválida: {countText??"(vazia)"}.");

        var totalPages=Math.Max(1,(int)Math.Ceiling(total/(double)pageSize));
        page=Math.Min(page,totalPages);
        var offset=(page-1)*pageSize;
        var select=string.Join(",",definition.Expressions.Select(Clean));
        var dataSql=$"SET NOCOUNT ON; SELECT {select} FROM {definition.FromClause}{predicate} ORDER BY {definition.OrderBy} OFFSET {offset} ROWS FETCH NEXT {pageSize} ROWS ONLY;";
        var dataLines=await RunSqlAsync(root,envFile,db,password,dataSql,ct);
        var rows=dataLines
            .Where(x=>!string.IsNullOrWhiteSpace(x))
            .Select(x=>x.Split('|',StringSplitOptions.None).Select(v=>string.IsNullOrEmpty(v)?null:v).ToArray())
            .ToArray();

        if(rows.Any(x=>x.Length!=definition.Columns.Length))
            throw new InvalidOperationException($"Saída inesperada ao ler a camada {definition.Id}.");

        return new LayerPage(definition.Id,page,pageSize,total,totalPages,term,definition.Columns,rows);
    }

    static Dictionary<string,string> LoadEnv(string path)
    {
        return File.ReadAllLines(path)
            .Select(x=>x.Trim())
            .Where(x=>x.Length>0&&!x.StartsWith('#')&&x.Contains('='))
            .Select(x=>x.Split('=',2))
            .ToDictionary(x=>x[0].Trim(),x=>x[1].Trim(),StringComparer.OrdinalIgnoreCase);
    }

    static string BuildPredicate(LayerDefinition definition,string term)
    {
        if(string.IsNullOrWhiteSpace(term))return string.Empty;
        var concat="CONCAT_WS(N'|',"+string.Join(",",definition.Expressions.Select(x=>$"COALESCE(CONVERT(nvarchar(max),{x}),N'')"))+")";
        var escaped=EscapeLike(term);
        return $" WHERE {concat} COLLATE Latin1_General_100_CI_AI LIKE N'%{escaped}%' ESCAPE N'~'";
    }

    static string EscapeLike(string value)=>value
        .Replace("'","''",StringComparison.Ordinal)
        .Replace("~","~~",StringComparison.Ordinal)
        .Replace("%","~%",StringComparison.Ordinal)
        .Replace("_","~_",StringComparison.Ordinal)
        .Replace("[","~[",StringComparison.Ordinal);

    static string Clean(string expression)=>
        $"REPLACE(REPLACE(REPLACE(COALESCE(CONVERT(nvarchar(max),{expression}),N''),N'|',N' '),NCHAR(13),N' '),NCHAR(10),N' ')";

    static async Task<string[]> RunSqlAsync(string root,string envFile,string db,string password,string query,CancellationToken ct)
    {
        var psi=new ProcessStartInfo("docker")
        {
            WorkingDirectory=root,
            RedirectStandardOutput=true,
            RedirectStandardError=true,
            StandardOutputEncoding=Encoding.UTF8,
            StandardErrorEncoding=Encoding.UTF8,
            UseShellExecute=false,
            CreateNoWindow=true
        };
        psi.Environment["SQLCMDPASSWORD"]=password;
        foreach(var arg in new[]{
            "compose","--env-file",envFile,"exec","-T","-e","SQLCMDPASSWORD","sqlserver",
            "/opt/mssql-tools18/bin/sqlcmd","-S","localhost","-U","sa","-C","-b","-I","-d",db,
            "-W","-h","-1","-s","|","-w","65535","-Q",query
        })psi.ArgumentList.Add(arg);

        using var process=new Process{StartInfo=psi};
        process.Start();
        var stdoutTask=process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask=process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        var stdout=await stdoutTask;
        var stderr=await stderrTask;
        if(process.ExitCode!=0)throw new InvalidOperationException($"Falha ao consultar camada DEV: {stderr.Trim()}");
        return stdout.Split(["\r\n","\n"],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
    }
}
