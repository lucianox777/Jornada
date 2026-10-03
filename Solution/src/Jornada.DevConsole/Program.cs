using System.Diagnostics;
using System.Text.Json;

var builder=WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<ConsoleSession>();
builder.Services.AddSingleton<RunStore>();
builder.Services.AddSingleton<CommandExecutor>();
var app=builder.Build();
app.MapGet("/",()=>Results.Text(Page.Html,"text/html; charset=utf-8"));
app.MapGet("/api/commands",async(RunStore s,CancellationToken ct)=>{
    var counts=await s.CountByCommandAsync(ct);
    return Results.Ok(CommandCatalog.All.Select(x=>new {x.Id,x.Title,x.Description,x.Implemented,x.CommandLine,x.DisplayCommand,RunCount=counts.GetValueOrDefault(x.Id)}));
});
app.MapGet("/api/runs",async(RunStore s,CancellationToken ct)=>Results.Ok(await s.ListAsync(ct)));
app.MapGet("/api/runs/{id:guid}",async(Guid id,RunStore s,CancellationToken ct)=>await s.GetAsync(id,ct) is { } r?Results.Ok(r):Results.NotFound());
app.MapPost("/api/commands/{command}/run",async(string command,CommandExecutor exec,RunStore store,CancellationToken ct)=>{
    var definition=CommandCatalog.All.FirstOrDefault(x=>x.Id.Equals(command,StringComparison.OrdinalIgnoreCase));
    if(definition is null)return Results.NotFound();
    var run=await exec.RunAsync(definition,ct); await store.SaveAsync(run,ct); return Results.Ok(run);
});
app.Run();

sealed record CommandDefinition(string Id,string Title,string Description,string? File,string? Arguments,string? ResultPath) { public bool Implemented => File is not null; public string? CommandLine => File is null ? null : $"{File} {Arguments}"; public string DisplayCommand => CommandLine ?? "Comando real ainda não mapeado."; }
sealed record StepResult(string Command,int ExitCode,long DurationMs,string Output,string Error);
sealed record RunRecord(Guid Id,string Command,string Title,DateTimeOffset StartedAt,DateTimeOffset FinishedAt,string Status,string Summary,StepResult Step,IReadOnlyList<Dictionary<string,string?>> Records);
sealed class ConsoleSession { public DateTimeOffset StartedAt { get; }=DateTimeOffset.UtcNow; }

static class CommandCatalog {
    // Catálogo independente: qualquer comando pode ser executado a qualquer momento.
    public static readonly CommandDefinition[] All=[
        new("update-build","Atualizar e compilar","Atualiza o checkout e compila a solução real.","pwsh","-NoProfile -File scripts/dev-console-command.ps1 -Action update-build",null),
        new("infrastructure","Subir infraestrutura","Prepara os serviços locais necessários para a execução.",null,null,null),
        new("schema","Aplicar schema","Cria ou atualiza o schema do banco DEV.",null,null,null),
        new("ibge","Carregar referência IBGE","Prepara a referência IBGE usada pelo linkage.",null,null,null),
        new("calibration","Calibrar e ativar","Executa a calibração e a ativação da configuração escolhida.",null,null,null),
        new("source-data","Carregar dados de origem","Prepara dados de origem para os cenários de desenvolvimento.",null,null,null),
        new("zip","Gerar ZIP de ingestão","Gera o pacote ZIP que poderá ser enviado à ingestão.",null,null,null),
        new("ingestion","Executar ingestão","Executa a entrada de dados pela ingestão.",null,null,null),
        new("bronze","Processar Bronze","Executa ou inspeciona a etapa Bronze.",null,null,null),
        new("silver","Processar Silver","Executa ou inspeciona a etapa Silver.",null,null,null),
        new("blocking","Executar blocking","Gera candidatos para o linkage sem impor sequência com outros comandos.",null,null,null),
        new("linkage","Executar linkage","Executa o linkage probabilístico.",null,null,null),
        new("identity","Consolidar identidade","Executa a consolidação de identidade.",null,null,null),
        new("gold-synthetic","Carregar Gold sintética","Carrega fixture sintética diretamente, sem exigir ingestão, blocking, linkage ou modelo ATIVO.","pwsh","-NoProfile -File scripts/dev-console-gold-synthetic.ps1",".local/dev-console/gold-synthetic-records.json"),
        new("gold","Gerar Gold","Executa ou inspeciona a geração da camada Gold.",null,null,null),
        new("replay","Executar replay","Executa um replay a partir das evidências disponíveis.",null,null,null),
        new("semiblind","Executar consulta semicega","Executa a consulta semicega de validação.",null,null,null),
        new("report","Gerar relatório","Produz o relatório da execução escolhida.",null,null,null),
        new("finish","Finalizar ambiente","Finaliza os processos do cenário sem apagar o histórico da Console.",null,null,null),
        new("destroy","Destruir ambiente DEV","Remove a infraestrutura DEV quando essa ação estiver implementada.",null,null,null)
    ];
}

