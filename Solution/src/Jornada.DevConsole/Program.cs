using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

var builder=WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<ConsoleRuntimeMode>();
builder.Services.AddSingleton<RunStore>();
builder.Services.AddSingleton<LiveExecutionService>();
builder.Services.AddSingleton<GoldZipTemplateService>();
builder.Services.AddSingleton<SemiblindDevService>();
builder.Services.AddSingleton<ContractFileService>();
builder.Services.AddSingleton<ActiveConfigFileService>();
builder.Services.AddSingleton<LayerBrowserService>();
builder.Services.AddSingleton<ConsoleActivityLog>();
builder.Services.AddSingleton<IsolatedWorkerSupervisorStatusReader>();
builder.Services.AddSingleton<IsolatedWorkerSupervisorModeController>();
builder.Services.AddSingleton<IsolatedWorkerAuditJournal>();

var app=builder.Build();

app.Use(async(context,next)=>{
    if(!context.Request.Path.StartsWithSegments("/api")||context.Request.Path.StartsWithSegments("/api/activity"))
    {
        await next();
        return;
    }

    var activity=context.RequestServices.GetRequiredService<ConsoleActivityLog>();
    var sw=System.Diagnostics.Stopwatch.StartNew();
    var action=$"{context.Request.Method} {context.Request.Path}{context.Request.QueryString}";
    var exceptionLogged=false;
    try
    {
        await next();
    }
    catch(Exception ex)
    {
        exceptionLogged=true;
        sw.Stop();
        activity.Add("HTTP",action,"EXCEPTION",ex.Message,sw.ElapsedMilliseconds);
        throw;
    }
    finally
    {
        if(!exceptionLogged)
        {
            sw.Stop();
            activity.Add("HTTP",action,context.Response.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture),durationMs:sw.ElapsedMilliseconds);
        }
    }
});

app.MapGet("/",(HttpResponse response)=>{
    response.Headers.CacheControl="no-store, no-cache, must-revalidate";
    response.Headers.Pragma="no-cache";
    response.Headers.Expires="0";
    return Results.Text(Page.Html,"text/html; charset=utf-8");
});

app.MapGet("/api/version",(HttpResponse response,ConsoleRuntimeMode runtime)=>{
    response.Headers.CacheControl="no-store";
    var revision=Environment.GetEnvironmentVariable("JORNADA_DEV_CONSOLE_SOURCE_SHA");
    return Results.Ok(new{
        revision=string.IsNullOrWhiteSpace(revision)?"desconhecida":revision,
        processId=Environment.ProcessId,
        mode=runtime.Mode
    });
});

app.MapGet("/api/commands",async(HttpResponse response,ConsoleRuntimeMode runtime,RunStore store,CancellationToken ct)=>{
    response.Headers.CacheControl="no-store";
    var counts=await store.CountByCommandAsync(ct);
    var latest=await store.LatestByCommandAsync(ct);

    RunSummary? LatestSuccess(params string[] ids)=>ids
        .Select(id=>latest.TryGetValue(id,out var run)&&string.Equals(run.Status,"SUCESSO",StringComparison.Ordinal)?run:null)
        .Where(run=>run is not null)
        .OrderByDescending(run=>run!.FinishedAt)
        .FirstOrDefault();

    string ExecutionState(CommandDefinition command)
    {
        if(string.Equals(command.Id,"infra-blocking",StringComparison.Ordinal))
        {
            latest.TryGetValue(command.Id,out var direct);
            var evidence=LatestSuccess("infra-blocking","blocking","infra-model","calibration","gold-synthetic","infrastructure");
            if(direct is not null
               &&!string.Equals(direct.Status,"SUCESSO",StringComparison.Ordinal)
               &&(evidence is null||direct.FinishedAt>evidence.FinishedAt))
                return "FALHA";
            if(evidence is null)return "PENDENTE";
            if(latest.TryGetValue("infra-corpus",out var corpus)
               &&string.Equals(corpus.Status,"SUCESSO",StringComparison.Ordinal)
               &&corpus.FinishedAt>evidence.FinishedAt)
                return "DESATUALIZADO";
            return "PRONTO";
        }

        if(!latest.TryGetValue(command.Id,out var last))return "PENDENTE";
        if(string.Equals(last.Status,"PARCIAL",StringComparison.Ordinal))return "PENDENTE";
        if(!string.Equals(last.Status,"SUCESSO",StringComparison.Ordinal))return "FALHA";
        foreach(var dependency in command.Dependencies)
            if(latest.TryGetValue(dependency,out var dependencyRun)
               &&string.Equals(dependencyRun.Status,"SUCESSO",StringComparison.Ordinal)
               &&dependencyRun.FinishedAt>last.FinishedAt)
                return "DESATUALIZADO";
        return "PRONTO";
    }

    return Results.Ok(CommandCatalog.All.Select(x=>{
        latest.TryGetValue(x.Id,out var last);
        var flowBlockedReason=FlowBlockedReason(x,latest);
        var runtimeDisabledReason=runtime.DisabledReason(x);
        return new{
            x.Id,x.Title,x.Description,x.Implemented,x.CommandLine,x.DisplayCommand,x.Dependencies,x.DependencyNote,x.Surface,x.Stage,
            executionMode=x.ExecutionMode,
            order=Array.IndexOf(CommandCatalog.All,x),
            visible=x.Visible,
            destructive=x.Destructive,
            disabled=runtime.IsDisabled(x)||flowBlockedReason is not null,
            disabledReason=runtimeDisabledReason??flowBlockedReason,
            executionCount=counts.GetValueOrDefault(x.Id),
            executionState=ExecutionState(x),
            lastStatus=last?.Status,
            lastStartedAt=last?.StartedAt,
            lastFinishedAt=last?.FinishedAt,
            lastExecutionNumber=counts.GetValueOrDefault(x.Id)
        };
    }));
});

