using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

sealed record CommandDefinition(string Id,string Title,string Description,string? File,string? Arguments,string? ResultPath,string[] Dependencies,string? DependencyNote)
{
    public bool Implemented=>File is not null||Id is "zip" or "semiblind";
    public string? CommandLine=>File is null?null:$"{File} {Arguments}";
    public string DisplayCommand=>Id switch{
        "zip"=>"Entrada manual → python scripts/build-ingestion-fixture.py",
        "semiblind"=>"POST /api/v1/identidade/candidatos (DEV sintético)",
        _=>CommandLine??"Operação parametrizada pela interface."
    };
}

sealed record StepResult(string Command,string WorkingDirectory,int ExitCode,long DurationMs,string Output,string Error,string? ResultPath,IReadOnlyList<string>? Artifacts=null);
sealed record ManualZipRequest(string Gestor,string ManifestJson,string PessoasJsonl,string RegistrosJsonl);
sealed record RunRecord(Guid Id,string Command,string Title,DateTimeOffset StartedAt,DateTimeOffset FinishedAt,string Status,string Summary,StepResult Step,IReadOnlyList<Dictionary<string,string?>> Records);
sealed record RunSummary(Guid Id,string Command,string Title,DateTimeOffset StartedAt,DateTimeOffset FinishedAt,string Status,string Summary);
sealed record ConsoleEvent(long Seq,DateTimeOffset At,string Stream,string Text);

static class DevConsoleJson
{
    public static readonly JsonSerializerOptions Pretty=new(JsonSerializerDefaults.Web){WriteIndented=true};
    public static readonly JsonSerializerOptions Compact=new(JsonSerializerDefaults.Web);
}

