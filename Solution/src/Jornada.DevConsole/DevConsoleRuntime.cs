using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

sealed record CommandDefinition(string Id,string Title,string Description,string? File,string? Arguments,string? ResultPath,string[] Dependencies,string? DependencyNote)
{
    public bool Visible{get;init;}=true;
    public bool Destructive{get;init;}
    public string[] CompositeSteps{get;init;}=[];
    public string Surface{get;init;}="flow";
    public string Stage{get;init;}="";
    public bool IsComposite=>CompositeSteps.Length>0;
    public bool Implemented=>IsComposite||File is not null||Id is "zip" or "semiblind" or "configuration" or "bronze" or "silver" or "linkage" or "gold";
    public string? CommandLine=>File is null?null:$"{File} {Arguments}";
    public string DisplayCommand=>Id switch{
        "zip"=>"Entrada manual → build-ingestion-fixture.py → POST /api/v1/ingestao/entregas",
        "semiblind"=>"POST /api/v1/identidade/candidatos (DEV sintético)",
        "configuration"=>"config/contracts/**/*.json + config/**/*.json + install/windows-production/Jornada.Cluster.Test.json",
        "bronze"=>"bronze.entrega_arquivo · objeto físico + metadados + Jornada.Bronze.Verify",
        "silver"=>"Jornada.Processor.Worker · Bronze → Silver em execução one-shot controlada pela Console DEV",
        "linkage"=>"Jornada.Linkage.Runner · resolução probabilística one-shot da última entrega",
        "gold"=>"gold.pessoa · estado publicado após Processor/Linkage",
        "infrastructure"=>"Orquestra 7 etapas independentes e reentrantes de preparação do ambiente",
        _=>CommandLine??"Operação parametrizada pela interface."
    };
}

sealed record StepResult(string Command,string WorkingDirectory,int ExitCode,long DurationMs,string Output,string Error,string? ResultPath,IReadOnlyList<string>? Artifacts=null);
sealed record ManualZipRequest(string Gestor,string ManifestJson,string PessoasJsonl,string RegistrosJsonl);
sealed record RunRecord(Guid Id,string Command,string Title,DateTimeOffset StartedAt,DateTimeOffset FinishedAt,string Status,string Summary,StepResult Step,IReadOnlyList<Dictionary<string,string?>> Records,int ExecutionNumber=0,Guid? ParentRunId=null);
sealed record RunSummary(Guid Id,string Command,string Title,DateTimeOffset StartedAt,DateTimeOffset FinishedAt,string Status,string Summary,int ExecutionNumber=0,Guid? ParentRunId=null);
sealed record StartedExecution(Guid Id,int ExecutionNumber);
sealed record ConsoleEvent(long Seq,DateTimeOffset At,string Stream,string Text);

static class DevConsoleJson
{
    public static readonly JsonSerializerOptions Pretty=new(JsonSerializerDefaults.Web){WriteIndented=true};
    public static readonly JsonSerializerOptions Compact=new(JsonSerializerDefaults.Web);
}

sealed class ConsoleRuntimeMode
{
    public string Mode{get;}
    public bool IsDev=>Mode=="DEV";
    public bool IsProduction=>Mode=="PROD";

    public ConsoleRuntimeMode()
    {
        var configured=Environment.GetEnvironmentVariable("JORNADA_RUNTIME_MODE");
        Mode=string.IsNullOrWhiteSpace(configured)?"HML":configured.Trim().ToUpperInvariant();
        if(Mode is not ("HML" or "DEV" or "PROD"))
            throw new InvalidOperationException($"JORNADA_RUNTIME_MODE inválido: {Mode}. Use HML, DEV ou PROD.");
    }

    public bool IsDisabled(CommandDefinition command)=>IsProduction&&command.Destructive;
    public string? DisabledReason(CommandDefinition command)=>IsDisabled(command)
        ?"Operação destrutiva indisponível na interface em PROD. Use o comando explícito com confirmação de produção."
        :null;
}

