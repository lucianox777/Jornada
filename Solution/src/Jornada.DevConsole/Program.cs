using System.Diagnostics;
using System.Text.Json;

var builder=WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<RunStore>();
builder.Services.AddSingleton<CommandExecutor>();
var app=builder.Build();
app.MapGet("/",()=>Results.Text(Page.Html,"text/html; charset=utf-8"));
app.MapGet("/api/commands",()=>Results.Ok(CommandCatalog.All));
app.MapGet("/api/runs",async(RunStore s,CancellationToken ct)=>Results.Ok(await s.ListAsync(ct)));
app.MapGet("/api/runs/{id:guid}",async(Guid id,RunStore s,CancellationToken ct)=>await s.GetAsync(id,ct) is { } r?Results.Ok(r):Results.NotFound());
app.MapPost("/api/commands/{command}/run",async(string command,CommandExecutor exec,RunStore store,CancellationToken ct)=>{
    var definition=CommandCatalog.All.FirstOrDefault(x=>x.Id.Equals(command,StringComparison.OrdinalIgnoreCase));
    if(definition is null)return Results.NotFound();
    var run=await exec.RunAsync(definition,ct); await store.SaveAsync(run,ct); return Results.Ok(run);
});
app.Run();

sealed record CommandDefinition(string Id,string Title,string Description,string File,string Arguments,string? ResultPath);
sealed record StepResult(string Command,int ExitCode,long DurationMs,string Output,string Error);
sealed record RunRecord(Guid Id,string Command,string Title,DateTimeOffset StartedAt,DateTimeOffset FinishedAt,string Status,string Summary,StepResult Step,IReadOnlyList<Dictionary<string,string?>> Records);

static class CommandCatalog {
    // Catálogo independente: qualquer comando pode ser executado a qualquer momento.
    public static readonly CommandDefinition[] All=[
        new("update-build","Atualizar e compilar","Atualiza o checkout e compila a solução real.","pwsh","-NoProfile -File scripts/dev-console-command.ps1 -Action update-build",null),
        new("gold-synthetic","Carregar Gold sintética","Carrega fixture sintética diretamente, sem exigir ingestão anterior.","pwsh","-NoProfile -File scripts/dev-console-command.ps1 -Action gold-synthetic",".local/dev-console/gold-synthetic-records.json")
    ];
}

sealed class CommandExecutor(IWebHostEnvironment env) {
    public async Task<RunRecord> RunAsync(CommandDefinition d,CancellationToken ct) {
        var started=DateTimeOffset.UtcNow; var root=FindSolutionRoot(env.ContentRootPath); var sw=Stopwatch.StartNew();
        using var p=new Process{StartInfo=new ProcessStartInfo(d.File,d.Arguments){WorkingDirectory=root,RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true}};
        p.Start();var stdout=p.StandardOutput.ReadToEndAsync(ct);var stderr=p.StandardError.ReadToEndAsync(ct);await p.WaitForExitAsync(ct);sw.Stop();
        var records=new List<Dictionary<string,string?>>();
        if(d.ResultPath is not null){var path=Path.Combine(root,d.ResultPath);if(File.Exists(path)){using var doc=JsonDocument.Parse(await File.ReadAllTextAsync(path,ct));foreach(var row in doc.RootElement.EnumerateArray())records.Add(row.EnumerateObject().ToDictionary(x=>x.Name,x=>(string?)x.Value.ToString()));}}
        var step=new StepResult($"{d.File} {d.Arguments}",p.ExitCode,sw.ElapsedMilliseconds,await stdout,await stderr);
        var summary=records.Count>0?$"{records.Count} registro(s) no resultado.":p.ExitCode==0?"Comando concluído.":"Comando falhou; veja stdout/stderr.";
        return new RunRecord(Guid.NewGuid(),d.Id,d.Title,started,DateTimeOffset.UtcNow,p.ExitCode==0?"SUCESSO":"FALHA",summary,step,records);
    }
    static string FindSolutionRoot(string start){for(var d=new DirectoryInfo(start);d is not null;d=d.Parent)if(File.Exists(Path.Combine(d.FullName,"Jornada.sln")))return d.FullName;throw new DirectoryNotFoundException("Jornada.sln não encontrado.");}
}