static class CommandCatalog
{
    // Cada ação é independente. Quando necessário, o próprio comando garante suas dependências locais.
    public static readonly CommandDefinition[] All=[
        new("infrastructure","Subir infraestrutura e referências","Sobe o ambiente DEV completo: Docker, SQL Server, schema, NAS, bootstrap IBGE e NODE1/NODE2. Também gera a configuração inicial em JSON e HTML.","pwsh","-NoProfile -File scripts/dev-console-infrastructure.ps1 -Action up",null,[],null),
        new("initial-config","Gerar/ver configuração inicial","Regenera a configuração inicial da Console DEV em JSON e HTML, inclui o estado/health atual dos serviços e mostra os caminhos produzidos.","pwsh","-NoProfile -File scripts/dev-console-initial-config.ps1",".local/dev-console/initial-config/configuration.json",["infrastructure"],"A subida da infraestrutura já gera estes arquivos automaticamente; este item também substitui a antiga consulta separada de status/health."),
        new("reference-check","Validar referência IBGE","Executa o quick check read-only da referência IBGE já materializada. O bootstrap/carga faz parte da infraestrutura básica.","pwsh","-NoProfile -File scripts/dev-console-operations.ps1 -Action reference-check",null,["infrastructure"],"A infraestrutura é subida automaticamente se necessário."),
        new("gold-synthetic","Carregar Gold sintética (30.000)","Materializa a Gold exclusivamente sintética da Console DEV com 30.000 pessoas; nomes e sobrenomes seguem a frequência pública IBGE versionada.","pwsh","-NoProfile -File scripts/dev-console-gold-synthetic.ps1",".local/dev-console/gold-synthetic-records.json",["infrastructure"],"Na Console DEV a Gold é sempre sintética. Não existe fallback para Gold real."),
        new("initial-calibration","Calibração inicial a partir da Gold","Gera e ativa o primeiro modelo de linkage a partir da Gold sintética da Console DEV. Recusa execução se a Gold estiver vazia ou se já houver modelo ATIVO.","pwsh","-NoProfile -File scripts/dev-console-operations.ps1 -Action calibrate-initial",".local/dev-console/initial-calibration.json",["infrastructure","gold-synthetic"],"Exige a Gold sintética de 30.000 pessoas e a referência IBGE ativa."),
        new("contract-bundle","Gerar bundle de contratos e configurações","Gera um ZIP operacional sem binários com OpenAPI, contratos JSON, configurações governadas e metadados do modelo ATIVO.","pwsh","-NoProfile -File scripts/dev-console-contract-bundle.ps1",".local/dev-console/contract-config-bundle.zip",["initial-calibration"],"Exige modelo ATIVO para vincular o bundle ao fingerprint/configuração efetivamente calibrados."),
        new("zip","Gerar ZIP de ingestão","Abre a entrada manual e gera o ZIP real de ingestão.",null,null,null,["contract-bundle"],"A geração é local; o envio para a API só é permitido depois de existir o bundle de contratos/configurações."),
        new("ingestion","Enviar último ZIP para ingestão","Envia o último ZIP manual para a API real em NODE1 usando a credencial sintética DEV correspondente ao Gestor.","pwsh","-NoProfile -File scripts/dev-console-operations.ps1 -Action ingest-latest",".local/dev-console/last-ingestion.json",["contract-bundle","zip"],"Falha fechado se o bundle de contratos/configurações ainda não tiver sido gerado."),
        new("pipeline-status","Ver status da última ingestão","Consulta o recibo da última Entrega. Bronze, Silver, identidade e Gold são processados pelo Processor residente.","pwsh","-NoProfile -File scripts/dev-console-operations.ps1 -Action pipeline-status",".local/dev-console/last-ingestion-status.json",["ingestion"],null),
        new("bronze-verify","Verificar Bronze","Executa Jornada.Bronze.Verify no NODE2 contra as referências Bronze persistidas.","pwsh","-NoProfile -File scripts/dev-console-operations.ps1 -Action bronze-verify",null,["ingestion"],"Pode ser executado antes, mas só terá conteúdo útil depois de uma ingestão."),
        new("blocking","Reconstruir blocking","Executa a reconstrução one-shot da projeção local de blocking sem recompilar nada.","pwsh","-NoProfile -File scripts/dev-console-operations.ps1 -Action blocking",null,["gold-synthetic"],"Requer Gold disponível; pode ser Gold sintética ou real."),
        new("calibration","Recalibrar e ativar","Executa novo ciclo GENERATE_DRAFT → conferência → VALIDATE → ACTIVATE sobre uma Gold já existente.","pwsh","-NoProfile -File scripts/dev-console-operations.ps1 -Action calibrate",null,["initial-calibration"],"Use após a calibração inicial quando quiser gerar uma nova versão do modelo."),
        new("linkage","Executar linkage","Executa o Jornada.Linkage.Runner real no NODE2 usando o modelo calibrado ATIVO.","pwsh","-NoProfile -File scripts/dev-console-operations.ps1 -Action linkage",null,["initial-calibration"],"Aceita o modelo inicial ou uma recalibração posterior, desde que exista modelo ATIVO."),
        new("semiblind","Consulta semicega","Consulta até cinco candidatos pela API real sem expor CPF, UUID ou score. O formulário usa uma pessoa sintética da Gold como exemplo.",null,null,null,["gold-synthetic","initial-calibration"],"Disponível somente no banco isolado JornadaSyntheticDev com a feature DEV habilitada."),
        new("replay","Executar replay do último run","Executa REPLAY real do último linkage PUBLICADO elegível, sem publicar o resultado.","pwsh","-NoProfile -File scripts/dev-console-operations.ps1 -Action replay-latest",null,["linkage"],"Exige pelo menos um linkage PUBLICADO não-REPLAY."),
        new("report","Diagnóstico do último linkage","Executa o diagnóstico real do último linkage publicado, incluindo modelo, thresholds, cobertura e qualidade sintética.","pwsh","-NoProfile -File scripts/dev-console-operations.ps1 -Action report",null,["linkage"],null),
        new("finish","Finalizar e limpar ambiente","Encerra o cluster e remove containers, volumes e órfãos locais. Na próxima subida tudo é recriado automaticamente.","pwsh","-NoProfile -File scripts/dev-console-infrastructure.ps1 -Action clean",null,[],null)
    ];}

static class DevConsolePaths
{
    public static string FindSolutionRoot(string start)
    {
        for(var d=new DirectoryInfo(start);d is not null;d=d.Parent)
            if(File.Exists(Path.Combine(d.FullName,"Jornada.sln")))return d.FullName;
        throw new DirectoryNotFoundException("Jornada.sln não encontrado.");
    }
}