app.MapGet("/api/runs",async(RunStore store,CancellationToken ct)=>
    Results.Ok(await store.ListSessionSummariesAsync(ct)));

app.MapGet("/api/activity",(ConsoleActivityLog activity)=>Results.Ok(activity.Snapshot()));

// C3.3a: effective mode is OBSERVED from Docker + SQL, never persisted in
// session memory. Not a toggle; write actions will be added only after
// isolated lifecycle/RunOnce gates pass. No view of the canonical cluster.
app.MapGet("/api/workers/supervisor",async(
    HttpResponse response, ConsoleRuntimeMode runtime,
    IsolatedWorkerSupervisorStatusReader supervisor,
    IsolatedWorkerSupervisorModeController controller, CancellationToken ct)=>{
    response.Headers.CacheControl="no-store";
    if(!supervisor.Enabled(runtime))
        return Results.Conflict(new{
            error="Supervisão indisponível fora do perfil GitHub DEV descartável.",
            toggleAvailable=false
        });
    try
    {
        return Results.Ok(controller.WithFiniteWorkers(await supervisor.ReadAsync(runtime,ct)));
    }
    catch(OperationCanceledException) when(ct.IsCancellationRequested)
    {
        return Results.StatusCode(StatusCodes.Status408RequestTimeout);
    }
    catch(Exception)
    {
        // Fail closed on missing, ambiguous, stale or inaccessible Docker/SQL
        // evidence. Never report OFF merely because inspection failed.
        return Results.Json(new{
            error="Estado efetivo não comprovado no sandbox privado.",
            mode="ERRO",toggleAvailable=false
        },statusCode:StatusCodes.Status503ServiceUnavailable);
    }
});


// C3.3b1: a single global ON/OFF operation, never an individual start button.
// Only this private GitHub-hosted DEV profile has a lifecycle endpoint; all
// other modes refuse BEFORE inspecting or modifying any Docker resources.
app.MapPost("/api/workers/supervisor",async(
    HttpContext context, IsolatedWorkerModeRequest request,
    ConsoleRuntimeMode runtime, IsolatedWorkerSupervisorStatusReader reader,
    IsolatedWorkerSupervisorModeController controller, CancellationToken ct)=>{
    if(!reader.Enabled(runtime)
        ||context.Connection.RemoteIpAddress is not { } remote
        ||!System.Net.IPAddress.IsLoopback(remote))
        return Results.Conflict(new{error="Supervisão somente no CI DEV efêmero local."});
    try
    {
        var effective=await controller.SetAsync(request.Mode,runtime,ct);
        return Results.Ok(effective);
    }
    catch(ArgumentException ex)
    {
        return Results.BadRequest(new{error=ex.Message});
    }
    catch(InvalidOperationException)
    {
        return Results.Conflict(new{error="Modo não alterado ou não comprovado; consulte estado real."});
    }
    catch(OperationCanceledException) when(ct.IsCancellationRequested)
    {
        return Results.StatusCode(StatusCodes.Status408RequestTimeout);
    }
    catch(Exception)
    {
        return Results.Json(new{error="Transição privada falhou.",mode="ERRO"},
            statusCode:StatusCodes.Status503ServiceUnavailable);
    }
});

