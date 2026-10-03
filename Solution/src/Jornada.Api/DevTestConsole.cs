using System.Diagnostics;
using System.Text.Json;

namespace Jornada.Api;

public sealed record DevTestStep(string Name,string Command,int ExitCode,long DurationMs,string Output,string Error);
public sealed record DevTestExecution(Guid Id,string Action,DateTimeOffset StartedAt,DateTimeOffset FinishedAt,string Status,IReadOnlyList<DevTestStep> Steps,Dictionary<string,string?> Parameters);
public sealed record DevTestSession(Guid Id,DateTimeOffset StartedAt,string Name,Dictionary<string,string?> InitialParameters,List<DevTestExecution> Executions);

public sealed class DevTestConsoleStore : IDisposable
{
    private readonly string _root;
    private readonly SemaphoreSlim _gate=new(1,1);
    private static readonly JsonSerializerOptions JsonOptions=new(JsonSerializerDefaults.Web){WriteIndented=true};

    public DevTestConsoleStore(IWebHostEnvironment env)
    {
        var repository=DevelopmentSecurityPaths.FindRepositoryRoot(env.ContentRootPath);
        _root=Path.Combine(repository,"data","dev-test-console");
        Directory.CreateDirectory(_root);
    }

    public async Task<DevTestSession> StartAsync(string? name,Dictionary<string,string?> parameters,CancellationToken ct)
    {
        var session=new DevTestSession(Guid.NewGuid(),DateTimeOffset.UtcNow,string.IsNullOrWhiteSpace(name)?$"DEV-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}":name.Trim(),parameters,[]);
        await SaveAsync(session,ct); return session;
    }

    public async Task<IReadOnlyList<DevTestSession>> ListAsync(CancellationToken ct)
    {
        var result=new List<DevTestSession>();
        foreach(var path in Directory.EnumerateFiles(_root,"*.json").OrderByDescending(File.GetLastWriteTimeUtc))
        {
            await using var stream=File.OpenRead(path);
            var session=await JsonSerializer.DeserializeAsync<DevTestSession>(stream,JsonOptions,ct);
            if(session is not null) result.Add(session);
        }
        return result;
    }

    public async Task<DevTestSession?> GetAsync(Guid id,CancellationToken ct)
    {
        var path=Path.Combine(_root,$"{id:N}.json");
        if(!File.Exists(path)) return null;
        await using var stream=File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<DevTestSession>(stream,JsonOptions,ct);
    }

    public async Task<DevTestSession?> AppendAsync(Guid id,DevTestExecution execution,CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try {
            var session=await GetAsync(id,ct); if(session is null) return null;
            session.Executions.Add(execution); await SaveCoreAsync(session,ct); return session;
        } finally { _gate.Release(); }
    }

    private async Task SaveAsync(DevTestSession session,CancellationToken ct)
    { await _gate.WaitAsync(ct); try { await SaveCoreAsync(session,ct); } finally { _gate.Release(); } }

    private async Task SaveCoreAsync(DevTestSession session,CancellationToken ct)
    {
        var path=Path.Combine(_root,$"{session.Id:N}.json");
        var temp=path+".tmp";
        await using(var stream=File.Create(temp)) await JsonSerializer.SerializeAsync(stream,session,JsonOptions,ct);
        File.Move(temp,path,true);
    }
    public void Dispose()=>_gate.Dispose();
}

public sealed class DevTestCommandRunner
{
    private readonly IWebHostEnvironment _environment;
    public DevTestCommandRunner(IWebHostEnvironment environment)=>_environment=environment;

    public async Task<DevTestExecution> UpdateAndBuildAsync(Dictionary<string,string?> parameters,CancellationToken ct)
    {
        var started=DateTimeOffset.UtcNow;
        var root=DevelopmentSecurityPaths.FindRepositoryRoot(_environment.ContentRootPath);
        var steps=new List<DevTestStep>();
        steps.Add(await RunAsync(root,"git","status --short --branch",ct));
        steps.Add(await RunAsync(root,"git","pull --ff-only",ct));
        steps.Add(await RunAsync(root,"dotnet","restore Solution/Jornada.sln --locked-mode",ct));
        steps.Add(await RunAsync(root,"dotnet","build Solution/Jornada.sln --no-restore --configuration Release",ct));
        steps.Add(await RunAsync(root,"git","rev-parse HEAD",ct));
        var finished=DateTimeOffset.UtcNow;
        return new DevTestExecution(Guid.NewGuid(),"ATUALIZAR_COMPILAR_FONTE",started,finished,steps.All(x=>x.ExitCode==0)?"SUCESSO":"FALHA",steps,parameters);
    }

    private static async Task<DevTestStep> RunAsync(string cwd,string file,string arguments,CancellationToken ct)
    {
        var command=$"{file} {arguments}";
        var sw=Stopwatch.StartNew();
        using var process=new Process { StartInfo=new ProcessStartInfo(file,arguments){WorkingDirectory=cwd,RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true} };
        process.Start();
        var stdout=process.StandardOutput.ReadToEndAsync(ct);
        var stderr=process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        sw.Stop();
        return new DevTestStep(file,command,process.ExitCode,sw.ElapsedMilliseconds,await stdout,await stderr);
    }
}