sealed class LiveExecution
{
    readonly object gate=new();
    readonly List<ConsoleEvent> events=[];
    long seq;
    public Guid Id{get;}
    public bool Completed{get;private set;}
    public LiveExecution(Guid id)=>Id=id;

    public void Add(string stream,string text)
    {
        lock(gate)events.Add(new ConsoleEvent(++seq,DateTimeOffset.UtcNow,stream,text));
    }

    public IReadOnlyList<ConsoleEvent> After(long after)
    {
        lock(gate)return events.Where(x=>x.Seq>after).ToArray();
    }

    public void Complete()
    {
        lock(gate)Completed=true;
    }
}

sealed record ZipTemplate(
    string Source,
    string PessoaUuid,
    string Gestor,
    string CodigoSistemaOrigem,
    string CodigoTipo,
    string NomeCompleto,
    string DataNascimento,
    string NomeMae,
    string IdPessoaEntrega,
    string CodigoRegistroOrigem,
    string ManifestJson,
    string PessoasJsonl,
    string RegistrosJsonl);

sealed class GoldZipTemplateService(IWebHostEnvironment env)
{
    public async Task<ZipTemplate> GetAsync(CancellationToken ct)
    {
        var root=DevConsolePaths.FindSolutionRoot(env.ContentRootPath);
        var envFile=Path.Combine(root,".env.devconsole");
        if(!File.Exists(envFile))throw new InvalidOperationException(".env.devconsole ausente. Suba a infraestrutura DEV primeiro.");
        var vars=File.ReadAllLines(envFile)
            .Select(x=>x.Trim())
            .Where(x=>x.Length>0&&!x.StartsWith('#')&&x.Contains('='))
            .Select(x=>x.Split('=',2))
            .ToDictionary(x=>x[0].Trim(),x=>x[1].Trim(),StringComparer.OrdinalIgnoreCase);
        var db=vars.TryGetValue("JORNADA_SQL_DATABASE",out var dbValue)&&!string.IsNullOrWhiteSpace(dbValue)?dbValue:"JornadaSyntheticDev";
        if(!string.Equals(db,"JornadaSyntheticDev",StringComparison.Ordinal))throw new InvalidOperationException($"Console DEV exige JornadaSyntheticDev; banco atual={db}.");
        if(!vars.TryGetValue("JORNADA_SQL_SA_PASSWORD",out var password)||string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("JORNADA_SQL_SA_PASSWORD ausente.");

        var query="SET NOCOUNT ON; SELECT TOP(1) CONVERT(varchar(36),pessoa_uuid),REPLACE(REPLACE(nome_completo,'|',' '),CHAR(10),' '),CONVERT(varchar(10),data_nascimento,23),REPLACE(REPLACE(nome_mae,'|',' '),CHAR(10),' ') FROM gold.pessoa WHERE nome_completo IS NOT NULL AND data_nascimento IS NOT NULL AND nome_mae IS NOT NULL ORDER BY atualizado_em DESC,pessoa_uuid;";
        var psi=new ProcessStartInfo("docker"){WorkingDirectory=root,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8,UseShellExecute=false,CreateNoWindow=true};
        psi.Environment["SQLCMDPASSWORD"]=password;
        foreach(var arg in new[]{"compose","--env-file",envFile,"exec","-T","-e","SQLCMDPASSWORD","sqlserver","/opt/mssql-tools18/bin/sqlcmd","-S","localhost","-U","sa","-C","-b","-I","-d",db,"-W","-h","-1","-s","|","-w","65535","-Q",query})psi.ArgumentList.Add(arg);
        using var process=new Process{StartInfo=psi};
        process.Start();
        var stdoutTask=process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask=process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        var stdout=(await stdoutTask).Trim();
        var stderr=await stderrTask;
        if(process.ExitCode!=0)throw new InvalidOperationException($"Falha ao consultar Gold para exemplo do ZIP: {stderr.Trim()}");
        var parts=stdout.Split('|',StringSplitOptions.TrimEntries);
        if(parts.Length<4)throw new InvalidOperationException("Gold sintética não possui Pessoa completa para montar o exemplo.");
        var uuid=parts[0];
        var nome=parts[1];
        var nascimento=parts[2];
        var mae=parts[3];
        if(string.IsNullOrWhiteSpace(uuid)||string.IsNullOrWhiteSpace(nome)||string.IsNullOrWhiteSpace(nascimento)||string.IsNullOrWhiteSpace(mae))
            throw new InvalidOperationException("Gold retornou Pessoa incompleta para o exemplo.");

        var suffix=uuid.Replace("-","",StringComparison.Ordinal).ToUpperInvariant()[..8];
        var pessoaId=$"DEV-GOLD-{suffix}";
        var registroId=$"DEV-GOLD-REG-{suffix}";
        var gestor="SEHAB";
        var sistema="SEHAB";
        var tipo="AA01";
        var today=DateTime.Today;
        var manifest=new Dictionary<string,object?>{
            ["formatoVersao"]=2,["pessoaSchemaVersao"]=4,["codigoSistemaOrigem"]=sistema,["natureza"]="BENEFICIO",["codigoTipo"]=tipo,["tipoVersao"]=1,
            ["dataReferencia"]=DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz",System.Globalization.CultureInfo.InvariantCulture)
        };
        var pessoa=new Dictionary<string,object?>{
            ["idPessoaEntrega"]=pessoaId,["cpf"]=null,["cpfAusenteMotivo"]="NAO_INFORMADO_ORIGEM",["nomeCompleto"]=nome,["dataNascimento"]=nascimento,["nomeMae"]=mae,
            ["sourceTransactionId"]=$"DEV-GOLD-TX-{suffix}",["atributosTransversais"]=Array.Empty<object>()
        };
        var registro=new Dictionary<string,object?>{
            ["idPessoaEntrega"]=pessoaId,["codigoRegistroOrigem"]=registroId,["operacao"]="INCLUSAO",["dataInicioConcessao"]=today.AddDays(-30).ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture),
            ["valorConcedido"]=600.0m,["dataEventoConcessao"]=today.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture),["situacaoVigencia"]="VIGENTE"
        };
        return new ZipTemplate(
            "gold.pessoa",uuid,gestor,sistema,tipo,nome,nascimento,mae,pessoaId,registroId,
            JsonSerializer.Serialize(manifest,DevConsoleJson.Pretty),
            JsonSerializer.Serialize(pessoa,DevConsoleJson.Compact),
            JsonSerializer.Serialize(registro,DevConsoleJson.Compact));
    }
}

