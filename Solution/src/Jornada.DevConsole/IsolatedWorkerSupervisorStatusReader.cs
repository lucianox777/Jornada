using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

// C3.3a: strictly READ-ONLY supervisor observation. No lifecycle operations.
// Until C3.3b exists, the Console cannot toggle workers or run a new service.
// This opt-in is restricted to the GitHub-hosted disposable JornadaE2E project.
sealed record IsolatedWorkerState(
    string Worker, string State, int? HostPid, int RestartCount,
    string? InstanceId, int? SqlProcessId, long? HeartbeatAgeSeconds,
    string? ContainerId);

sealed record IsolatedWorkerSupervisorStatus(
    string Mode, string ComposeProject, bool SqlRunning,
    bool ApiReady, bool ResultadoApiLive,
    bool ToggleAvailable, IReadOnlyList<IsolatedWorkerState> Workers);

sealed class IsolatedWorkerSupervisorStatusReader
{
    private const string Repo = "lucianox777/Jornada";
    private static readonly Regex RunId = new("^[0-9]{6,16}$", RegexOptions.CultureInvariant);
    private static readonly Regex AttemptId = new("^[0-9]{1,3}$", RegexOptions.CultureInvariant);
    private static readonly Regex ContainerId = new("^[0-9a-f]{12,64}$", RegexOptions.CultureInvariant);
    private static readonly Regex Uuid = new("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", RegexOptions.CultureInvariant);

    private static readonly (string Service,string Component)[] Workers =
    [
        ("processor", "Processor"),
        ("operations-maintenance", "OperationsMaintenance"),
        ("bronze-maintenance", "BronzeMaintenance")
    ];

    // DOCKER_HOST/CONTEXT overrides could direct even harmless CLI operations to
    // another daemon. Never read the status of the ordinary host/cluster.
    public bool Enabled(ConsoleRuntimeMode mode)
    {
        var run = Environment.GetEnvironmentVariable("GITHUB_RUN_ID") ?? "";
        var attempt = Environment.GetEnvironmentVariable("GITHUB_RUN_ATTEMPT") ?? "";
        return mode.Mode == "DEV"
            && Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true"
            && Environment.GetEnvironmentVariable("CI") == "true"
            && Environment.GetEnvironmentVariable("GITHUB_REPOSITORY") == Repo
            && Environment.GetEnvironmentVariable("JORNADA_WORKERS_E2E_RUNTIME_TEST") == "true"
            && RunId.IsMatch(run) && AttemptId.IsMatch(attempt)
            && Environment.GetEnvironmentVariable("JORNADA_WORKERS_E2E_ID") == "ci" + run + attempt
            && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JORNADA_WORKERS_E2E_SQL_PASSWORD"))
            && (Environment.GetEnvironmentVariable("JORNADA_WORKERS_E2E_IMAGE_TAG") is null or "test")
            && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOCKER_HOST"))
            && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOCKER_CONTEXT"));
    }

    public async Task<IsolatedWorkerSupervisorStatus> ReadAsync(ConsoleRuntimeMode runtime, CancellationToken ct)
    {
        if (!Enabled(runtime))
            throw new InvalidOperationException("C3.3a indisponível fora do CI DEV descartável.");

        var project = "jornada-workers-e2e-" +
            Environment.GetEnvironmentVariable("JORNADA_WORKERS_E2E_ID");
        // Name is derived exclusively from the checked GitHub run+attempt,
        // NEVER from an untrusted HTTP path, query, header or request payload.
        var known = new Dictionary<string, DockerServiceSnapshot?>(StringComparer.Ordinal);
        foreach (var service in new[] { "sqlserver", "api", "resultado-api" }
                     .Concat(Workers.Select(x => x.Service)))
        {
            var ids = await DockerAsync(ct, null,
                "ps", "-aq",
                "--filter", "label=com.docker.compose.project=" + project,
                "--filter", "label=com.docker.compose.service=" + service);
            var lines = ids.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length > 1 || lines.Any(x => !ContainerId.IsMatch(x)))
                throw new InvalidOperationException("Identidade de serviço Docker ambígua no sandbox.");
            known[service] = lines.Length == 0
                ? null
                : await InspectVerifiedAsync(lines[0], project, service, ct);
        }

        var sql = known["sqlserver"];
        var api = known["api"];
        var resultado = known["resultado-api"];
        if (sql?.Running != true || api?.Running != true || resultado?.Running != true
            || sql?.Healthy != true || api?.Healthy != true || resultado?.Healthy != true)
            throw new InvalidOperationException("SQL/API/ResultadoApi privados não estão disponíveis.");

        // Inspect alone is insufficient: /health/ready exercises schema/SQL.
        await DockerAsync(ct, null, "exec", api.Id,
            "curl", "--max-time", "6", "-fsS", "http://127.0.0.1:5080/health/ready");
        await DockerAsync(ct, null, "exec", resultado.Id,
            "curl", "--max-time", "6", "-fsS", "http://127.0.0.1:5081/health");