sealed class CommandExecutor(IWebHostEnvironment env) {
    public async Task<RunRecord> RunAsync(CommandDefinition d,CancellationToken ct) {
        var started=DateTimeOffset.UtcNow; var root=FindSolutionRoot(env.ContentRootPath);
        if(!d.Implemented){
            var pendingStep=new StepResult(d.DisplayCommand,-1,0,"","");
            return new RunRecord(Guid.NewGuid(),d.Id,d.Title,started,DateTimeOffset.UtcNow,"SEM EXECUTOR","Opção disponível; comando real ainda não mapeado.",pendingStep,Array.Empty<Dictionary<string,string?>>());
        }
        var sw=Stopwatch.StartNew();
        using var p=new Process{StartInfo=new ProcessStartInfo(d.File!,d.Arguments!){WorkingDirectory=root,RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true}};
        p.Start();var stdout=p.StandardOutput.ReadToEndAsync(ct);var stderr=p.StandardError.ReadToEndAsync(ct);await p.WaitForExitAsync(ct);sw.Stop();
        var records=new List<Dictionary<string,string?>>();
        if(d.ResultPath is not null){var path=Path.Combine(root,d.ResultPath);if(File.Exists(path)){using var doc=JsonDocument.Parse(await File.ReadAllTextAsync(path,ct));foreach(var row in doc.RootElement.EnumerateArray())records.Add(row.EnumerateObject().ToDictionary(x=>x.Name,x=>(string?)x.Value.ToString()));}}
        var step=new StepResult(d.CommandLine!,p.ExitCode,sw.ElapsedMilliseconds,await stdout,await stderr);
        var summary=records.Count>0?$"{records.Count} registro(s) no resultado.":p.ExitCode==0?"Comando concluído.":"Comando falhou; veja stdout/stderr.";
        return new RunRecord(Guid.NewGuid(),d.Id,d.Title,started,DateTimeOffset.UtcNow,p.ExitCode==0?"SUCESSO":"FALHA",summary,step,records);
    }
    static string FindSolutionRoot(string start){for(var d=new DirectoryInfo(start);d is not null;d=d.Parent)if(File.Exists(Path.Combine(d.FullName,"Jornada.sln")))return d.FullName;throw new DirectoryNotFoundException("Jornada.sln não encontrado.");}
}

sealed class RunStore(IWebHostEnvironment env,ConsoleSession session) {
    readonly string root=Path.Combine(env.ContentRootPath,".runs");
    static readonly JsonSerializerOptions Opt=new(JsonSerializerDefaults.Web){WriteIndented=true};
    public async Task SaveAsync(RunRecord run,CancellationToken ct){Directory.CreateDirectory(root);await File.WriteAllTextAsync(Path.Combine(root,$"{run.Id:N}.json"),JsonSerializer.Serialize(run,Opt),ct);}
    public async Task<RunRecord?> GetAsync(Guid id,CancellationToken ct){var p=Path.Combine(root,$"{id:N}.json");return File.Exists(p)?JsonSerializer.Deserialize<RunRecord>(await File.ReadAllTextAsync(p,ct),Opt):null;}
    public async Task<IReadOnlyList<RunRecord>> ListAsync(CancellationToken ct){Directory.CreateDirectory(root);var xs=new List<RunRecord>();foreach(var p in Directory.EnumerateFiles(root,"*.json").OrderByDescending(File.GetLastWriteTimeUtc)){var x=JsonSerializer.Deserialize<RunRecord>(await File.ReadAllTextAsync(p,ct),Opt);if(x is not null&&x.StartedAt>=session.StartedAt)xs.Add(x);}return xs;}
    public async Task<IReadOnlyDictionary<string,int>> CountByCommandAsync(CancellationToken ct)=>(await ListAsync(ct)).GroupBy(x=>x.Command,StringComparer.OrdinalIgnoreCase).ToDictionary(x=>x.Key,x=>x.Count(),StringComparer.OrdinalIgnoreCase);
}