sealed record SemiblindDevRequest(string Gestor,string? NomeCompleto,string? DataNascimento,string? NomeMae);
sealed record SemiblindDevResponse(int StatusCode,string Json);

sealed class SemiblindDevService(IWebHostEnvironment env,GoldZipTemplateService gold)
{
    public async Task<ZipTemplate> TemplateAsync(CancellationToken ct)=>await gold.GetAsync(ct);

    public async Task<SemiblindDevResponse> SearchAsync(SemiblindDevRequest request,CancellationToken ct)
    {
        var root=DevConsolePaths.FindSolutionRoot(env.ContentRootPath);
        var gestor=string.IsNullOrWhiteSpace(request.Gestor)?"SEHAB":request.Gestor.Trim().ToUpperInvariant();
        var keysPath=Path.Combine(root,"config","security","test-access-keys.json");
        using var keysDoc=JsonDocument.Parse(await File.ReadAllTextAsync(keysPath,ct));
        var credential=keysDoc.RootElement.GetProperty("credentials").EnumerateArray()
            .FirstOrDefault(x=>
                string.Equals(x.GetProperty("type").GetString(),"GESTOR",StringComparison.Ordinal)&&
                string.Equals(x.GetProperty("publicCode").GetString(),gestor,StringComparison.Ordinal)&&
                x.GetProperty("scopes").EnumerateArray().Any(s=>string.Equals(s.GetString(),"jornada.identidade.busca.read",StringComparison.Ordinal)));
        if(credential.ValueKind==JsonValueKind.Undefined)throw new InvalidOperationException($"Credencial sintética DEV para {gestor} sem scope jornada.identidade.busca.read.");

        var accessKey=credential.GetProperty("accessKey").GetString()??throw new InvalidOperationException("Access key DEV ausente.");
        var body=new Dictionary<string,object?>();
        if(!string.IsNullOrWhiteSpace(request.NomeCompleto))body["nome_completo"]=request.NomeCompleto.Trim();
        if(!string.IsNullOrWhiteSpace(request.DataNascimento))body["data_nascimento"]=request.DataNascimento.Trim();
        if(!string.IsNullOrWhiteSpace(request.NomeMae))body["nome_mae"]=request.NomeMae.Trim();

        using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(30)};
        using var message=new HttpRequestMessage(HttpMethod.Post,"http://127.0.0.1:5080/api/v1/identidade/candidatos");
        message.Headers.Add("X-Jornada-Gestor",gestor);
        message.Headers.Add("X-Jornada-Access-Key",accessKey);
        message.Content=new StringContent(JsonSerializer.Serialize(body,DevConsoleJson.Compact),Encoding.UTF8,"application/json");
        using var response=await http.SendAsync(message,ct);
        var json=await response.Content.ReadAsStringAsync(ct);
        if(string.IsNullOrWhiteSpace(json))json="{}";
        return new SemiblindDevResponse((int)response.StatusCode,json);
    }
}