        var state = new List<IsolatedWorkerState>(Workers.Length);
        foreach (var (service, component) in Workers)
        {
            var worker = known[service];
            if (worker is null || !worker.Running)
            {
                state.Add(new IsolatedWorkerState(
                    service, worker?.Restarting == true ? "REINICIANDO" : "PARADO",
                    null, worker?.RestartCount ?? 0, null, null, null,
                    worker?.Id));
                continue;
            }

            var heartbeat = await ReadHeartbeatAsync(sql.Id, component, ct);
            var ok = heartbeat is { SqlStatus: "RUNNING", Age: >= 0 and <= 45 }
                     && worker.RestartPolicy == "unless-stopped"
                     && worker.HostPid > 1;
            state.Add(new IsolatedWorkerState(
                service, worker.Restarting ? "REINICIANDO" : ok ? "ATIVO" : "ERRO",
                worker.HostPid, worker.RestartCount,
                heartbeat?.InstanceId, heartbeat?.ProcessId, heartbeat?.Age,
                worker.Id));
        }

        var active = state.Count(x => x.State == "ATIVO");
        var existsRunning = Workers.Any(x => known[x.Service]?.Running == true);
        var hasRestarting = state.Any(x => x.State == "REINICIANDO");
        var modeValue = !existsRunning && !hasRestarting ? "OFF"
            : active == Workers.Length ? "ON"
            : hasRestarting ? "REINICIANDO"
            : "ERRO";
        // This is EFFECTIVE state inferred from actual Docker + SQL heartbeat,
        // never a stale in-memory flag or a request body's desired state.
        return new IsolatedWorkerSupervisorStatus(
            modeValue, project, true, true, true, false, state);
    }

    private static async Task<DockerServiceSnapshot> InspectVerifiedAsync(
        string id, string project, string service, CancellationToken ct)
    {
        using var json = JsonDocument.Parse(await DockerAsync(ct, null,
            "inspect", "--format", "{{json .}}", id));
        var value = json.RootElement;
        var labels = value.GetProperty("Config").GetProperty("Labels");
        if (labels.GetProperty("com.docker.compose.project").GetString() != project
            || labels.GetProperty("com.docker.compose.service").GetString() != service)
            throw new InvalidOperationException("Inspeção Docker não pertence ao sandbox autorizado.");
        var state = value.GetProperty("State");
        var running = state.GetProperty("Running").GetBoolean();
        var restarting = state.GetProperty("Restarting").GetBoolean();
        var pid = state.GetProperty("Pid").GetInt32();
        var health = state.TryGetProperty("Health", out var healthValue)
            ? healthValue.GetProperty("Status").GetString() : null;
        var policy = value.GetProperty("HostConfig")
            .GetProperty("RestartPolicy").GetProperty("Name").GetString();
        return new DockerServiceSnapshot(
            id, running, restarting, pid, value.GetProperty("RestartCount").GetInt32(),
            policy, health is null ? null : health == "healthy");
    }

    private static async Task<SqlHeartbeat?> ReadHeartbeatAsync(
        string sqlId, string component, CancellationToken ct)
    {
        // component is from the hard-coded Workers allowlist only.
        var password = Environment.GetEnvironmentVariable("JORNADA_WORKERS_E2E_SQL_PASSWORD");
        var query = "SET NOCOUNT ON; SELECT TOP(1) CONCAT("
            + "CONVERT(varchar(36),instance_id),'|',status,'|',"
            + "DATEDIFF_BIG(SECOND,heartbeat_em,SYSUTCDATETIME()),'|',process_id)"
            + " FROM controle.runtime_componente"
            + " WHERE node_id=N'NODE2' AND componente=N'" + component + "'"
            + " ORDER BY heartbeat_em DESC;";
        var raw = await DockerAsync(ct, password,
            "exec", "-e", "SQLCMDPASSWORD", sqlId,
            "/opt/mssql-tools18/bin/sqlcmd",
            "-S", "localhost", "-U", "sa", "-C", "-b", "-I", "-l", "7",
            "-d", "JornadaE2E", "-W", "-h", "-1", "-Q", query);
        var fields = string.Concat(raw.Where(x => x is not '\r' and not '\n')).Trim().Split('|');
        if (fields.Length == 1 && fields[0].Length == 0) return null;
        if (fields.Length != 4 || !Uuid.IsMatch(fields[0])
            || !long.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var age)
            || !int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var sqlPid))
            throw new InvalidOperationException("Heartbeat SQL do worker inválido.");
        return new SqlHeartbeat(fields[0], fields[1], sqlPid, age);
    }

    private static async Task<string> DockerAsync(
        CancellationToken ct, string? sqlPassword, params string[] args)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        var start = new ProcessStartInfo("docker")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        if (sqlPassword is not null) start.Environment["SQLCMDPASSWORD"] = sqlPassword;
        using var process = new Process { StartInfo = start };
        if (!process.Start())
            throw new InvalidOperationException("Docker CLI privado indisponível.");
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errors = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var text = await output;
            _ = await errors; // NEVER log the CLI stderr: it may contain secrets.
            if (process.ExitCode != 0)
                throw new InvalidOperationException("Consulta somente leitura ao Docker/SQL privado falhou.");
            return text.Trim();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Only kill the short-lived docker *CLI* child if it timed out.
            // Never invoke Docker stop/kill/down/prune against any container.
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw new InvalidOperationException("Timeout ao consultar estado do sandbox privado.");
        }
    }

    private sealed record DockerServiceSnapshot(
        string Id, bool Running, bool Restarting, int HostPid, int RestartCount,
        string? RestartPolicy, bool? Healthy);
    private sealed record SqlHeartbeat(
        string InstanceId, string SqlStatus, int ProcessId, long Age);
}