static class Page { public const string Html="""
<!doctype html><html lang=pt-BR><meta charset=utf-8><meta name=viewport content="width=device-width"><title>Jornada DEV Console</title><style>body{font:15px system-ui;margin:24px;background:#f5f6f8;color:#17202a}main{max-width:1100px;margin:auto}section{background:white;padding:16px;margin:14px 0;border:1px solid #ddd;border-radius:8px}button{padding:8px 12px}button:disabled{cursor:wait;opacity:.72}a{cursor:pointer;color:#075ca8}.run{border-top:1px solid #ddd;padding:12px 0}.run.running{background:#fff8dd}.cmd-title{display:flex;gap:12px;align-items:center;justify-content:space-between}.cmd-desc{margin:4px 0 6px}.cmd-line{display:block;color:#555;margin-top:3px}.running-badge{display:inline-flex;align-items:center;gap:6px;font-weight:700;color:#8a5a00;margin-right:8px}.spinner{width:14px;height:14px;border:2px solid #d6a128;border-top-color:transparent;border-radius:50%;display:inline-block;animation:spin .8s linear infinite}@keyframes spin{to{transform:rotate(360deg)}}@media (prefers-reduced-motion:reduce){.spinner{animation:none;border-top-color:#d6a128}}pre{white-space:pre-wrap;background:#111;color:#eee;padding:10px;overflow:auto}table{border-collapse:collapse;width:100%}td,th{border-bottom:1px solid #ddd;padding:6px;text-align:left}</style><main><h1>Jornada · Console DEV</h1><p>Comandos independentes. Execute somente o que quiser; nenhuma ação exige a anterior.</p><section><h2>Comandos</h2><div id=activity aria-live="polite"></div><div id=commands></div></section><section><h2>Execuções</h2><div id=runs></div></section><section id=detail hidden><h2>Resultado da execução</h2><div id=body></div></section></main><script>
const esc=x=>String(x??'').replace(/[&<>]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;'}[c]));const running=new Set();let commandCache=[];async function j(u,o){let r=await fetch(u,o);if(!r.ok)throw Error(await r.text());return r.json()}
function renderCommands(){activity.innerHTML=running.size?'<p><strong>'+running.size+' ação(ões) rodando agora.</strong></p>':'';commands.innerHTML=commandCache.map(c=>{let isRunning=running.has(c.id);return '<div class="run '+(isRunning?'running':'')+'"><div class="cmd-title"><b>'+esc(c.title)+'</b><span>'+(isRunning?'<span class="running-badge"><span class="spinner" aria-hidden="true"></span>RODANDO...</span>':'')+'<strong>executado '+c.runCount+' vez(es)</strong> <button '+(isRunning?'disabled aria-busy="true"':'')+' onclick="go(\''+c.id+'\')">'+(isRunning?'Rodando...':'Executar')+'</button></span></div><div class="cmd-desc">'+esc(c.description)+'</div><small class="cmd-line"><code>'+esc(c.displayCommand)+'</code></small></div>'}).join('')}
async function load(){commandCache=await j('/api/commands');renderCommands();let rs=await j('/api/runs');runs.innerHTML=rs.length?rs.map(r=>'<div class=run><a onclick="openRun(\''+r.id+'\')"><b>'+esc(r.title)+'</b> · '+esc(r.status)+' · '+esc(r.startedAt)+'</a><br>'+esc(r.summary)+'</div>').join(''):'Nenhuma execução ainda.'}
async function go(id){if(running.has(id))return;running.add(id);renderCommands();try{await j('/api/commands/'+id+'/run',{method:'POST'});}catch(e){alert('Falha ao executar: '+e.message);}finally{running.delete(id);await load()}}
async function openRun(id){let r=await j('/api/runs/'+id);detail.hidden=false;body.innerHTML='<p><b>'+esc(r.status)+'</b> · '+esc(r.summary)+'</p>'+(r.records.length?'<h3>Registros gerados</h3><table><tr>'+Object.keys(r.records[0]).map(k=>'<th>'+esc(k)+'</th>').join('')+'</tr>'+r.records.map(x=>'<tr>'+Object.values(x).map(v=>'<td>'+esc(v)+'</td>').join('')+'</tr>').join('')+'</table>':'<p>Sem registros tabulares nesta execução.</p>')+'<details><summary>Comando e saída</summary><pre>'+esc(r.step.command)+'\\n\\n'+esc(r.step.output)+'\\n'+esc(r.step.error)+'</pre></details>';detail.scrollIntoView()}
load().catch(e=>{commands.innerHTML='<p>Falha ao carregar comandos: '+esc(e.message)+'</p>';runs.innerHTML='<p>Falha ao carregar execuções.</p>';console.error(e)})</script></html>
"""; }
