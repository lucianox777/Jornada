using System.Text.Json;

sealed record ContractFileInfo(string Path,string Content);
sealed record ContractSaveRequest(string Path,string Content);

sealed class ContractFileService(IWebHostEnvironment env)
{
    string Root=>DevConsolePaths.FindSolutionRoot(env.ContentRootPath);
    string ContractsRoot=>Path.Combine(Root,"config","contracts");

    public IReadOnlyList<string> List()=>Directory.EnumerateFiles(ContractsRoot,"*.json",SearchOption.AllDirectories)
        .Select(p=>Path.GetRelativePath(Root,p).Replace('\\','/'))
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
        var full=Path.GetFullPath(Path.Combine(Root,path.Replace('/',Path.DirectorySeparatorChar)));
        var prefix=Path.GetFullPath(ContractsRoot).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        if(!full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)||!string.Equals(Path.GetExtension(full),".json",StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Somente JSON dentro de config/contracts pode ser alterado.");
        if(!File.Exists(full))throw new FileNotFoundException("Arquivo de contrato não encontrado.",full);
        return full;
    }
}