sealed class LiveExecutionService(IWebHostEnvironment env,RunStore store)
{
    static readonly JsonSerializerOptions StreamJson=new(JsonSerializerDefaults.Web);
    readonly ConcurrentDictionary<Guid,LiveExecution> active=new();

    public bool Contains(Guid id)=>active.ContainsKey(id);

    public Guid StartCommand(CommandDefinition definition)
    {
        var id=Guid.NewGuid();
        var live=new LiveExecution(id);
        if(!active.TryAdd(id,live))throw new InvalidOperationException("Não foi possível registrar a execução.");
        _=Task.Run(()=>RunCommandAsync(id,definition,live));
        return id;
    }

    public Guid StartManualZip(ManualZipRequest request)
    {
        var id=Guid.NewGuid();
        var live=new LiveExecution(id);
        if(!active.TryAdd(id,live))throw new InvalidOperationException("Não foi possível registrar a execução.");
        _=Task.Run(()=>RunManualZipAsync(id,request,live));
        return id;
    }

    public async Task StreamAsync(Guid id,HttpResponse response,CancellationToken ct)
    {
        if(!active.TryGetValue(id,out var live))return;
        long cursor=0;
        while(!ct.IsCancellationRequested)
        {
            var batch=live.After(cursor);
            foreach(var item in batch)
            {
                cursor=item.Seq;
                var json=JsonSerializer.Serialize(item,StreamJson);
                await response.WriteAsync($"id: {item.Seq}\ndata: {json}\n\n",ct);
            }
            if(batch.Count>0)await response.Body.FlushAsync(ct);
            if(live.Completed&&batch.Count==0)break;
            await Task.Delay(120,ct);
        }
    }

