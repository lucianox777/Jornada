using System.Text.Json;

sealed record ActiveConfigInfo(string Path,string FullPath,string[] RuntimePaths,bool RestartRequired,string Content);
sealed record ActiveConfigSaveRequest(string Path,string Content);

sealed class ActiveConfigFileService(IWebHostEnvironment env)
{
    string Root=>DevConsolePaths.FindSolutionRoot(env.ContentRootPath);

    public IReadOnlyList<object> List()
    {
        var files=new List<string>();
        var config=Path.Combine(Root,"config");
        if(Directory.Exists(config))
            files.AddRange(Directory.EnumerateFiles(config,"*.json",SearchOption.AllDirectories)
                .Where(p=>!p.StartsWith(Path.Combine(config,"contracts")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)));
        var cluster=Path.Combine(Root,"install","windows-production","Jornada.Cluster.Test.json");
        if(File.Exists(cluster))files.Add(cluster);
        return files.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p=>p,StringComparer.OrdinalIgnoreCase)
            .Select(p=>{var rel=Relative(p);return (object)new{path=rel,fullPath=p,runtimePaths=RuntimePaths(rel),restartRequired=RestartRequired(rel)};}).ToArray();
    }

    public async Task<ActiveConfigInfo> GetAsync(string path,CancellationToken ct)
    {
        var full=Resolve(path); var content=await File.ReadAllTextAsync(full,ct); using var _=JsonDocument.Parse(content);
        var rel=Relative(full); return new(rel,full,RuntimePaths(rel),RestartRequired(rel),content);
    }

    public async Task<ActiveConfigInfo> SaveAsync(ActiveConfigSaveRequest request,CancellationToken ct)
    {
        using var parsed=JsonDocument.Parse(request.Content);
        var normalized=JsonSerializer.Serialize(parsed.RootElement,DevConsoleJson.Pretty);
        var full=Resolve(request.Path); await File.WriteAllTextAsync(full,normalized,ct);
        var rel=Relative(full); return new(rel,full,RuntimePaths(rel),RestartRequired(rel),normalized);
    }

    string Resolve(string path)
    {
        if(string.IsNullOrWhiteSpace(path))throw new InvalidDataException("Informe o arquivo de configuração.");
        var full=Path.GetFullPath(Path.Combine(Root,path.Replace('/',Path.DirectorySeparatorChar)));
        var configRoot=Path.GetFullPath(Path.Combine(Root,"config"))+Path.DirectorySeparatorChar;
        var contractsRoot=Path.GetFullPath(Path.Combine(Root,"config","contracts"))+Path.DirectorySeparatorChar;
        var cluster=Path.GetFullPath(Path.Combine(Root,"install","windows-production","Jornada.Cluster.Test.json"));
        var allowed=(full.StartsWith(configRoot,StringComparison.OrdinalIgnoreCase)&&!full.StartsWith(contractsRoot,StringComparison.OrdinalIgnoreCase))
                    ||full.Equals(cluster,StringComparison.OrdinalIgnoreCase);
        if(!allowed||!full.EndsWith(".json",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Arquivo fora do conjunto de configurações JSON ativas da Console DEV.");
        if(!File.Exists(full))throw new FileNotFoundException("Arquivo de configuração não encontrado.",full);
        return full;
    }

    string Relative(string path)=>Path.GetRelativePath(Root,path).Replace('\\','/');
    static bool RestartRequired(string rel)=>rel.StartsWith("install/windows-production/",StringComparison.OrdinalIgnoreCase)||rel.StartsWith("config/security/",StringComparison.OrdinalIgnoreCase);
    static string[] RuntimePaths(string rel)=>rel switch{
        "install/windows-production/Jornada.Cluster.Test.json"=>["/etc/jornada/Jornada.Cluster.Test.json"],
        "config/security/test-access-keys.json"=>["/opt/jornada/config/security/test-access-keys.json"],
        _ when rel.StartsWith("config/",StringComparison.OrdinalIgnoreCase)=>[$"/opt/jornada/{rel}"],
        _=>[]
    };
}
