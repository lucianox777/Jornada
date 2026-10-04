using System.Text.Json;

sealed record ContractFileInfo(string Path,string Content);
sealed record ContractSaveRequest(string Path,string Content);

sealed class ContractFileService(IWebHostEnvironment env)
{
    string Root=>DevConsolePaths.FindSolutionRoot(env.ContentRootPath);
    string ContractsRoot=>Path.Combine(Root,"config","contracts");
    string SupportContractsRoot=>Path.GetFullPath(Path.Combine(Root,"..","ApoioSecretarias","config","contracts"));

    public IReadOnlyList<string> List()=>ContractRoots()
        .Where(x=>Directory.Exists(x.Root))
        .SelectMany(x=>Directory.EnumerateFiles(x.Root,"*.json",SearchOption.AllDirectories)
            .Select(p=>x.Prefix+Path.GetRelativePath(x.Root,p).Replace('\\','/')))
        .OrderBy(p=>p,StringComparer.OrdinalIgnoreCase).ToArray();

    public async Task<ContractFileInfo> GetAsync(string path,CancellationToken ct)
    {
        var full=Resolve(path);
        return new(path.Replace('\\','/'),await File.ReadAllTextAsync(full,ct));
    }

    public async Task<ContractFileInfo> SaveAsync(ContractSaveRequest request,CancellationToken ct)
    {
        using var _=JsonDocument.Parse(request.Content);
        var full=Resolve(request.Path);
        await File.WriteAllTextAsync(full,request.Content,ct);
        return new(request.Path.Replace('\\','/'),request.Content);
    }

    string Resolve(string path)
    {
        if(string.IsNullOrWhiteSpace(path))throw new InvalidDataException("Informe o arquivo de contrato.");
        var normalized=path.Replace('\\','/');
        var supportPrefix="ApoioSecretarias/config/contracts/";
        var full=normalized.StartsWith(supportPrefix,StringComparison.OrdinalIgnoreCase)
            ? Path.GetFullPath(Path.Combine(SupportContractsRoot,normalized[supportPrefix.Length..].Replace('/',Path.DirectorySeparatorChar)))
            : Path.GetFullPath(Path.Combine(Root,normalized.Replace('/',Path.DirectorySeparatorChar)));
        var allowed=ContractRoots().Any(x=>full.StartsWith(Path.GetFullPath(x.Root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase));
        if(!allowed||!string.Equals(Path.GetExtension(full),".json",StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Somente JSON dos catálogos de contratos da Solution ou ApoioSecretarias pode ser alterado.");
        if(!File.Exists(full))throw new FileNotFoundException("Arquivo de contrato não encontrado.",full);
        return full;
    }

    IEnumerable<(string Root,string Prefix)> ContractRoots()
    {
        yield return (ContractsRoot,string.Empty);
        yield return (SupportContractsRoot,"ApoioSecretarias/config/contracts/");
    }
}