    async Task RunCommandAsync(Guid id,CommandDefinition definition,LiveExecution live)
    {
        var root=DevConsolePaths.FindSolutionRoot(env.ContentRootPath);
        var started=DateTimeOffset.UtcNow;
        live.Add("system",$"Execução {id:N}");
        live.Add("system",$"Diretório: {root}");
        live.Add("command",$"> {definition.DisplayCommand}");

        if(!definition.Implemented||definition.File is null)
        {
            live.Add("stderr","SEM EXECUTOR: operação sem executor configurado.");
            var step=new StepResult(definition.DisplayCommand,root,-1,0,"","Operação sem executor configurado.",null);
            await FinishAsync(new RunRecord(id,definition.Id,definition.Title,started,DateTimeOffset.UtcNow,"SEM EXECUTOR","Opção disponível; comando real ainda não mapeado.",step,Array.Empty<Dictionary<string,string?>>()),live);
            return;
        }

        var sw=Stopwatch.StartNew();
        try
        {
            var result=await RunProcessAsync(definition.File,definition.Arguments!,root,live);
            sw.Stop();
            var candidatePath=definition.ResultPath is null?null:Path.GetFullPath(Path.Combine(root,definition.ResultPath));
            var resultPath=result.ExitCode==0&&candidatePath is not null&&File.Exists(candidatePath)?candidatePath:null;
            var records=resultPath is null?Array.Empty<Dictionary<string,string?>>():await LoadRecordsAsync(definition.ResultPath,root);
            var artifacts=ParseArtifacts(result.Output,root);
            if(resultPath is not null)live.Add("result",$"Resultado: {resultPath}");
            var summary=records.Count>0
                ?$"{records.Count} registro(s) no resultado. Resultado: {resultPath}"
                :result.ExitCode==0
                    ?(resultPath is null?"Comando concluído.":$"Comando concluído. Resultado: {resultPath}")
                    :$"Comando falhou (exit {result.ExitCode}).";
            var step=new StepResult(definition.CommandLine!,root,result.ExitCode,sw.ElapsedMilliseconds,result.Output,result.Error,resultPath,artifacts);
            var status=result.ExitCode==0?"SUCESSO":"FALHA";
            live.Add("status",$"{status} · {(sw.ElapsedMilliseconds/1000d):0.00}s");
            await FinishAsync(new RunRecord(id,definition.Id,definition.Title,started,DateTimeOffset.UtcNow,status,summary,step,records),live);
        }
        catch(Exception ex)
        {
            sw.Stop();
            live.Add("stderr",ex.ToString());
            live.Add("status",$"FALHA · {(sw.ElapsedMilliseconds/1000d):0.00}s");
            var step=new StepResult(definition.CommandLine!,root,-1,sw.ElapsedMilliseconds,"",ex.ToString(),null);
            await FinishAsync(new RunRecord(id,definition.Id,definition.Title,started,DateTimeOffset.UtcNow,"FALHA","Falha inesperada; veja o console.",step,Array.Empty<Dictionary<string,string?>>()),live);
        }
    }

    async Task RunManualZipAsync(Guid id,ManualZipRequest request,LiveExecution live)
    {
        var root=DevConsolePaths.FindSolutionRoot(env.ContentRootPath);
        var started=DateTimeOffset.UtcNow;
        var sw=Stopwatch.StartNew();
        var command="python scripts/build-ingestion-fixture.py";
        live.Add("system",$"Execução {id:N}");
        live.Add("system",$"Diretório: {root}");
        try
        {
            if(string.IsNullOrWhiteSpace(request.Gestor))throw new ArgumentException("Gestor é obrigatório.");
            live.Add("stdout","Validando manifest.json...");
            using(var manifest=JsonDocument.Parse(request.ManifestJson)){}
            var pessoas=SplitJsonl(request.PessoasJsonl).ToArray();
            var registros=SplitJsonl(request.RegistrosJsonl).ToArray();
            live.Add("stdout",$"Validando pessoas.jsonl: {pessoas.Length} linha(s)...");
            foreach(var line in pessoas)using(var doc=JsonDocument.Parse(line)){}
            live.Add("stdout",$"Validando registros.jsonl: {registros.Length} linha(s)...");
            foreach(var line in registros)using(var doc=JsonDocument.Parse(line)){}

            var work=Path.Combine(root,".local","dev-console","manual-zip",id.ToString("N"));
            Directory.CreateDirectory(work);
            await File.WriteAllTextAsync(Path.Combine(work,"manifest.json"),request.ManifestJson.Trim()+Environment.NewLine);
            await File.WriteAllTextAsync(Path.Combine(work,"pessoas.jsonl"),NormalizeJsonl(request.PessoasJsonl));
            await File.WriteAllTextAsync(Path.Combine(work,"registros.jsonl"),NormalizeJsonl(request.RegistrosJsonl));
            live.Add("result",$"Entrada gravada em: {work}");

            var python=OperatingSystem.IsWindows()?"python":"python3";
            var psi=new ProcessStartInfo(python){WorkingDirectory=root,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8,UseShellExecute=false,CreateNoWindow=true};
            psi.ArgumentList.Add(Path.Combine(root,"scripts","build-ingestion-fixture.py"));
            psi.ArgumentList.Add("--fixture");psi.ArgumentList.Add(work);
            psi.ArgumentList.Add("--gestor");psi.ArgumentList.Add(request.Gestor.Trim());
            psi.ArgumentList.Add("--output-dir");psi.ArgumentList.Add(work);
            command=$"{python} scripts/build-ingestion-fixture.py --fixture \"{work}\" --gestor {request.Gestor.Trim()} --output-dir \"{work}\"";
            live.Add("command",$"> {command}");
            var result=await RunProcessAsync(psi,live);
            sw.Stop();
            var output=result.Output.Trim();
            var zip=result.ExitCode==0&&File.Exists(output)?Path.GetFullPath(output):null;
            if(zip is not null)live.Add("result",$"ZIP gerado: {zip}");
            var status=result.ExitCode==0?"SUCESSO":"FALHA";
            live.Add("status",$"{status} · {(sw.ElapsedMilliseconds/1000d):0.00}s");
            var summary=result.ExitCode==0?$"ZIP gerado. Resultado: {zip}":$"Falha ao gerar ZIP (exit {result.ExitCode}).";
            var step=new StepResult(command,root,result.ExitCode,sw.ElapsedMilliseconds,result.Output,result.Error,zip,zip is null?Array.Empty<string>():new[]{zip});
            await FinishAsync(new RunRecord(id,"zip","Gerar ZIP de ingestão",started,DateTimeOffset.UtcNow,status,summary,step,Array.Empty<Dictionary<string,string?>>()),live);
        }
        catch(Exception ex)
        {
            sw.Stop();
            live.Add("stderr",ex.ToString());
            live.Add("status",$"FALHA · {(sw.ElapsedMilliseconds/1000d):0.00}s");
            var step=new StepResult(command,root,-1,sw.ElapsedMilliseconds,"",ex.ToString(),null);
            await FinishAsync(new RunRecord(id,"zip","Gerar ZIP de ingestão",started,DateTimeOffset.UtcNow,"FALHA","Falha ao gerar ZIP; veja o console.",step,Array.Empty<Dictionary<string,string?>>()),live);
        }
    }

