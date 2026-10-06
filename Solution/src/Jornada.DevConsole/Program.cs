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

var app=builder.Build();

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

    string ExecutionState(CommandDefinition command)
    {
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
            visible=x.Visible,
            destructive=x.Destructive,
            disabled=runtime.IsDisabled(x)||flowBlockedReason is not null,
            disabledReason=runtimeDisabledReason??flowBlockedReason,
            executionCount=counts.GetValueOrDefault(x.Id),
            executionState=ExecutionState(x),
            lastStatus=last?.Status,
            lastStartedAt=last?.StartedAt,
            lastFinishedAt=last?.FinishedAt,
            lastExecutionNumber=last?.ExecutionNumber??0
        };
    }));
});

app.MapGet("/api/runs",async(RunStore store,CancellationToken ct)=>
    Results.Ok(await store.ListSessionSummariesAsync(ct)));

app.MapGet("/api/runs/{id:guid}",async(Guid id,RunStore store,CancellationToken ct)=>
    await store.GetAsync(id,ct) is { } run?Results.Ok(run):Results.NotFound());

app.MapPost("/api/commands/{command}/start",async(string command,[FromServices] LiveExecutionService live,[FromServices] ConsoleRuntimeMode runtime,[FromServices] RunStore store,CancellationToken ct)=>{
    var definition=CommandCatalog.All.FirstOrDefault(x=>x.Id.Equals(command,StringComparison.OrdinalIgnoreCase));
    if(definition is null)return Results.NotFound();
    if(runtime.IsDisabled(definition))
        return Results.Conflict(new{error=runtime.DisabledReason(definition),mode=runtime.Mode});
    var latest=await store.LatestByCommandAsync(ct);
    var flowBlockedReason=FlowBlockedReason(definition,latest);
    if(flowBlockedReason is not null)
        return Results.Conflict(new{error=flowBlockedReason,mode=runtime.Mode});
    var started=live.StartCommand(definition);
    return Results.Accepted($"/api/runs/{started.Id}",started);
});

app.MapGet("/api/zip/template",async(GoldZipTemplateService service,CancellationToken ct)=>{
    try{return Results.Ok(await service.GetAsync(ct));}
    catch(Exception ex){return Results.BadRequest(new{error=ex.Message});}
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

app.MapPost("/api/zip/manual/start",static([FromBody] ManualZipRequest request,[FromServices] LiveExecutionService live)=>{
    var started=live.StartManualZip(request);
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
