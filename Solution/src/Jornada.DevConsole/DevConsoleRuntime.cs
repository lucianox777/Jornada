using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

sealed record CommandDefinition(string Id,string Title,string Description,string? File,string? Arguments,string? ResultPath,string[] Dependencies,string? DependencyNote)
{
    public bool Implemented=>File is not null||Id=="zip";
    public string? CommandLine=>File is null?null:$"{File} {Arguments}";
    public string DisplayCommand=>Id=="zip"
        ?"Entrada manual → python scripts/build-ingestion-fixture.py"
        :CommandLine??"Operação parametrizada pela interface.";
}

sealed record StepResult(string Command,string WorkingDirectory,int ExitCode,long DurationMs,string Output,string Error,string? ResultPath,IReadOnlyList<string>? Artifacts=null);
sealed record ManualZipRequest(string Gestor,string ManifestJson,string PessoasJsonl,string RegistrosJsonl);
sealed record RunRecord(Guid Id,string Command,string Title,DateTimeOffset StartedAt,DateTimeOffset FinishedAt,string Status,string Summary,StepResult Step,IReadOnlyList<Dictionary<string,string?>> Records);
sealed record ConsoleEvent(long Seq,DateTimeOffset At,string Stream,string Text);

static class CommandCatalog
{
    // Cada ação é independente. Quando necessário, o próprio comando garante suas dependências locais.
    public static readonly CommandDefinition[] All=[
        new("infrastructure","Subir infraestrutura e referências","Sobe o ambiente DEV completo: Docker, SQL Server, schema, NAS, bootstrap IBGE e NODE1/NODE2. Também gera a configuração inicial em JSON e HTML.","pwsh","-NoProfile -File scripts/dev-console-infrastructure.ps1 -Action up",null,[],null),
        new("initial-config","Gerar/ver configuração inicial","Regenera a configuração inicial da Console DEV em JSON e HTML e mostra os caminhos dos arquivos produzidos.","pwsh","-NoProfile -File scripts/dev-console-initial-config.ps1",".local/dev-console/initial-config/configuration.json",["infrastructure"],"A subida da infraestrutura já gera estes arquivos automaticamente; use este item para regenerar ou visualizar."),
        new("reference-check","Validar referência IBGE","Executa o quick check read-only da referência IBGE já materializada. O bootstrap/carga faz parte da infraestrutura básica.","pwsh","-NoProfile -File scripts/dev-console-operations.ps1 -Action reference-check",null,["infrastructure"],"A infraestrutura é subida automaticamente se necessário."),
        new("gold-synthetic","Carregar Gold sintética","Carrega uma Gold sintética de bootstrap para permitir a primeira calibração antes do recebimento de arquivos externos.","pwsh","-NoProfile -File scripts/dev-console-gold-synthetic.ps1",".local/dev-console/gold-synthetic-records.json",["infrastructure"],"Bootstrap DEV: pode ser substituída por Gold real quando ela já existir."),
        new("initial-calibration","Calibração inicial a partir da Gold","Gera e ativa o primeiro modelo de linkage a partir da Gold existente. Recusa execução se a Gold estiver vazia ou se já houver modelo ATIVO.","pwsh","-NoProfile -File scripts/dev-console-operations.ps1 -Action calibrate-initial",".local/dev-console/initial-calibration.json",["infrastructure","gold-synthetic"],"A dependência de Gold é semântica: serve Gold sintética ou Gold real. Blocking e referência IBGE são garantidos pelo fluxo de calibração."),
        new("contract-bundle","Gerar bundle de contratos e configurações","Gera um ZIP operacional sem binários com OpenAPI, contratos JSON, configurações governadas e metadados do modelo ATIVO.","pwsh","-NoProfile -File scripts/dev-console-contract-bundle.ps1",".local/dev-console/contract-config-bundle.zip",["initial-calibration"],"Exige modelo ATIVO para vincular o bundle ao fingerprint/configuração efetivamente calibrados."),
        new("zip","Gerar ZIP de ingestão","Abre a entrada manual e gera o ZIP real de ingestão.",null,null,null,["contract-bundle"],"A geração é local; o envio para a API só é permitido depois de existir o bundle de contratos/configurações."),
        new("ingestion","Enviar último ZIP para ingestão","Envia o último ZIP manual para a API real em NODE1 usando a credencial sintética DEV correspondente ao Gestor.","pwsh","-NoProfile -File scripts/dev-console-operations.ps1 -Action ingest-latest",".local/dev-console/last-ingestion.json",["contract-bundle","zip"],"Falha fechado se o bundle de contratos/configurações ainda não tiver sido gerado."),
        new("pipeline-status","Ver status da última ingestão","Consulta o recibo da última Entrega. Bronze, Silver, identidade e Gold são processados pelo Processor residente.","pwsh","-NoProfile -File scripts/dev-console-operations.ps1 -Action pipeline-status",".local/dev-console/last-ingestion-status.json",["ingestion"],null),
        new("bronze-verify","Verificar Bronze","Executa Jornada.Bronze.Verify no NODE2 contra as referências Bronze persistidas.","pwsh","-NoProfile -File scripts/dev-console-operations.ps1 -Action bronze-verify",null,["ingestion"],"Pode ser executado antes, mas só terá conteúdo útil depois de uma ingestão."),
        new("blocking","Reconstruir blocking","Executa a reconstrução one-shot da projeção local de blocking sem recompilar nada.","pwsh","-NoProfile -File scripts/dev-console-operations.ps1 -Action blocking",null,["gold-synthetic"],"Requer Gold disponível; pode ser Gold sintética ou real."),
        new("calibration","Recalibrar e ativar","Executa novo ciclo GENERATE_DRAFT → conferência → VALIDATE → ACTIVATE sobre uma Gold já existente.","pwsh","-NoProfile -File scripts/dev-console-operations.ps1 -Action calibrate",null,["initial-calibration"],"Use após a calibração inicial quando quiser gerar uma nova versão do modelo."),
        new("linkage","Executar linkage","Executa o Jornada.Linkage.Runner real no NODE2 usando o modelo calibrado ATIVO.","pwsh","-NoProfile -File scripts/dev-console-operations.ps1 -Action linkage",null,["initial-calibration"],"Aceita o modelo inicial ou uma recalibração posterior, desde que exista modelo ATIVO."),
        new("replay","Executar replay do último run","Executa REPLAY real do último linkage PUBLICADO elegível, sem publicar o resultado.","pwsh","-NoProfile -File scripts/dev-console-operations.ps1 -Action replay-latest",null,["linkage"],"Exige pelo menos um linkage PUBLICADO não-REPLAY."),
        new("report","Diagnóstico do último linkage","Executa o diagnóstico real do último linkage publicado, incluindo modelo, thresholds, cobertura e qualidade sintética.","pwsh","-NoProfile -File scripts/dev-console-operations.ps1 -Action report",null,["linkage"],null),
        new("environment-status","Status da infraestrutura","Mostra todos os serviços do compose, inclusive o init one-shot jornada-reference-bootstrap.","pwsh","-NoProfile -File scripts/dev-console-infrastructure.ps1 -Action status",null,[],null),
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

        async Task PumpAsync(StreamReader reader,StringBuilder sink,string stream)
        {
            while(await reader.ReadLineAsync() is { } line)
            {
                sink.AppendLine(line);
                live.Add(stream,line);
            }
        }

        var outTask=PumpAsync(process.StandardOutput,stdout,"stdout");
        var errTask=PumpAsync(process.StandardError,stderr,"stderr");
        await Task.WhenAll(outTask,errTask,process.WaitForExitAsync());
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

    public async Task<IReadOnlyList<RunRecord>> ListAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(root);
        var xs=new List<RunRecord>();
        foreach(var path in Directory.EnumerateFiles(root,"*.json").OrderByDescending(File.GetLastWriteTimeUtc))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var item=JsonSerializer.Deserialize<RunRecord>(await File.ReadAllTextAsync(path,ct),Opt);
                if(item is not null)xs.Add(item);
            }
            catch(JsonException)
            {
                // Histórico legado/corrompido não pode impedir a listagem das demais execuções.
            }
            catch(NotSupportedException)
            {
                // Mantém compatibilidade com registros persistidos por versões anteriores da Console.
            }
        }
        return xs;
    }

    public async Task<IReadOnlyDictionary<string,int>> CountByCommandAsync(CancellationToken ct)=>
        (await ListAsync(ct)).Where(x=>x.StartedAt>=sessionStartedAt)
            .GroupBy(x=>x.Command,StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x=>x.Key,x=>x.Count(),StringComparer.OrdinalIgnoreCase);
}