    async Task FinishAsync(RunRecord run,LiveExecution live)
    {
        await store.SaveAsync(run,CancellationToken.None);
        live.Complete();
    }

    static async Task<IReadOnlyList<Dictionary<string,string?>>> LoadRecordsAsync(string? relative,string root)
    {
        var records=new List<Dictionary<string,string?>>();
        if(relative is null)return records;
        var path=Path.Combine(root,relative);
        if(!File.Exists(path))return records;
        using var doc=JsonDocument.Parse(await File.ReadAllTextAsync(path));
        if(doc.RootElement.ValueKind!=JsonValueKind.Array)return records;
        foreach(var row in doc.RootElement.EnumerateArray())
            records.Add(row.EnumerateObject().ToDictionary(x=>x.Name,x=>(string?)x.Value.ToString()));
        return records;
    }

    static async Task<ProcessCapture> RunProcessAsync(string file,string arguments,string root,LiveExecution live)
    {
        var psi=new ProcessStartInfo(file,arguments){WorkingDirectory=root,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8,UseShellExecute=false,CreateNoWindow=true};
        return await RunProcessAsync(psi,live);
    }

    static async Task<ProcessCapture> RunProcessAsync(ProcessStartInfo psi,LiveExecution live)
    {
        using var process=new Process{StartInfo=psi};
        process.Start();
        var stdout=new StringBuilder();
        var stderr=new StringBuilder();
        var started=Stopwatch.StartNew();
        var lastOutputTicks=Stopwatch.GetTimestamp();

        async Task PumpAsync(StreamReader reader,StringBuilder sink,string stream)
        {
            while(await reader.ReadLineAsync() is { } line)
            {
                sink.AppendLine(line);
                Volatile.Write(ref lastOutputTicks,Stopwatch.GetTimestamp());
                live.Add(stream,line);
            }
        }

        async Task HeartbeatAsync()
        {
            while(!process.HasExited)
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                if(process.HasExited)break;
                var silent=Stopwatch.GetElapsedTime(Volatile.Read(ref lastOutputTicks));
                if(silent<TimeSpan.FromSeconds(5))continue;
                live.Add("system",$"⏳ Processo ativo há {started.Elapsed.TotalSeconds:0}s; sem nova saída há {silent.TotalSeconds:0}s. Aguardando...");
            }
        }

