using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;

// C3.3b1: backend mode changes in one process only, exclusively for the
// disposable GitHub-hosted SQL/JornadaE2E project. No host cluster access.
// This gate will also serialize future RunOnce operations before C3.3b2.
sealed class IsolatedWorkerSupervisorModeController(
    IsolatedWorkerSupervisorStatusReader reader,
    LiveExecutionService live,
    ConsoleActivityLog activity,
    IsolatedWorkerAuditJournal audit,
    IWebHostEnvironment env) : IDisposable
{
    private readonly SemaphoreSlim transition = new(1, 1);
    private readonly ConcurrentDictionary<string,byte> activeFinite =
        new(StringComparer.Ordinal);

    // An opaque server-owned finite identity is never inferred from a socket,
    // Docker PID or client-provided string. It is NOT permission to cancel.
    // A separate confirmed cancellation implementation must consume the
    // nonce atomically and verify an actual removed CI-only oneoff.
    private FiniteRunConfirmation? finiteConfirmation;
    private sealed record FiniteRunConfirmation(
        Guid RunId, string Worker, string Nonce, DateTimeOffset ExpiresAtUtc);

    public async Task<IsolatedWorkerRunOnceChallenge> ReadCancelChallengeAsync(
        string worker, ConsoleRuntimeMode runtime, CancellationToken ct)
    {
        if (!reader.Enabled(runtime) || !AllowedFiniteWorkers.Contains(worker))
            throw new InvalidOperationException("Desafio de RunOnce fora do CI DEV isolado.");
        await transition.WaitAsync(ct);
        try
        {
            var run = finiteConfirmation;
            if (run is null || run.Worker != worker
                || !activeFinite.ContainsKey(worker)
                || run.ExpiresAtUtc <= DateTimeOffset.UtcNow)
                throw new InvalidOperationException("Nenhum RunOnce confirmado neste worker.");
            var effective = await reader.ReadAsync(runtime, ct);
            if (effective.Mode != "OFF"
                || !ReferenceEquals(finiteConfirmation,run)
                || !activeFinite.ContainsKey(worker))
                throw new InvalidOperationException("RunOnce já terminou ou modo não é OFF.");
            // Read-only token, no Docker/PID mutation and no implicit ON.
            return new IsolatedWorkerRunOnceChallenge(
                run.RunId, run.Worker, run.Nonce, run.ExpiresAtUtc);
        }
        finally { transition.Release(); }
    }

    public IReadOnlyList<string> ActiveFiniteWorkers => activeFinite.Keys.OrderBy(x=>x,StringComparer.Ordinal).ToArray();

    public IsolatedWorkerSupervisorStatus WithFiniteWorkers(IsolatedWorkerSupervisorStatus status)
    {
        if(activeFinite.IsEmpty)return status;
        return status with {
            Workers=status.Workers.Select(x=>
                activeFinite.ContainsKey(x.Worker) && x.State=="PARADO"
                    ? x with {State="RUN_ONCE"}
                    : x).ToArray()
        };
    }

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

            audit.Record("SUPERVISAO", requested, "ADMITIDO");
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
                        audit.Record("SUPERVISAO", requested, "SUCESSO");
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
                audit.Record("SUPERVISAO", requested, "ERRO");
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

    // C3.3c: immutable request identity. A resident must be re-observed
    // before a confirmed action; client-provided PID alone is insufficient.
    public static bool MatchesResidentIdentity(
        IsolatedWorkerState observed, string worker, string containerId, int hostPid)
    {
        return observed.Worker == worker
            && observed.State == "ATIVO"
            && observed.ContainerId == containerId
            && observed.HostPid == hostPid
            && hostPid > 1;
    }

    public async Task<IsolatedWorkerSupervisorStatus> StopResidentAsync(
        string worker, string containerId, int hostPid, bool confirmed,
        ConsoleRuntimeMode runtime, CancellationToken ct)
    {
        if (!reader.Enabled(runtime) || !confirmed
            || !AllowedFiniteWorkers.Contains(worker)
            || containerId.Length != 64
            || !containerId.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
            throw new InvalidOperationException("Identidade ou confirmação inválida.");

        await transition.WaitAsync(ct);
        try
        {
            if (!activeFinite.IsEmpty || live.HasActiveWorkerRunOnce())
                throw new InvalidOperationException("RunOnce em andamento.");
            var before = await reader.ReadAsync(runtime, ct);
            if (before.Mode != "ON"
                || !MatchesResidentIdentity(before.Workers.Single(x => x.Worker == worker),
                    worker, containerId, hostPid))
                throw new InvalidOperationException("Identidade do residente não comprovada.");

            audit.Record("PARAR_PROCESSO", worker, "ADMITIDO");
            var root = DevConsolePaths.FindSolutionRoot(env.ContentRootPath);
            var script = Path.Combine(root, "scripts", "console-private-worker-stop.py");
            if (!File.Exists(script))
                throw new InvalidOperationException("Operação privada indisponível.");
            var psi = new ProcessStartInfo("python3")
            {
                WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var arg in new[] { script, worker, containerId,
                         hostPid.ToString(System.Globalization.CultureInfo.InvariantCulture) })
                psi.ArgumentList.Add(arg);
            psi.Environment["JORNADA_RUNTIME_MODE"] = "DEV";
            using var process = new Process { StartInfo = psi };
            if (!process.Start())
                throw new InvalidOperationException("Operação privada não iniciou.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            _ = await stdout;
            _ = await stderr;
            if (process.ExitCode != 0)
                throw new InvalidOperationException("Operação privada rejeitada.");
            var after = await reader.ReadAsync(runtime, ct);
            if (after.Mode != "ERRO"
                || after.Workers.Single(x => x.Worker == worker).State != "PARADO"
                || after.Workers.Where(x => x.Worker != worker).Any(x => x.State != "ATIVO"))
                throw new InvalidOperationException("Estado final não comprovado.");
            audit.Record("PARAR_PROCESSO", worker, "SUCESSO");
            activity.Add("PARAR_PROCESSO", worker, "SUCESSO",
                "C3.3c: residente privado parado com confirmação e identidade verificada.");
            return after;
        }
        catch
        {
            audit.Record("PARAR_PROCESSO", worker, "ERRO");
            activity.Add("PARAR_PROCESSO", worker, "ERRO",
                "C3.3c: operação não comprovada.");
            throw;
        }
        finally { transition.Release(); }
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
            audit.Record("RUN_ONCE",worker,"ADMITIDO");
            if(!activeFinite.TryAdd(worker,0))
                throw new InvalidOperationException("RunOnce concorrente não autorizado.");
            // Random 256-bit nonce, never persisted nor inserted into audit.
            // Versioning/expiry belong to this specific active server process.
            var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .TrimEnd('=').Replace('+','-').Replace('/','_');
            finiteConfirmation = new FiniteRunConfirmation(
                Guid.NewGuid(),worker,nonce,DateTimeOffset.UtcNow.AddMinutes(2));
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
            audit.Record("RUN_ONCE",worker,"SUCESSO");
            activity.Add("RUN_ONCE",worker,"SUCESSO",
                "Worker finito, independente, apenas JornadaE2E.");
            return new IsolatedWorkerRunOnceResult(worker,"CONCLUIDO",0);
        }
        catch
        {
            audit.Record("RUN_ONCE",worker,"ERRO");
            activity.Add("RUN_ONCE",worker,"ERRO",
                "Execução finita não concluiu no banco DEV privado.");
            throw;
        }
        finally
        {
            activeFinite.TryRemove(worker,out _);
            // The server owns the challenge, not the disconnected HTTP client.
            // Invalidating on exit makes stale confirmations useless.
            if(finiteConfirmation?.Worker==worker)
                finiteConfirmation=null;
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
sealed record IsolatedWorkerRunOnceChallenge(
    Guid RunId, string Worker, string ConfirmationNonce, DateTimeOffset ExpiresAtUtc);