sealed class RunStore(IWebHostEnvironment env) {
    readonly string root=Path.Combine(env.ContentRootPath,".runs");
    static readonly JsonSerializerOptions Opt=new(JsonSerializerDefaults.Web){WriteIndented=true};
    public async Task SaveAsync(RunRecord run,CancellationToken ct){Directory.CreateDirectory(root);await File.WriteAllTextAsync(Path.Combine(root,$"{run.Id:N}.json"),JsonSerializer.Serialize(run,Opt),ct);}
    public async Task<RunRecord?> GetAsync(Guid id,CancellationToken ct){var p=Path.Combine(root,$"{id:N}.json");return File.Exists(p)?JsonSerializer.Deserialize<RunRecord>(await File.ReadAllTextAsync(p,ct),Opt):null;}
    public async Task<IReadOnlyList<RunRecord>> ListAsync(CancellationToken ct){Directory.CreateDirectory(root);var xs=new List<RunRecord>();foreach(var p in Directory.EnumerateFiles(root,"*.json").OrderByDescending(File.GetLastWriteTimeUtc)){var x=JsonSerializer.Deserialize<RunRecord>(await File.ReadAllTextAsync(p,ct),Opt);if(x is not null)xs.Add(x);}return xs;}
}

static class Page { public const string Html="""
<!doctype html><html lang=pt-BR><meta charset=utf-8><meta name=viewport content="width=device-width"><title>Jornada DEV Console</title><style>body{font:15px system-ui;margin:24px;background:#f5f6f8;color:#17202a}main{max-width:1100px;margin:auto}section{background:white;padding:16px;margin:14px 0;border:1px solid #ddd;border-radius:8px}button{padding:8px 12px}a{cursor:pointer;color:#075ca8}.run{border-top:1px solid #ddd;padding:10px 0}pre{white-space:pre-wrap;background:#111;color:#eee;padding:10px;overflow:auto}table{border-collapse:collapse;width:100%}td,th{border-bottom:1px solid #ddd;padding:6px;text-align:left}</style><main><h1>Jornada · Console DEV</h1><p>Comandos independentes. Execute somente o que quiser; nenhuma ação exige a anterior.</p><section><h2>Comandos</h2><div id=commands></div></section><section><h2>Execuções</h2><div id=runs></div></section><section id=detail hidden><h2>Resultado da execução</h2><div id=body></div></section></main><script>
const esc=x=>String(x??'').replace(/[&<>]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;'}[c]));async function j(u,o){let r=await fetch(u,o);if(!r.ok)throw Error(await r.text());return r.json()}
async function load(){let cs=await j('/api/commands');commands.innerHTML=cs.map(c=>'<p><b>'+esc(c.title)+'</b> — '+esc(c.description)+' <button onclick="go(\''+c.id+'\')">Executar</button></p>').join('');let rs=await j('/api/runs');runs.innerHTML=rs.length?rs.map(r=>'<div class=run><a onclick="openRun(\''+r.id+'\')"><b>'+esc(r.title)+'</b> · '+esc(r.status)+' · '+esc(r.startedAt)+'</a><br>'+esc(r.summary)+'</div>').join(''):'Nenhuma execução ainda.'}
async function go(id){await j('/api/commands/'+id+'/run',{method:'POST'});await load()}
async function openRun(id){let r=await j('/api/runs/'+id);detail.hidden=false;body.innerHTML='<p><b>'+esc(r.status)+'</b> · '+esc(r.summary)+'</p>'+(r.records.length?'<h3>Registros gerados</h3><table><tr>'+Object.keys(r.records[0]).map(k=>'<th>'+esc(k)+'</th>').join('')+'</tr>'+r.records.map(x=>'<tr>'+Object.values(x).map(v=>'<td>'+esc(v)+'</td>').join('')+'</tr>').join('')+'</table>':'<p>Sem registros tabulares nesta execução.</p>')+'<details><summary>Comando e saída</summary><pre>'+esc(r.step.command)+'\n\n'+esc(r.step.output)+'\n'+esc(r.step.error)+'</pre></details>';detail.scrollIntoView()}
load()</script></html>
"""; }