        var outTask=PumpAsync(process.StandardOutput,stdout,"stdout");
        var errTask=PumpAsync(process.StandardError,stderr,"stderr");
        var heartbeatTask=HeartbeatAsync();
        await Task.WhenAll(outTask,errTask,process.WaitForExitAsync());
        await heartbeatTask;
        return new ProcessCapture(process.ExitCode,stdout.ToString(),stderr.ToString());
    }

    static IReadOnlyList<string> ParseArtifacts(string output,string root)
    {
        var items=new List<string>();
        foreach(var line in output.Replace("\r","").Split('\n',StringSplitOptions.RemoveEmptyEntries))
        {
            const string marker="ARTEFATO:";
            var index=line.IndexOf(marker,StringComparison.OrdinalIgnoreCase);
            if(index<0)continue;
            var raw=line[(index+marker.Length)..].Trim().Trim('"');
            if(string.IsNullOrWhiteSpace(raw))continue;
            var full=Path.IsPathRooted(raw)?Path.GetFullPath(raw):Path.GetFullPath(Path.Combine(root,raw));
            if(File.Exists(full)&&!items.Contains(full,StringComparer.OrdinalIgnoreCase))items.Add(full);
        }
        return items;
    }

    static IEnumerable<string> SplitJsonl(string text)=>text.Replace("\r","").Split('\n',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
    static string NormalizeJsonl(string text)=>string.Join(Environment.NewLine,SplitJsonl(text))+(string.IsNullOrWhiteSpace(text)?"":Environment.NewLine);
    sealed record ProcessCapture(int ExitCode,string Output,string Error);
}

sealed class RunStore(IWebHostEnvironment env)
{
    readonly string root=Path.Combine(env.ContentRootPath,".runs");
    readonly DateTimeOffset sessionStartedAt=DateTimeOffset.UtcNow;
    static readonly JsonSerializerOptions Opt=new(JsonSerializerDefaults.Web){WriteIndented=true};

    public async Task SaveAsync(RunRecord run,CancellationToken ct)
    {
        Directory.CreateDirectory(root);
        var target=Path.Combine(root,$"{run.Id:N}.json");
        var temp=target+".tmp";
        await File.WriteAllTextAsync(temp,JsonSerializer.Serialize(run,Opt),ct);
        File.Move(temp,target,true);
    }

    public async Task<RunRecord?> GetAsync(Guid id,CancellationToken ct)
    {
        var path=Path.Combine(root,$"{id:N}.json");
        if(!File.Exists(path))return null;
        try{return JsonSerializer.Deserialize<RunRecord>(await File.ReadAllTextAsync(path,ct),Opt);}
        catch(JsonException){return null;}
    }

    public async Task<IReadOnlyList<RunSummary>> ListSummariesAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(root);
        var xs=new List<RunSummary>();
        foreach(var path in Directory.EnumerateFiles(root,"*.json")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Take(200))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await using var stream=File.OpenRead(path);
                using var doc=await JsonDocument.ParseAsync(stream,cancellationToken:ct);
                var e=doc.RootElement;
                if(!e.TryGetProperty("id",out var idElement)||!Guid.TryParse(idElement.GetString(),out var id))continue;
                var command=e.TryGetProperty("command",out var commandElement)?commandElement.GetString()??"":"";
                var title=e.TryGetProperty("title",out var titleElement)?titleElement.GetString()??command:command;
                var status=e.TryGetProperty("status",out var statusElement)?statusElement.GetString()??"":"";
                var summary=e.TryGetProperty("summary",out var summaryElement)?summaryElement.GetString()??"":"";
                var started=e.TryGetProperty("startedAt",out var startedElement)&&startedElement.TryGetDateTimeOffset(out var startedAt)?startedAt:File.GetCreationTimeUtc(path);
                var finished=e.TryGetProperty("finishedAt",out var finishedElement)&&finishedElement.TryGetDateTimeOffset(out var finishedAt)?finishedAt:started;
                xs.Add(new RunSummary(id,command,title,started,finished,status,summary));
            }
            catch(JsonException){}
            catch(IOException){}
            catch(UnauthorizedAccessException){}
        }
        return xs;
    }

    public async Task<IReadOnlyDictionary<string,int>> CountByCommandAsync(CancellationToken ct)=>
        (await ListSummariesAsync(ct)).Where(x=>x.StartedAt>=sessionStartedAt)
            .GroupBy(x=>x.Command,StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x=>x.Key,x=>x.Count(),StringComparer.OrdinalIgnoreCase);
}