static class CommandCatalog
{
    // A navegação principal segue a jornada real do dado. Ferramentas administrativas,
    // diagnósticos e ensaios ficam em uma superfície separada para não esconder as
    // transições Bronze → Silver → Identidade/Linkage → Gold.
    public static readonly CommandDefinition[] All=[
        new("infrastructure","Preparar ambiente completo","Executa em sequência as sete etapas reentrantes: banco/serviços, referência IBGE, nós, corpus de calibração, blocking, modelo inicial e finalização.",null,null,null,[],null)
            {Stage="1 · Preparação do ambiente",CompositeSteps=["infra-base","infra-reference","infra-nodes","infra-corpus","infra-blocking","infra-model","infra-finalize"]},
        new("infra-base","Banco e serviços básicos","Prepara Docker, SQL Server, schema canônico e NAS sem executar referência, nós, blocking ou calibração.","pwsh","-NoProfile -File scripts/dev-console-infrastructure.ps1 -Action base",null,[],null)
            {Stage="1.1 · Banco e serviços básicos"},
        new("infra-reference","Referência IBGE","Materializa ou valida o snapshot nominal IBGE canônico. Na primeira execução esta costuma ser a etapa mais longa.","pwsh","-NoProfile -File scripts/dev-console-infrastructure.ps1 -Action reference",null,["infra-base"],"Requer SQL/schema e NAS prontos.")
            {Stage="1.2 · Referência IBGE"},
        new("infra-nodes","NODE1 / NODE2","Inicia NODE1/NODE2, valida readiness e garante os objetos Bronze canônicos usados pelo ambiente.","pwsh","-NoProfile -File scripts/dev-console-infrastructure.ps1 -Action nodes",null,["infra-reference"],"Requer a referência IBGE materializada.")
            {Stage="1.3 · NODE1 / NODE2"},
        new("infra-corpus","Corpus de calibração","Materializa/valida a Gold sintética canônica de 30.000 pessoas. Em DEV preserva o corpus adicional configurado para publicação posterior.","pwsh","-NoProfile -File scripts/dev-console-infrastructure.ps1 -Action corpus",null,["infra-nodes"],"Requer os nós prontos e a referência nominal disponível.")
            {Stage="1.4 · Corpus de calibração"},
        new("infra-blocking","Blocking","Reconcilia integralmente a projeção física de blocking com o estado atual de Gold/Silver.","pwsh","-NoProfile -File scripts/dev-console-infrastructure.ps1 -Action blocking",null,["infra-corpus"],"Mudanças no corpus tornam esta etapa desatualizada.")
            {Stage="1.5 · Blocking"},
        new("infra-model","Modelo inicial","Garante um modelo BOOTSTRAP calibrado e ATIVO; em DEV também publica os registros adicionais pelo Linkage Runner real.","pwsh","-NoProfile -File scripts/dev-console-infrastructure.ps1 -Action model",null,["infra-blocking"],"A calibração revalida o blocking antes de publicar o modelo.")
            {Stage="1.6 · Modelo inicial"},
        new("infra-finalize","Finalização","Gera configuração inicial, bundle de contratos/configurações e registra o resumo final do ambiente.","pwsh","-NoProfile -File scripts/dev-console-infrastructure.ps1 -Action finalize",null,["infra-model"],"Conclui os artefatos exigidos pela ingestão.")
            {Stage="1.7 · Finalização"},
        new("zip","Ingestão","Cria e envia uma nova Entrega pela API. No modo didático da Console DEV, o Processor residente fica suspenso: o ZIP permanece na Bronze até a etapa Silver ser executada explicitamente.",null,null,null,["infra-finalize"],"A finalização gera o bundle exigido pela ingestão. Depois de enviar, confira a Bronze antes de processar.")
            {Stage="2 · Ingestão"},
        new("bronze","Bronze","Mostra os metadados e a localização lógica do objeto recebido e permite verificar a integridade física da última Entrega com Jornada.Bronze.Verify.",null,null,null,["zip"],"A verificação confere objeto, SHA-256 e tamanho. Ela não processa nem altera a Entrega.")
            {Stage="3 · Bronze"},
        new("silver","Silver · processar Bronze","Executa explicitamente o Jornada.Processor.Worker em modo one-shot para drenar somente a Entrega pendente da Console DEV e depois permite inspecionar silver.pessoa_observacao.","pwsh","-NoProfile -File scripts/dev-console-operations.ps1 -Action process-latest",null,["bronze"],"A Console DEV desabilita o Processor residente para que esta transição seja visível e acionada pelo operador.")
            {Stage="4 · Silver"},
        new("linkage","Identidade e Linkage","Mostra a camada de vínculos correntes e executa o Jornada.Linkage.Runner real no NODE2 somente para as observações elegíveis da última Entrega. Replay continua disponível como ação secundária.","pwsh","-NoProfile -File scripts/dev-console-operations.ps1 -Action linkage",null,["silver"],"Execute o Processor antes. Se a identidade determinística já resolveu tudo, o Runner informa que não há universo probabilístico para a Entrega.")
            {Stage="5 · Identidade / Linkage"},
        new("gold","Gold / Serving","Inspeciona o estado canônico publicado em gold.pessoa após as etapas de Processor e, quando necessário, Linkage. A visualização é somente leitura.",null,null,null,["silver"],"Para entregas SEM_CPF que dependem de resolução probabilística, execute Linkage antes de interpretar o estado final.")
            {Stage="6 · Gold / Serving"},
        new("finish","Finalizar e destruir ambiente","Encerra o cluster e remove containers, volumes e órfãos locais. Em PROD a ação é bloqueada na interface.","pwsh","-NoProfile -File scripts/dev-console-infrastructure.ps1 -Action clean",null,[],null)
            {Stage="7 · Encerramento",Destructive=true},

        new("system-status","Estado geral do sistema","Diagnóstico read-only consolidado de infraestrutura, SQL/schema, referência IBGE, modelo, componentes, Bronze e último linkage.","pwsh","-NoProfile -File scripts/dev-console-operations.ps1 -Action system-status",null,["infra-nodes"],"No modo didático, Processor residente ausente é esperado; o binário one-shot deve estar disponível.")
            {Surface="tools",Stage="Verificações"},
        new("reference-check","Verificar referência IBGE","Executa o quick check da referência nominal IBGE sem recriar a infraestrutura.","pwsh","-NoProfile -File scripts/dev-console-operations.ps1 -Action reference-check",null,["infra-reference"],null)
            {Surface="tools",Stage="Verificações"},
        new("bronze-verify-all","Verificar Bronze completa","Executa Jornada.Bronze.Verify sobre todas as referências Bronze DISPONÍVEIS do ambiente atual.","pwsh","-NoProfile -File scripts/dev-console-operations.ps1 -Action bronze-verify",null,["infra-nodes"],"Para a Entrega corrente, use o botão Verificar integridade no cartão Bronze.")
            {Surface="tools",Stage="Verificações"},
        new("semiblind","Consulta semicega","Consulta até cinco candidatos pela API real sem expor CPF, UUID ou score. O formulário usa uma pessoa sintética da Gold DEV como exemplo.",null,null,null,["infra-model"],null)
            {Surface="tools",Stage="Consultas"},
        new("configuration","Contratos e configurações","Administra contratos de ingestão e configurações ativas, preservando suas validações e persistências próprias.",null,null,null,[], "Configurações que exigem reinício continuam sinalizadas.")
            {Surface="tools",Stage="Administração"},
        new("reset-environment","Resetar ambiente","Recria o banco e o bootstrap do ambiente. Permitido em HML/DEV; em PROD fica desabilitado na interface e exige confirmação explícita na CLI.","pwsh","-NoProfile -File scripts/dev-console-infrastructure.ps1 -Action reset",null,[],null)
            {Surface="tools",Stage="Administração",Destructive=true},
        new("calibration","Recalibrar e ativar","Executa novo ciclo GENERATE_DRAFT → conferência → VALIDATE → ACTIVATE sobre a Gold DEV corrente.","pwsh","-NoProfile -File scripts/dev-console-operations.ps1 -Action calibrate",null,["infra-corpus"],"Use apenas para criar deliberadamente uma nova versão de modelo.")
            {Surface="tools",Stage="Modelo e massa"},
        new("gold-synthetic","Adicionar mais 5.000 registros","Expande a Gold sintética DEV em blocos de 5.000 e reconstrói blocking automaticamente.","pwsh","-NoProfile -File scripts/dev-console-gold-add.ps1 -AdditionalPeople 5000",".local/dev-console/gold-synthetic-add.json",["infra-model"],null)
            {Surface="tools",Stage="Modelo e massa"},

        new("contract-bundle","Gerar bundle de contratos e configurações","Gera um ZIP operacional sem binários com OpenAPI, contratos JSON, configurações governadas e metadados do modelo ATIVO.","pwsh","-NoProfile -File scripts/dev-console-contract-bundle.ps1",".local/dev-console/contract-config-bundle.zip",["infra-model"],"Ação auxiliar de Contratos e configurações."){Visible=false},
        new("ingestion","Reenviar último ZIP para ingestão","Reenvia manualmente o último ZIP já gerado para a API real em NODE1 usando a credencial sintética DEV correspondente ao Gestor.","pwsh","-NoProfile -File scripts/dev-console-operations.ps1 -Action ingest-latest",".local/dev-console/last-ingestion.json",["contract-bundle","zip"],"Ação auxiliar do cartão Ingestão."){Visible=false},
        new("pipeline-status","Ver status da última ingestão","Consulta o recibo da última Entrega sem acionar processamento.","pwsh","-NoProfile -File scripts/dev-console-operations.ps1 -Action pipeline-status",".local/dev-console/last-ingestion-status.json",["zip"],"Ação auxiliar da Ingestão."){Visible=false},
        new("bronze-verify-latest","Verificar integridade da última Entrega","Executa Jornada.Bronze.Verify filtrado pelo entrega_id registrado pela Console DEV.","pwsh","-NoProfile -File scripts/dev-console-operations.ps1 -Action bronze-verify-latest",".local/dev-console/last-bronze-verify.json",["zip"],"Ação auxiliar do cartão Bronze."){Visible=false},
        new("blocking","Reconstruir blocking","Executa manualmente a reconstrução one-shot da projeção local de blocking.","pwsh","-NoProfile -File scripts/dev-console-operations.ps1 -Action blocking",null,["infra-corpus"],"Ferramenta técnica."){Visible=false},
        new("replay","Executar replay do último run","Executa REPLAY real do último linkage PUBLICADO elegível, sem publicar o resultado.","pwsh","-NoProfile -File scripts/dev-console-operations.ps1 -Action replay-latest",null,["linkage"],"Ação auxiliar do cartão Linkage."){Visible=false}
    ];
}

