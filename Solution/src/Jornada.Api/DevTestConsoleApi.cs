using System.Text;

namespace Jornada.Api;

public static class DevTestConsoleApi
{
    public const string PageRoute="/dev/test";

    public static IServiceCollection AddDevTestConsole(this IServiceCollection services,IHostEnvironment env)
    {
        if(env.IsDevelopment()) {
            services.AddSingleton<DevTestConsoleStore>();
            services.AddSingleton<DevTestCommandRunner>();
        }
        return services;
    }

    public static IEndpointRouteBuilder MapDevTestConsole(this IEndpointRouteBuilder app)
    {
        var env=app.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
        if(!env.IsDevelopment()) return app;

        app.MapGet(PageRoute,()=>Results.Text(DevTestConsolePage.Html,"text/html; charset=utf-8",Encoding.UTF8));
        app.MapGet("/api/dev/test/parameters",(IConfiguration cfg)=>Results.Ok(Snapshot(cfg,env)));
        app.MapGet("/api/dev/test/sessions",async(DevTestConsoleStore store,CancellationToken ct)=>Results.Ok(await store.ListAsync(ct)));
        app.MapGet("/api/dev/test/sessions/{id:guid}",async(Guid id,DevTestConsoleStore store,CancellationToken ct)=>
            await store.GetAsync(id,ct) is { } s?Results.Ok(s):Results.NotFound());
        app.MapPost("/api/dev/test/sessions",async(StartSessionRequest request,IConfiguration cfg,DevTestConsoleStore store,CancellationToken ct)=>
            Results.Ok(await store.StartAsync(request.Name,Snapshot(cfg,env),ct)));
        app.MapPost("/api/dev/test/sessions/{id:guid}/actions/update-build",async(Guid id,IConfiguration cfg,DevTestConsoleStore store,DevTestCommandRunner runner,CancellationToken ct)=>{
            if(await store.GetAsync(id,ct) is null) return Results.NotFound();
            var execution=await runner.UpdateAndBuildAsync(Snapshot(cfg,env),ct);
            await store.AppendAsync(id,execution,ct);
            return Results.Ok(execution);
        });
        return app;
    }

    private static Dictionary<string,string?> Snapshot(IConfiguration cfg,IHostEnvironment env)
    {
        static bool Secret(string key)=>new[]{"password","secret","token","accesskey","connectionstrings","keyfile"}.Any(x=>key.Contains(x,StringComparison.OrdinalIgnoreCase));
        var result=cfg.AsEnumerable().Where(x=>!string.IsNullOrWhiteSpace(x.Value))
            .OrderBy(x=>x.Key,StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x=>x.Key,x=>Secret(x.Key)?"***":x.Value,StringComparer.OrdinalIgnoreCase);
        result["Runtime:Environment"]=env.EnvironmentName;
        result["Runtime:CapturedUtc"]=DateTimeOffset.UtcNow.ToString("O");
        return result;
    }

    public sealed record StartSessionRequest(string? Name);
}

internal static class DevTestConsolePage
{
    public const string Html="""
<!doctype html><html lang="pt-BR"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Jornada · Console DEV</title>
<style>body{font-family:system-ui;margin:0;background:#f5f6f8;color:#17202a}header{background:#17202a;color:white;padding:16px 24px}main{display:grid;grid-template-columns:280px 1fr;gap:18px;padding:18px}nav,section{background:white;border:1px solid #ddd;border-radius:8px;padding:16px}button{padding:9px 12px;margin:4px;cursor:pointer}.action{display:block;width:100%;text-align:left}.muted{color:#667}pre{white-space:pre-wrap;background:#111;color:#eee;padding:12px;overflow:auto}.run{border-top:1px solid #ddd;padding:12px 0}.ok{color:#176b35}.fail{color:#a21b1b}details{margin:8px 0}table{border-collapse:collapse;width:100%}td,th{border-bottom:1px solid #ddd;padding:6px;text-align:left}</style></head>
<body><header><b>Jornada · Console DEV passo a passo</b> <span id="sessionLabel"></span></header><main>
<nav><button class="action" onclick="showParameters()">Parâmetros ativos</button><button class="action" onclick="startSession()">1. Iniciar sessão</button><button class="action" onclick="updateBuild()">2. Atualizar e compilar fonte</button><hr><div class="muted">As próximas ações serão habilitadas incrementalmente reutilizando os comandos existentes. Nenhuma ação dispara a seguinte automaticamente.</div><hr><b>Sessões</b><div id="sessions"></div></nav>
<section><h2 id="title">Parâmetros ativos</h2><div id="content"></div></section></main>
<script>
let sessionId=localStorage.getItem('jornada.dev.session');
const esc=x=>String(x??'').replace(/[&<>]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;'}[c]));
async function json(url,opt){const r=await fetch(url,opt);if(!r.ok)throw new Error(await r.text());return r.json()}
async function showParameters(){title.textContent='Parâmetros ativos';const p=await json('/api/dev/test/parameters');content.innerHTML='<table>'+Object.entries(p).map(([k,v])=>'<tr><th>'+esc(k)+'</th><td>'+esc(v)+'</td></tr>').join('')+'</table>'}
async function startSession(){const name=prompt('Nome da sessão (opcional)');const s=await json('/api/dev/test/sessions',{method:'POST',headers:{'content-type':'application/json'},body:JSON.stringify({name})});sessionId=s.id;localStorage.setItem('jornada.dev.session',sessionId);await renderSession(s);await loadSessions()}
async function updateBuild(){if(!sessionId)return alert('Inicie uma sessão primeiro.');title.textContent='Atualizando e compilando…';content.innerHTML='<p>Uma execução finita está em andamento. Aguarde o resultado.</p>';const e=await json('/api/dev/test/sessions/'+sessionId+'/actions/update-build',{method:'POST'});await openSession(sessionId)}
function execution(e){return '<div class="run"><b>'+esc(e.action)+' · '+esc(e.status)+'</b> · '+esc(e.startedAt)+' → '+esc(e.finishedAt)+e.steps.map(s=>'<details><summary>'+esc(s.command)+' · exit '+s.exitCode+' · '+s.durationMs+' ms</summary><pre>'+esc(s.output)+(s.error?'\nSTDERR\n'+esc(s.error):'')+'</pre></details>').join('')+'</div>'}
async function renderSession(s){sessionLabel.textContent=' · '+s.name;title.textContent='Sessão '+s.name;content.innerHTML='<p><b>ID:</b> '+s.id+' · iniciada '+s.startedAt+'</p><h3>Histórico cumulativo</h3>'+s.executions.map(execution).join('');}
async function openSession(id){sessionId=id;localStorage.setItem('jornada.dev.session',id);await renderSession(await json('/api/dev/test/sessions/'+id))}
async function loadSessions(){const xs=await json('/api/dev/test/sessions');sessions.innerHTML=xs.map(s=>'<button class="action" onclick="openSession(\''+s.id+'\')">'+esc(s.name)+' ('+s.executions.length+')</button>').join('');if(sessionId)try{await openSession(sessionId)}catch{}}
showParameters();loadSessions();
</script></body></html>
""";
}
