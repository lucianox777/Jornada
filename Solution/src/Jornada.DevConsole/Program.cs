using System.Text.Json;

var builder=WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<RunStore>();
builder.Services.AddSingleton<LiveExecutionService>();
builder.Services.AddSingleton<GoldZipTemplateService>();
builder.Services.AddSingleton<SemiblindDevService>();
builder.Services.AddSingleton<ContractFileService>();
builder.Services.AddSingleton<ActiveConfigFileService>();

var app=builder.Build();

app.MapGet("/",()=>Results.Text(Page.Html,"text/html; charset=utf-8"));

app.MapGet("/api/commands",async(RunStore store,CancellationToken ct)=>{
    var counts=await store.CountByCommandAsync(ct);
    return Results.Ok(CommandCatalog.All.Where(x=>x.Visible).Select(x=>new{
        x.Id,x.Title,x.Description,x.Implemented,x.CommandLine,x.DisplayCommand,x.Dependencies,x.DependencyNote,
        RunCount=x.Id switch{
            "zip"=>counts.GetValueOrDefault("zip")+counts.GetValueOrDefault("ingestion")+counts.GetValueOrDefault("pipeline-status"),
            "linkage"=>counts.GetValueOrDefault("linkage")+counts.GetValueOrDefault("replay"),
            "gold-synthetic"=>counts.GetValueOrDefault("gold-synthetic")+counts.GetValueOrDefault("blocking"),
            "configuration"=>counts.GetValueOrDefault("contract-bundle"),
            _=>counts.GetValueOrDefault(x.Id)
        }
    }));
});

app.MapGet("/api/runs",async(RunStore store,CancellationToken ct)=>
    Results.Ok(await store.ListSessionSummariesAsync(ct)));

app.MapGet("/api/runs/{id:guid}",async(Guid id,RunStore store,CancellationToken ct)=>
    await store.GetAsync(id,ct) is { } run?Results.Ok(run):Results.NotFound());

app.MapPost("/api/commands/{command}/start",(string command,LiveExecutionService live)=>{
    var definition=CommandCatalog.All.FirstOrDefault(x=>x.Id.Equals(command,StringComparison.OrdinalIgnoreCase));
    if(definition is null)return Results.NotFound();
    var id=live.StartCommand(definition);
    return Results.Accepted($"/api/runs/{id}",new{id});
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

app.MapGet("/api/config/active",(ActiveConfigFileService service)=>Results.Ok(service.List()));
app.MapGet("/api/config/active/file",async(string path,ActiveConfigFileService service,CancellationToken ct)=>{
    try{return Results.Ok(await service.GetAsync(path,ct));}catch(Exception ex){return Results.BadRequest(new{error=ex.Message});}
});
app.MapPut("/api/config/active/file",async(ActiveConfigSaveRequest request,ActiveConfigFileService service,CancellationToken ct)=>{
    try{return Results.Ok(await service.SaveAsync(request,ct));}catch(Exception ex){return Results.BadRequest(new{error=ex.Message});}
});

app.MapPost("/api/zip/manual/start",(ManualZipRequest request,LiveExecutionService live)=>{
    var id=live.StartManualZip(request);
    return Results.Accepted($"/api/runs/{id}",new{id});
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