static class DevConsolePaths
{
    public static string FindSolutionRoot(string start)
    {
        for(var d=new DirectoryInfo(start);d is not null;d=d.Parent)
            if(File.Exists(Path.Combine(d.FullName,"Jornada.sln")))return d.FullName;
        throw new DirectoryNotFoundException("Jornada.sln não encontrado.");
    }

    public static string ApplicationDataRoot()
    {
        var local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if(string.IsNullOrWhiteSpace(local))local=AppContext.BaseDirectory;
        return Path.Combine(local,"Jornada","DevConsole");
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
    int PessoaSchemaVersao,
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
        var db=vars.TryGetValue("JORNADA_SQL_DATABASE",out var dbValue)&&!string.IsNullOrWhiteSpace(dbValue)?dbValue:"JornadaLocal";
        if(!vars.TryGetValue("JORNADA_SQL_SA_PASSWORD",out var password)||string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("JORNADA_SQL_SA_PASSWORD ausente.");

        var query="SET NOCOUNT ON; SELECT TOP(1) CONVERT(varchar(36),p.pessoa_uuid),REPLACE(REPLACE(p.nome_completo,'|',' '),CHAR(10),' '),CONVERT(varchar(10),p.data_nascimento,23),REPLACE(REPLACE(p.nome_mae,'|',' '),CHAR(10),' '),CONVERT(varchar(10),v.versao) FROM gold.pessoa p CROSS JOIN (SELECT TOP(1) gpv.versao FROM ref.gestor g JOIN ref.gestor_pessoa_versao gpv ON gpv.gestor_id=g.gestor_id WHERE g.codigo='SEHAB' AND g.ativo=1 AND gpv.status IN('ATIVA','ENCERRADA') ORDER BY CASE gpv.status WHEN 'ATIVA' THEN 0 ELSE 1 END,gpv.versao DESC) v WHERE p.nome_completo IS NOT NULL AND p.data_nascimento IS NOT NULL AND p.nome_mae IS NOT NULL ORDER BY p.atualizado_em DESC,p.pessoa_uuid;";
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
        if(parts.Length<5)throw new InvalidOperationException("Gold sintética ou contrato cadastral utilizável SEHAB não disponível para montar o exemplo.");
        var uuid=parts[0];
        var nome=parts[1];
        var nascimento=parts[2];
        var mae=parts[3];
        if(!int.TryParse(parts[4],out var pessoaSchemaVersao)||pessoaSchemaVersao<1)throw new InvalidOperationException("Versão cadastral SEHAB utilizável inválida.");
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
            ["formatoVersao"]=2,["pessoaSchemaVersao"]=pessoaSchemaVersao,["codigoSistemaOrigem"]=sistema,["natureza"]="BENEFICIO",["codigoTipo"]=tipo,["tipoVersao"]=1,
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
            "gold.pessoa",uuid,gestor,sistema,tipo,pessoaSchemaVersao,nome,nascimento,mae,pessoaId,registroId,
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

    public StartedExecution StartCommand(CommandDefinition definition)
    {
        var id=Guid.NewGuid();
        var executionNumber=store.ReserveExecutionNumber(definition.Id);
        var live=new LiveExecution(id);
        if(!active.TryAdd(id,live))throw new InvalidOperationException("Não foi possível registrar a execução.");
        _=definition.IsComposite
            ?Task.Run(()=>RunCompositeCommandAsync(id,definition,live,executionNumber))
            :Task.Run(()=>RunCommandAsync(id,definition,live,executionNumber));
        return new StartedExecution(id,executionNumber);
    }

    public StartedExecution StartManualZip(ManualZipRequest request)
    {
        var id=Guid.NewGuid();
        var executionNumber=store.ReserveExecutionNumber("zip");
        var live=new LiveExecution(id);
        if(!active.TryAdd(id,live))throw new InvalidOperationException("Não foi possível registrar a execução.");
        _=Task.Run(()=>RunManualZipAsync(id,request,live,executionNumber));
        return new StartedExecution(id,executionNumber);
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

    async Task RunCompositeCommandAsync(Guid id,CommandDefinition definition,LiveExecution live,int executionNumber)
    {
        var root=DevConsolePaths.FindSolutionRoot(env.ContentRootPath);
        var started=DateTimeOffset.UtcNow;
        var sw=Stopwatch.StartNew();
        var combinedOutput=new StringBuilder();
        var combinedError=new StringBuilder();
        var combinedArtifacts=new List<string>();

        live.Add("system",$"Execução {definition.Title} #{executionNumber} · {id:N}");
        live.Add("system",$"Diretório: {root}");
        live.Add("command",$"> {definition.DisplayCommand}");
        live.Add("system",$"Plano: {definition.CompositeSteps.Length} etapa(s) independente(s).");

        foreach(var stepId in definition.CompositeSteps)
        {
            var child=CommandCatalog.All.FirstOrDefault(x=>x.Id.Equals(stepId,StringComparison.OrdinalIgnoreCase));
            if(child is null||child.File is null)
            {
                sw.Stop();
                var message=$"Etapa composta inválida ou sem executor: {stepId}.";
                live.Add("stderr",message);
                live.Add("status",$"FALHA · {(sw.ElapsedMilliseconds/1000d):0.00}s");
                var failedStep=new StepResult(definition.DisplayCommand,root,-1,sw.ElapsedMilliseconds,combinedOutput.ToString(),message,null,combinedArtifacts);
                await FinishAsync(new RunRecord(id,definition.Id,definition.Title,started,DateTimeOffset.UtcNow,"FALHA",message,failedStep,Array.Empty<Dictionary<string,string?>>(),executionNumber),live);
                return;
            }

            var childId=Guid.NewGuid();
            var childNumber=store.ReserveExecutionNumber(child.Id);
            var childStarted=DateTimeOffset.UtcNow;
            var childSw=Stopwatch.StartNew();
            live.Add("system","");
            live.Add("system",$"=== {child.Stage} · {child.Title} #{childNumber} ===");
            live.Add("command",$"> {child.DisplayCommand}");

            try
            {
                var result=await RunProcessAsync(child.File,child.Arguments!,root,live);
                childSw.Stop();
                var candidatePath=child.ResultPath is null?null:Path.GetFullPath(Path.Combine(root,child.ResultPath));
                var resultPath=result.ExitCode==0&&candidatePath is not null&&File.Exists(candidatePath)?candidatePath:null;
                var records=resultPath is null?Array.Empty<Dictionary<string,string?>>():await LoadRecordsAsync(child.ResultPath,root);
                var artifacts=ParseArtifacts(result.Output,root);
                combinedOutput.AppendLine($"=== {child.Title} #{childNumber} ===").Append(result.Output);
                combinedError.Append(result.Error);
                combinedArtifacts.AddRange(artifacts);
                if(resultPath is not null)live.Add("result",$"{child.Title} #{childNumber}: {resultPath}");

                var summary=records.Count>0
                    ?$"{records.Count} registro(s) no resultado. Resultado: {resultPath}"
                    :result.ExitCode==0
                        ?(resultPath is null?"Etapa concluída.":$"Etapa concluída. Resultado: {resultPath}")
                        :$"Etapa falhou (exit {result.ExitCode}).";
                var childStatus=result.ExitCode==0?"SUCESSO":"FALHA";
                var childStep=new StepResult(child.CommandLine!,root,result.ExitCode,childSw.ElapsedMilliseconds,result.Output,result.Error,resultPath,artifacts);
                var childRun=new RunRecord(childId,child.Id,child.Title,childStarted,DateTimeOffset.UtcNow,childStatus,summary,childStep,records,childNumber,id);
                await store.SaveAsync(childRun,CancellationToken.None);

                if(result.ExitCode!=0)
                {
                    sw.Stop();
                    live.Add("system",$"{child.Title} #{childNumber}: FALHA · {(childSw.ElapsedMilliseconds/1000d):0.00}s");
                    live.Add("status",$"FALHA · {(sw.ElapsedMilliseconds/1000d):0.00}s");
                    var parentStep=new StepResult(definition.DisplayCommand,root,result.ExitCode,sw.ElapsedMilliseconds,combinedOutput.ToString(),combinedError.ToString(),null,combinedArtifacts.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
                    await FinishAsync(new RunRecord(id,definition.Id,definition.Title,started,DateTimeOffset.UtcNow,"FALHA",$"Falha em {child.Title} #{childNumber}.",parentStep,Array.Empty<Dictionary<string,string?>>(),executionNumber),live);
                    return;
                }

                live.Add("system",$"{child.Title} #{childNumber}: SUCESSO · {(childSw.ElapsedMilliseconds/1000d):0.00}s");
            }
            catch(Exception ex)
            {
                childSw.Stop();
                sw.Stop();
                var error=ex.ToString();
                combinedError.AppendLine(error);
                live.Add("stderr",error);
                var childStep=new StepResult(child.CommandLine!,root,-1,childSw.ElapsedMilliseconds,"",error,null);
                await store.SaveAsync(new RunRecord(childId,child.Id,child.Title,childStarted,DateTimeOffset.UtcNow,"FALHA","Falha inesperada; veja o console.",childStep,Array.Empty<Dictionary<string,string?>>(),childNumber,id),CancellationToken.None);
                live.Add("status",$"FALHA · {(sw.ElapsedMilliseconds/1000d):0.00}s");
                var parentStep=new StepResult(definition.DisplayCommand,root,-1,sw.ElapsedMilliseconds,combinedOutput.ToString(),combinedError.ToString(),null,combinedArtifacts.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
                await FinishAsync(new RunRecord(id,definition.Id,definition.Title,started,DateTimeOffset.UtcNow,"FALHA",$"Falha em {child.Title} #{childNumber}.",parentStep,Array.Empty<Dictionary<string,string?>>(),executionNumber),live);
                return;
            }
        }

        sw.Stop();
        live.Add("status",$"SUCESSO · {(sw.ElapsedMilliseconds/1000d):0.00}s");
        var step=new StepResult(definition.DisplayCommand,root,0,sw.ElapsedMilliseconds,combinedOutput.ToString(),combinedError.ToString(),null,combinedArtifacts.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        await FinishAsync(new RunRecord(id,definition.Id,definition.Title,started,DateTimeOffset.UtcNow,"SUCESSO",$"{definition.CompositeSteps.Length}/{definition.CompositeSteps.Length} etapas concluídas.",step,Array.Empty<Dictionary<string,string?>>(),executionNumber),live);
    }

    async Task RunCommandAsync(Guid id,CommandDefinition definition,LiveExecution live,int executionNumber)
    {
        var root=DevConsolePaths.FindSolutionRoot(env.ContentRootPath);
        var started=DateTimeOffset.UtcNow;
        live.Add("system",$"Execução {definition.Title} #{executionNumber} · {id:N}");
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

    async Task RunManualZipAsync(Guid id,ManualZipRequest request,LiveExecution live,int executionNumber)
    {
        var root=DevConsolePaths.FindSolutionRoot(env.ContentRootPath);
        var started=DateTimeOffset.UtcNow;
        var sw=Stopwatch.StartNew();
        var command="python scripts/build-ingestion-fixture.py";
        live.Add("system",$"Execução Ingestão #${executionNumber} · {id:N}");
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
            var zipCommand=$"{python} scripts/build-ingestion-fixture.py --fixture \"{work}\" --gestor {request.Gestor.Trim()} --output-dir \"{work}\"";
            command=zipCommand;
            live.Add("command",$"> {zipCommand}");
            var generated=await RunProcessAsync(psi,live);
            var generatedOutput=generated.Output.Trim();
            var zip=generated.ExitCode==0&&File.Exists(generatedOutput)?Path.GetFullPath(generatedOutput):null;
            if(generated.ExitCode!=0||zip is null)
            {
                sw.Stop();
                var generationFailureSummary=$"Falha ao gerar ZIP (exit {generated.ExitCode}).";
                live.Add("status",$"FALHA · {(sw.ElapsedMilliseconds/1000d):0.00}s");
                var failedStep=new StepResult(command,root,generated.ExitCode,sw.ElapsedMilliseconds,generated.Output,generated.Error,null);
                await FinishAsync(new RunRecord(id,"zip","Gerar e enviar ZIP de ingestão",started,DateTimeOffset.UtcNow,"FALHA",generationFailureSummary,failedStep,Array.Empty<Dictionary<string,string?>>()),live);
                return;
            }

            live.Add("result",$"ZIP gerado: {zip}");
            live.Add("stdout","ZIP validado. Enviando o mesmo arquivo para a API real de ingestão...");

            var sendPsi=new ProcessStartInfo("pwsh"){WorkingDirectory=root,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8,UseShellExecute=false,CreateNoWindow=true};
            sendPsi.ArgumentList.Add("-NoProfile");
            sendPsi.ArgumentList.Add("-File");
            sendPsi.ArgumentList.Add(Path.Combine(root,"scripts","dev-console-operations.ps1"));
            sendPsi.ArgumentList.Add("-Action");
            sendPsi.ArgumentList.Add("ingest-latest");
            sendPsi.ArgumentList.Add("-ZipPath");
            sendPsi.ArgumentList.Add(zip);
            var sendCommand=$"pwsh -NoProfile -File scripts/dev-console-operations.ps1 -Action ingest-latest -ZipPath \"{zip}\"";
            command=zipCommand+" && "+sendCommand;
            live.Add("command",$"> {sendCommand}");
            var sent=await RunProcessAsync(sendPsi,live);
            sw.Stop();

            var receiptCandidate=Path.Combine(root,".local","dev-console","last-ingestion.json");
            var receipt=sent.ExitCode==0&&File.Exists(receiptCandidate)?Path.GetFullPath(receiptCandidate):null;
            var combinedOutput=generated.Output+(generated.Output.EndsWith(Environment.NewLine,StringComparison.Ordinal)?"":Environment.NewLine)+sent.Output;
            var combinedError=generated.Error+sent.Error;
            var artifacts=new List<string>{zip!};
            artifacts.AddRange(ParseArtifacts(sent.Output,root));
            var distinctArtifacts=artifacts.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var status=sent.ExitCode==0&&receipt is not null?"SUCESSO":"FALHA";
            var sendSummary=status=="SUCESSO"
                ?$"ZIP gerado e enviado. Recibo: {receipt}"
                :$"ZIP gerado, mas o envio falhou (exit {sent.ExitCode}). O ZIP foi preservado para diagnóstico/reenvio.";
            var resultPath=status=="SUCESSO"?receipt:zip;
            live.Add("status",$"{status} · {(sw.ElapsedMilliseconds/1000d):0.00}s");
            var step=new StepResult(command,root,sent.ExitCode,sw.ElapsedMilliseconds,combinedOutput,combinedError,resultPath,distinctArtifacts);
            await FinishAsync(new RunRecord(id,"zip","Gerar e enviar ZIP de ingestão",started,DateTimeOffset.UtcNow,status,sendSummary,step,Array.Empty<Dictionary<string,string?>>()),live);
        }
        catch(Exception ex)
        {
            sw.Stop();
            live.Add("stderr",ex.ToString());
            live.Add("status",$"FALHA · {(sw.ElapsedMilliseconds/1000d):0.00}s");
            var step=new StepResult(command,root,-1,sw.ElapsedMilliseconds,"",ex.ToString(),null);
            await FinishAsync(new RunRecord(id,"zip","Gerar e enviar ZIP de ingestão",started,DateTimeOffset.UtcNow,"FALHA","Falha ao gerar/enviar ZIP; veja o console.",step,Array.Empty<Dictionary<string,string?>>()),live);
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
        if(!File.Exists(path)||!string.Equals(Path.GetExtension(path),".json",StringComparison.OrdinalIgnoreCase))return records;
        try
        {
            using var doc=JsonDocument.Parse(await File.ReadAllTextAsync(path));
            if(doc.RootElement.ValueKind!=JsonValueKind.Array)return records;
            foreach(var row in doc.RootElement.EnumerateArray())
            {
                if(row.ValueKind!=JsonValueKind.Object)continue;
                records.Add(row.EnumerateObject().ToDictionary(x=>x.Name,x=>(string?)x.Value.ToString()));
            }
        }
        catch(JsonException)
        {
            // O artefato continua válido como arquivo mesmo quando não é uma lista JSON exibível.
        }
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
    readonly string root=Path.Combine(DevConsolePaths.ApplicationDataRoot(),"runs");
    readonly string summariesRoot=Path.Combine(DevConsolePaths.ApplicationDataRoot(),"runs","summaries");
    readonly string legacyRoot=Path.Combine(env.ContentRootPath,".runs");
    readonly DateTimeOffset sessionStartedAt=DateTimeOffset.UtcNow;
    static readonly JsonSerializerOptions Opt=new(JsonSerializerDefaults.Web){WriteIndented=true};

    IEnumerable<string> RunRoots()
    {
        yield return root;
        if(!string.Equals(Path.GetFullPath(legacyRoot),Path.GetFullPath(root),StringComparison.OrdinalIgnoreCase))
            yield return legacyRoot;
    }

    public async Task SaveAsync(RunRecord run,CancellationToken ct)
    {
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(summariesRoot);
        var target=Path.Combine(root,$"{run.Id:N}.json");
        var temp=target+".tmp";
        await File.WriteAllTextAsync(temp,JsonSerializer.Serialize(run,Opt),ct);
        File.Move(temp,target,true);

        var summary=new RunSummary(run.Id,run.Command,run.Title,run.StartedAt,run.FinishedAt,run.Status,run.Summary);
        var summaryTarget=Path.Combine(summariesRoot,$"{run.Id:N}.json");
        var summaryTemp=summaryTarget+".tmp";
        await File.WriteAllTextAsync(summaryTemp,JsonSerializer.Serialize(summary,Opt),ct);
        File.Move(summaryTemp,summaryTarget,true);
    }

    public async Task<RunRecord?> GetAsync(Guid id,CancellationToken ct)
    {
        foreach(var runRoot in RunRoots())
        {
            var path=Path.Combine(runRoot,$"{id:N}.json");
            if(!File.Exists(path))continue;
            try{return JsonSerializer.Deserialize<RunRecord>(await File.ReadAllTextAsync(path,ct),Opt);}
            catch(JsonException){return null;}
        }
        return null;
    }

    public async Task<IReadOnlyList<RunSummary>> ListSummariesAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(summariesRoot);
        var candidates=RunRoots()
            .Where(Directory.Exists)
            .SelectMany(runRoot=>Directory.EnumerateFiles(runRoot,"*.json",SearchOption.TopDirectoryOnly))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Take(200)
            .ToArray();
        var byId=new Dictionary<Guid,RunSummary>();
        foreach(var path in candidates)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var idText=Path.GetFileNameWithoutExtension(path);
                if(!Guid.TryParseExact(idText,"N",out var id))continue;
                var runRoot=Path.GetDirectoryName(path)!;
                var summaryPath=Path.Combine(runRoot,"summaries",$"{id:N}.json");
                RunSummary? summary=null;
                if(File.Exists(summaryPath))
                    summary=JsonSerializer.Deserialize<RunSummary>(await File.ReadAllTextAsync(summaryPath,ct),Opt);
                else
                {
                    summary=await ReadLegacySummaryAsync(path,ct);
                    if(summary is not null&&string.Equals(runRoot,root,StringComparison.OrdinalIgnoreCase))
                        await File.WriteAllTextAsync(Path.Combine(summariesRoot,$"{id:N}.json"),JsonSerializer.Serialize(summary,Opt),ct);
                }
                if(summary is not null&&(!byId.TryGetValue(id,out var previous)||summary.StartedAt>previous.StartedAt))
                    byId[id]=summary;
            }
            catch(JsonException){}
            catch(IOException){}
            catch(UnauthorizedAccessException){}
        }
        return byId.Values.OrderByDescending(x=>x.StartedAt).Take(200).ToArray();
    }

    static async Task<RunSummary?> ReadLegacySummaryAsync(string path,CancellationToken ct)
    {
        const int maxPrefixBytes=65536;
        await using var stream=File.OpenRead(path);
        var size=(int)Math.Min(maxPrefixBytes,stream.Length);
        if(size<=0)return null;
        var buffer=new byte[size];
        var read=0;
        while(read<size)
        {
            var n=await stream.ReadAsync(buffer.AsMemory(read,size-read),ct);
            if(n==0)break;
            read+=n;
        }
        var text=Encoding.UTF8.GetString(buffer,0,read);
        var stepIndex=text.IndexOf("\"step\"",StringComparison.OrdinalIgnoreCase);
        if(stepIndex<0)return null;
        var comma=text.LastIndexOf(',',stepIndex);
        if(comma<0)return null;
        var header=text[..comma]+"}";
        using var doc=JsonDocument.Parse(header);
        var e=doc.RootElement;
        if(!e.TryGetProperty("id",out var idElement)||!Guid.TryParse(idElement.GetString(),out var id))return null;
        var command=e.TryGetProperty("command",out var commandElement)?commandElement.GetString()??"":"";
        var title=e.TryGetProperty("title",out var titleElement)?titleElement.GetString()??command:command;
        var status=e.TryGetProperty("status",out var statusElement)?statusElement.GetString()??"":"";
        var summary=e.TryGetProperty("summary",out var summaryElement)?summaryElement.GetString()??"":"";
        var started=e.TryGetProperty("startedAt",out var startedElement)&&startedElement.TryGetDateTimeOffset(out var startedAt)?startedAt:File.GetCreationTimeUtc(path);
        var finished=e.TryGetProperty("finishedAt",out var finishedElement)&&finishedElement.TryGetDateTimeOffset(out var finishedAt)?finishedAt:started;
        return new RunSummary(id,command,title,started,finished,status,summary);
    }

    public async Task<IReadOnlyList<RunSummary>> ListSessionSummariesAsync(CancellationToken ct)=>
        (await ListSummariesAsync(ct))
            .Where(x=>x.StartedAt>=sessionStartedAt)
            .OrderByDescending(x=>x.StartedAt)
            .ToArray();

    public async Task<IReadOnlyDictionary<string,int>> CountByCommandAsync(CancellationToken ct)=>
        (await ListSessionSummariesAsync(ct))
            .GroupBy(x=>x.Command,StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x=>x.Key,x=>x.Count(),StringComparer.OrdinalIgnoreCase);
}