// C3.3b2: an individual FINITE worker cycle, not an individual continuous
// start. Only loopback HTTP in the isolated GitHub-hosted Development sandbox.
app.MapPost("/api/workers/{worker}/run-once",async(
    string worker, HttpContext context, ConsoleRuntimeMode runtime,
    IsolatedWorkerSupervisorStatusReader reader,
    IsolatedWorkerSupervisorModeController controller,
    IHostApplicationLifetime application)=>{
    if(!reader.Enabled(runtime)
        ||context.Connection.RemoteIpAddress is not { } remote
        ||!System.Net.IPAddress.IsLoopback(remote))
        return Results.Conflict(new{error="RunOnce restrito ao CI DEV efêmero local."});
    try
    {
        // RequestAborted is NOT a worker cancellation signal. Losing the
        // browser connection must not untrack a running oneoff; it remains
        // owned by the server until exit or application shutdown.
        return Results.Ok(await controller.RunOnceAsync(
            worker,runtime,application.ApplicationStopping));
    }
    catch(InvalidOperationException)
    {
        return Results.Conflict(new{error="RunOnce recusado por modo/worker/instância conflitante."});
    }
    catch(OperationCanceledException) when(application.ApplicationStopping.IsCancellationRequested)
    {
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
    catch(Exception)
    {
        return Results.Json(new{error="RunOnce privado não foi concluído."},
            statusCode:StatusCodes.Status503ServiceUnavailable);
    }
});

// C3.3c: confirmed resident lifecycle, loopback and disposable CI only.
app.MapPost("/api/workers/{worker}/stop", async(
    string worker, IsolatedWorkerStopRequest request, HttpContext context,
    ConsoleRuntimeMode runtime, IsolatedWorkerSupervisorStatusReader reader,
    IsolatedWorkerSupervisorModeController controller, CancellationToken ct) =>
{
    if (!reader.Enabled(runtime)
        || context.Connection.RemoteIpAddress is not { } remote
        || !System.Net.IPAddress.IsLoopback(remote))
        return Results.Conflict(new { error = "Operação restrita ao CI DEV isolado." });
    try
    {
        return Results.Ok(await controller.StopResidentAsync(
            worker, request.ContainerId, request.HostPid, request.Confirmed, runtime, ct));
    }
    catch (InvalidOperationException)
    {
        return Results.Conflict(new { error = "Identidade, confirmação ou estado não comprovado." });
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        return Results.StatusCode(StatusCodes.Status408RequestTimeout);
    }
    catch (Exception)
    {
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapPost("/api/session-counts/reset",([FromBody] string[] commands,RunStore store)=>
    Results.Ok(new{reset=store.ResetExecutionCounts(commands)}));

app.MapGet("/api/runs/{id:guid}",async(Guid id,RunStore store,CancellationToken ct)=>
    await store.GetAsync(id,ct) is { } run?Results.Ok(run):Results.NotFound());

app.MapPost("/api/commands/{command}/start",async(string command,[FromServices] LiveExecutionService live,[FromServices] ConsoleRuntimeMode runtime,[FromServices] IsolatedWorkerSupervisorStatusReader supervisor,[FromServices] RunStore store,[FromServices] ConsoleActivityLog activity,CancellationToken ct)=>{
    var definition=CommandCatalog.All.FirstOrDefault(x=>x.Id.Equals(command,StringComparison.OrdinalIgnoreCase));
    if(definition is null)return Results.NotFound();
    // The legacy Silver RunOnce executes cluster-bound PowerShell. Do not
    // allow it to target user SQL while private lifecycle mode is enabled.
    // A new sandbox-specific 3-worker RunOnce path comes in C3.3b2.
    if(definition.Id=="silver"&&supervisor.Enabled(runtime))
        return Results.Conflict(new{error="RunOnce isolado ainda não implementado; execução canônica bloqueada."});
    if(runtime.IsDisabled(definition))
        return Results.Conflict(new{error=runtime.DisabledReason(definition),mode=runtime.Mode});
    var latest=await store.LatestByCommandAsync(ct);
    var flowBlockedReason=FlowBlockedReason(definition,latest);
    if(flowBlockedReason is not null)
        return Results.Conflict(new{error=flowBlockedReason,mode=runtime.Mode});
    var started=live.StartCommand(definition);
    activity.Add("EXECUCAO",definition.Id,"INICIADA",$"{definition.Title} #{started.ExecutionNumber}");
    return Results.Accepted($"/api/runs/{started.Id}",started);
});

app.MapGet("/api/zip/contracts",async(GoldZipTemplateService service,ConsoleActivityLog activity,CancellationToken ct)=>{
    try
    {
        var contracts=await service.ListContractsAsync(ct);
        activity.Add("ZIP","carregar-contratos","SUCESSO",$"{contracts.Count} combinação(ões) utilizável(is).");
        return Results.Ok(contracts);
    }
    catch(Exception ex)
    {
        activity.Add("ZIP","carregar-contratos","FALHA",ex.Message);
        return Results.BadRequest(new{error=ex.Message});
    }
});

app.MapGet("/api/zip/template",async(string? contract,GoldZipTemplateService service,ConsoleActivityLog activity,CancellationToken ct)=>{
    try
    {
        var template=await service.GetAsync(contract,ct);
        activity.Add("ZIP","carregar-template","SUCESSO",template.ContractLabel);
        return Results.Ok(template);
    }
    catch(Exception ex)
    {
        activity.Add("ZIP","carregar-template","FALHA",ex.Message);
        return Results.BadRequest(new{error=ex.Message});
    }
});

app.MapGet("/api/semiblind/template",async(SemiblindDevService service,CancellationToken ct)=>{
    try{
        var t=await service.TemplateAsync(ct);
        return Results.Ok(new{gestor=t.Gestor,nomeCompleto=t.NomeCompleto,dataNascimento=t.DataNascimento,nomeMae=t.NomeMae,source=t.Source,pessoaUuid=t.PessoaUuid});
    }
    catch(Exception ex){return Results.BadRequest(new{error=ex.Message});}
});

app.MapPost("/api/semiblind/search",async(SemiblindDevRequest request,SemiblindDevService service,CancellationToken ct)=>{
    try{
        var result=await service.SearchAsync(request,ct);
        return Results.Text(result.Json,"application/json; charset=utf-8",statusCode:result.StatusCode);
    }
    catch(Exception ex){return Results.BadRequest(new{error=ex.Message});}
});

app.MapGet("/api/contracts",(ContractFileService service)=>Results.Ok(service.List()));
app.MapGet("/api/contracts/file",async(string path,ContractFileService service,CancellationToken ct)=>{
    try{return Results.Ok(await service.GetAsync(path,ct));}catch(Exception ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapPut("/api/contracts/file",async(ContractSaveRequest request,ContractFileService service,CancellationToken ct)=>{
    try{return Results.Ok(await service.SaveAsync(request,ct));}catch(Exception ex){return Results.BadRequest(new{error=ex.Message});}
});

app.MapGet("/api/layers/{layer}",async(string layer,int page,int pageSize,string? search,LayerBrowserService service,CancellationToken ct)=>{
    try{return Results.Ok(await service.BrowseAsync(layer,page,pageSize,search,ct));}
    catch(Exception ex){return Results.BadRequest(new{error=ex.Message});}
});

app.MapGet("/api/config/active",(ActiveConfigFileService service)=>Results.Ok(service.List()));
app.MapGet("/api/config/active/file",async(string path,ActiveConfigFileService service,CancellationToken ct)=>{
    try{return Results.Ok(await service.GetAsync(path,ct));}catch(Exception ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapPut("/api/config/active/file",async(ActiveConfigSaveRequest request,ActiveConfigFileService service,CancellationToken ct)=>{
    try{return Results.Ok(await service.SaveAsync(request,ct));}catch(Exception ex){return Results.BadRequest(new{error=ex.Message});}
});

app.MapPost("/api/zip/manual/start",static([FromBody] ManualZipRequest request,[FromServices] LiveExecutionService live,[FromServices] ConsoleActivityLog activity)=>{
    var started=live.StartManualZip(request);
    activity.Add("EXECUCAO","zip","INICIADA",$"Gerar ZIP de ingestão #{started.ExecutionNumber}");
    return Results.Accepted($"/api/runs/{started.Id}",started);
});

app.MapGet("/api/runs/{id:guid}/stream",async(Guid id,HttpResponse response,LiveExecutionService live,CancellationToken ct)=>{
    if(!live.Contains(id)){response.StatusCode=404;return;}
    response.ContentType="text/event-stream";
    response.Headers.CacheControl="no-cache";
    response.Headers.Connection="keep-alive";
    await live.StreamAsync(id,response,ct);
});

app.MapGet("/api/runs/{id:guid}/result",async(Guid id,RunStore store,IWebHostEnvironment env,CancellationToken ct)=>{
    var run=await store.GetAsync(id,ct);
    if(run?.Step.ResultPath is not { Length:>0 } path)return Results.NotFound();
    return ServeArtifact(path,env,false);
});

app.MapGet("/api/runs/{id:guid}/result/html",async(Guid id,RunStore store,IWebHostEnvironment env,CancellationToken ct)=>{
    var run=await store.GetAsync(id,ct);
    if(run?.Step.ResultPath is not { Length:>0 } path)return Results.NotFound();
    return ServeArtifact(path,env,true);
});

app.MapGet("/api/runs/{id:guid}/artifacts/{index:int}",async(Guid id,int index,RunStore store,IWebHostEnvironment env,CancellationToken ct)=>{
    var run=await store.GetAsync(id,ct);
    var artifacts=run?.Step.Artifacts??Array.Empty<string>();
    if(index<0||index>=artifacts.Count)return Results.NotFound();
    return ServeArtifact(artifacts[index],env,false);
});

app.MapGet("/api/runs/{id:guid}/artifacts/{index:int}/html",async(Guid id,int index,RunStore store,IWebHostEnvironment env,CancellationToken ct)=>{
    var run=await store.GetAsync(id,ct);
    var artifacts=run?.Step.Artifacts??Array.Empty<string>();
    if(index<0||index>=artifacts.Count)return Results.NotFound();
    return ServeArtifact(artifacts[index],env,true);
});

static string? FlowBlockedReason(CommandDefinition command,IReadOnlyDictionary<string,RunSummary> latest)
{
    if(!string.Equals(command.Id,"linkage",StringComparison.OrdinalIgnoreCase))return null;

    if(!latest.TryGetValue("ingestion",out var ingestion)
       ||!string.Equals(ingestion.Status,"SUCESSO",StringComparison.Ordinal))
        return "Envie um arquivo de ingestão antes de executar Identidade e Linkage.";

    if(!latest.TryGetValue("silver",out var silver)
       ||!string.Equals(silver.Status,"SUCESSO",StringComparison.Ordinal)
       ||silver.FinishedAt<ingestion.FinishedAt)
        return "A última Entrega ainda não concluiu Bronze → Silver. Execute primeiro 'Processar Bronze → Silver' e aguarde SUCESSO.";

    return null;
}

static IResult ServeArtifact(string path,IWebHostEnvironment env,bool html)
{
    var root=DevConsolePaths.FindSolutionRoot(env.ContentRootPath);
    var full=Path.GetFullPath(path);
    if(!full.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!File.Exists(full))
        return Results.NotFound();
    var ext=Path.GetExtension(full).ToLowerInvariant();
    if(html&&ext==".json")
    {
        try
        {
            var htmlText=FriendlyJsonHtml.Render(full,File.ReadAllText(full));
            return Results.Text(htmlText,"text/html; charset=utf-8");
        }
        catch(JsonException ex){return Results.BadRequest($"JSON inválido: {ex.Message}");}
    }
    if(html&&ext==".html")return Results.File(full,"text/html; charset=utf-8");
    var contentType=ext switch{".json"=>"application/json",".html"=>"text/html; charset=utf-8",".zip"=>"application/zip",".txt"=>"text/plain; charset=utf-8",".csv"=>"text/csv; charset=utf-8",_=>"application/octet-stream"};
    return Results.File(full,contentType,fileDownloadName:ext==".zip"?Path.GetFileName(full):null,enableRangeProcessing:true);
}

app.Run();

sealed record IsolatedWorkerStopRequest(string ContainerId, int HostPid, bool Confirmed);
