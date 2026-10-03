using System.Text.Json;

var builder=WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<RunStore>();
builder.Services.AddSingleton<LiveExecutionService>();

var app=builder.Build();

app.MapGet("/",()=>Results.Text(Page.Html,"text/html; charset=utf-8"));

app.MapGet("/api/commands",async(RunStore store,CancellationToken ct)=>{
    var counts=await store.CountByCommandAsync(ct);
    return Results.Ok(CommandCatalog.All.Select(x=>new{
        x.Id,x.Title,x.Description,x.Implemented,x.CommandLine,x.DisplayCommand,
        RunCount=counts.GetValueOrDefault(x.Id)
    }));
});

app.MapGet("/api/runs",async(RunStore store,CancellationToken ct)=>
    Results.Ok(await store.ListAsync(ct)));

app.MapGet("/api/runs/{id:guid}",async(Guid id,RunStore store,CancellationToken ct)=>
    await store.GetAsync(id,ct) is { } run?Results.Ok(run):Results.NotFound());

app.MapPost("/api/commands/{command}/start",(string command,LiveExecutionService live)=>{
    var definition=CommandCatalog.All.FirstOrDefault(x=>x.Id.Equals(command,StringComparison.OrdinalIgnoreCase));
    if(definition is null)return Results.NotFound();
    var id=live.StartCommand(definition);
    return Results.Accepted($"/api/runs/{id}",new{id});
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
    if(run?.Step.ResultPath is not { Length:>0 } path||!File.Exists(path))return Results.NotFound();
    var root=DevConsolePaths.FindSolutionRoot(env.ContentRootPath);
    var full=Path.GetFullPath(path);
    if(!full.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))return Results.BadRequest("Resultado fora da árvore da Solution.");
    var ext=Path.GetExtension(full).ToLowerInvariant();
    var contentType=ext switch{".json"=>"application/json",".zip"=>"application/zip",".txt"=>"text/plain","text/csv"=>"text/csv",_=>"application/octet-stream"};
    return Results.File(full,contentType,fileDownloadName:ext==".zip"?Path.GetFileName(full):null,enableRangeProcessing:true);
});

app.Run();
