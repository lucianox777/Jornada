using System.Collections.Concurrent;
using System.Diagnostics;

// C3.3b1: backend mode changes in one process only, exclusively for the
// disposable GitHub-hosted SQL/JornadaE2E project. No host cluster access.
// This gate will also serialize future RunOnce operations before C3.3b2.
sealed class IsolatedWorkerSupervisorModeController(
    IsolatedWorkerSupervisorStatusReader reader,
    LiveExecutionService live,
    ConsoleActivityLog activity,
    IWebHostEnvironment env) : IDisposable
{
    private readonly SemaphoreSlim transition = new(1, 1);
    private readonly ConcurrentDictionary<string,byte> activeFinite =
        new(StringComparer.Ordinal);

    public IReadOnlyList<string> ActiveFiniteWorkers => activeFinite.Keys.OrderBy(x=>x,StringComparer.Ordinal).ToArray();

    // DI owns this singleton and disposes its gate on application shutdown.
    public void Dispose() => transition.Dispose();

    public async Task<IsolatedWorkerSupervisorStatus> SetAsync(
        string requested, ConsoleRuntimeMode runtime, CancellationToken ct)
    {
        if (!reader.Enabled(runtime))
            throw new InvalidOperationException("Modo de trabalhadores indisponível fora de CI DEV isolada.");
        if (requested is not ("ON" or "OFF"))
            throw new ArgumentException("Modo precisa ser ON ou OFF.", nameof(requested));

        await transition.WaitAsync(ct);
        try
        {
            // A finite one-off may be active even though the 3 resident services
            // are correctly OFF. Never queue an implicit ON behind its work.
            if(requested=="ON"&&!activeFinite.IsEmpty)
                throw new InvalidOperationException(
                    "RunOnce isolado em execução; ON requer confirmação explícita de interrupção.");
            var before = await reader.ReadAsync(runtime, ct);
            if (before.Mode is not ("OFF" or "ON"))
                throw new InvalidOperationException("Modo atual não comprovado; operação rejeitada sem efeitos.");

            if (before.Mode == requested)
                return before; // idempotent, no Compose command.

            if (requested == "ON" && live.HasActiveWorkerRunOnce())
            {
                // C3.3b2 will add explicit operator confirmation/cancellation.
                // Never silently kill or race a finite RunOnce process.
                throw new InvalidOperationException(
                    "RunOnce em execução. Confirmação e parada finita exigidas antes de ativar.");
            }

            try
            {
                await ApplyPrivateModeAsync(requested, env, ct);
                // The CLI process exit 0 only proves that Compose returned;
                // the *real* source of truth is Docker PID/restart + SQL heartbeat.
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                deadline.CancelAfter(TimeSpan.FromSeconds(90));
                while (true)
                {
                    var effective = await reader.ReadAsync(runtime, deadline.Token);
                    if (effective.Mode == requested)
                    {
                        activity.Add("SUPERVISAO", requested, "SUCESSO",
                            $"C3.3b1 · {requested} · três workers · {effective.ComposeProject}");
                        return effective;
                    }

                    // A partial healthy state is never a successful transition.
                    await Task.Delay(TimeSpan.FromSeconds(2), deadline.Token);
                }
            }
            catch
            {
                activity.Add("SUPERVISAO", requested, "ERRO",
                    "Transição não comprovada no SQL/Docker privado.");
                throw;
            }
        }
        finally
        {
            transition.Release();
        }
    }

    private static readonly HashSet<string> AllowedFiniteWorkers =
        new(["processor", "operations-maintenance", "bronze-maintenance"],
            StringComparer.Ordinal);

    public async Task<IsolatedWorkerRunOnceResult> RunOnceAsync(
        string worker, ConsoleRuntimeMode runtime, CancellationToken ct)
    {
        if(!reader.Enabled(runtime) || !AllowedFiniteWorkers.Contains(worker))
            throw new InvalidOperationException("RunOnce fora da allowlist/projeto DEV isolado.");

        // Admission and supervisor transitions share the same semaphore.
        // Do NOT hold it for the entire finite workload; SetAsync must be
        // able to reject ON immediately while this subprocess runs.
        await transition.WaitAsync(ct);
        try
        {
            if(!activeFinite.IsEmpty || live.HasActiveWorkerRunOnce())
                throw new InvalidOperationException("RunOnce já está em execução.");
            var effective=await reader.ReadAsync(runtime,ct);
            if(effective.Mode!="OFF")
                throw new InvalidOperationException("RunOnce só é autorizado com supervisão OFF comprovada.");
            if(!activeFinite.TryAdd(worker,0))
                throw new InvalidOperationException("RunOnce concorrente não autorizado.");
        }
        finally
        {
            transition.Release();
        }

        try
        {
            await ApplyPrivateRunOnceAsync(worker,env,ct);
            var after=await reader.ReadAsync(runtime,ct);
            if(after.Mode!="OFF")
                throw new InvalidOperationException("RunOnce terminou sem preservar OFF.");
            activity.Add("RUN_ONCE",worker,"SUCESSO",
                "Worker finito, independente, apenas JornadaE2E.");
            return new IsolatedWorkerRunOnceResult(worker,"CONCLUIDO",0);
        }
        catch
        {
            activity.Add("RUN_ONCE",worker,"ERRO",
                "Execução finita não concluiu no banco DEV privado.");
            throw;
        }
        finally
        {
            activeFinite.TryRemove(worker,out _);
        }
    }

    private static async Task ApplyPrivateRunOnceAsync(
        string worker, IWebHostEnvironment env, CancellationToken ct)
    {
        var root=DevConsolePaths.FindSolutionRoot(env.ContentRootPath);
        var script=Path.Combine(root,"scripts","console-private-worker-runonce.py");
        if(!File.Exists(script))
            throw new InvalidOperationException("Script RunOnce privado indisponível.");
        var psi=new ProcessStartInfo("python3"){
            WorkingDirectory=root,
            UseShellExecute=false,
            CreateNoWindow=true,
            RedirectStandardOutput=true,
            RedirectStandardError=true,
        };
        psi.ArgumentList.Add(script);
        psi.ArgumentList.Add(worker);
        psi.Environment["JORNADA_RUNTIME_MODE"]="DEV";
        using var process=new Process{StartInfo=psi};
        if(!process.Start())
            throw new InvalidOperationException("RunOnce privado não iniciou.");
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(115));
        try
        {
            var output=process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errors=process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            _=await output;
            _=await errors; // never log stderr/output with possible secrets
            if(process.ExitCode!=0)
                throw new InvalidOperationException("RunOnce privado retornou código de erro.");
        }
        catch(OperationCanceledException)
        {
            // Only the CLI process started by this request. If a Compose
            // oneoff remains, C3.3b1 ON refuses its existence fail-closed.
            if(!process.HasExited)process.Kill(entireProcessTree:true);
            throw;
        }
    }

    private static async Task ApplyPrivateModeAsync(
        string requested, IWebHostEnvironment env, CancellationToken ct)
    {
        var root = DevConsolePaths.FindSolutionRoot(env.ContentRootPath);
        var path = Path.Combine(root, "scripts", "console-private-worker-mode.py");
        if (!File.Exists(path))
            throw new InvalidOperationException("Controlador privado não encontrado.");

        var psi = new ProcessStartInfo("python3")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add(path);
        psi.ArgumentList.Add(requested);
        // Inherited env already constrained by reader.Enabled(runtime);
        // no shell and no arbitrary user-supplied command/path arguments.
        psi.Environment["JORNADA_RUNTIME_MODE"] = "DEV";
        using var process = new Process { StartInfo = psi };
        if (!process.Start())
            throw new InvalidOperationException("Controlador privado não iniciou.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            _ = await stdout;
            _ = await stderr; // never log process output: secrets may be present.
            if (process.ExitCode != 0)
                throw new InvalidOperationException("Transição worker privada falhou; consultar CI E2E.");
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true); // only this child's CLI process
            throw;
        }
    }
}

sealed record IsolatedWorkerModeRequest(string Mode);

sealed record IsolatedWorkerRunOnceResult(string Worker,string State,int ExitCode);
