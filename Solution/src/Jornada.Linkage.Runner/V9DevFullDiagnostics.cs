using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Runner;

/// <summary>Disposable DEV FULL phase/SQL observation. Never exports SQL text, parameters, UUIDs or raw plans.</summary>
internal sealed class V9DevFullDiagnostics
{
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly ConcurrentQueue<object> phases = new();
    private string current = "full-start";
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

    internal void Phase(string phase)
    {
        Volatile.Write(ref current, phase);
        phases.Enqueue(new { phase, elapsedMs = clock.ElapsedMilliseconds });
    }

    private static string? Identifier(string? value) =>
        !string.IsNullOrEmpty(value) && value.Length <= 128 &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '[' or ']' or ' ' or '-')
            ? value : null;

    private static object[] Operators(string raw)
    {
        if (raw.Length > 4 * 1024 * 1024) return [];
        var document = XDocument.Parse(raw);
        return document.Descendants(Ns + "RelOp").Take(128).Select(node =>
        {
            var counters = node.Elements(Ns + "RunTimeInformation").Elements(Ns + "RunTimeCountersPerThread");
            double Sum(string name) => counters.Sum(x =>
                double.TryParse((string?)x.Attribute(name), NumberStyles.Float, CultureInfo.InvariantCulture,
                    out var value) && double.IsFinite(value) ? value : 0);
            return (object)new {
                nodeId = (int?)node.Attribute("NodeId"),
                physical = Identifier((string?)node.Attribute("PhysicalOp")),
                logical = Identifier((string?)node.Attribute("LogicalOp")),
                estimatedRows = (double?)node.Attribute("EstimateRows"),
                actualRows = Sum("ActualRows"), actualExecutions = Sum("ActualExecutions"),
                objects = node.Elements().SelectMany(x => x.Elements(Ns + "Object")).Select(x => new {
                    schema = Identifier((string?)x.Attribute("Schema")),
                    table = Identifier((string?)x.Attribute("Table")),
                    index = Identifier((string?)x.Attribute("Index"))
                }).ToArray()
            };
        }).ToArray();
    }

    internal async Task<T> RunAsync<T>(IOperationalSqlAdapter sql, Guid attempt, Func<Task<T>> run)
    {
        var output = Environment.GetEnvironmentVariable("RUNNER_TEMP");
        if (string.IsNullOrEmpty(output) || !Path.IsPathFullyQualified(output) || !Directory.Exists(output))
            throw new InvalidOperationException("RUNNER_V9_DEV_FULL_DIAGNOSTIC_ROOT");
        using var stop = new CancellationTokenSource();
        var samples = new List<object>();
        string status = "FAILED";
        clock.Restart();
        var sampler = SampleAsync();
        try
        {
            var result = await run();
            status = "FULL_COMPLETED";
            return result;
        }
        catch (SqlException e)
        {
            status = FormattableString.Invariant($"SQL_{e.Number}_{e.State}");
            throw;
        }
        finally
        {
            var elapsedMs = clock.ElapsedMilliseconds;
            await stop.CancelAsync();
            await sampler;
            await using var file = new FileStream(Path.Combine(output, "v9-full-diagnostic.json"),
                FileMode.CreateNew, FileAccess.Write);
            await JsonSerializer.SerializeAsync(file, new {
                schema = "V9_DEV_FULL_DIAGNOSTIC_V1", status, elapsedMs,
                finalPhase = Volatile.Read(ref current), phases = phases.ToArray(), samples,
                rawPlanExported = false, qualityMeasured = false
            });
        }

        async Task SampleAsync()
        {
            try
            {
                await using var conn = await sql.OpenAsync(stop.Token);
                await V9ControlledDevSelection.VerifyDisposableAttemptDatabaseAsync(conn, attempt, stop.Token);
                var knownPlans = new HashSet<string>(StringComparer.Ordinal);
                while (!stop.IsCancellationRequested && samples.Count < 900)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stop.Token);
                    await using var cmd = new SqlCommand("""
                        SELECT TOP (8) r.session_id,r.status,r.wait_type,r.cpu_time,r.total_elapsed_time,
                               r.logical_reads,r.reads,r.blocking_session_id,
                               CONVERT(varchar(18),r.query_hash,1),CONVERT(varchar(18),r.query_plan_hash,1),
                               CASE WHEN r.total_elapsed_time>=5000 THEN CONVERT(nvarchar(max),p.query_plan) END
                        FROM sys.dm_exec_requests r
                        JOIN sys.dm_exec_sessions s ON s.session_id=r.session_id
                        OUTER APPLY sys.dm_exec_query_statistics_xml(r.session_id) p
                        WHERE r.database_id=DB_ID() AND s.host_process_id=@pid AND r.session_id<>@@SPID
                        ORDER BY r.total_elapsed_time DESC,r.session_id;
                        """, conn) { CommandTimeout = 5 };
                    cmd.Parameters.Add("@pid", SqlDbType.Int).Value = Environment.ProcessId;
                    await using var reader = await cmd.ExecuteReaderAsync(stop.Token);
                    while (await reader.ReadAsync(stop.Token))
                    {
                        if (samples.Count >= 900) break;
                        var hash = reader.IsDBNull(8) ? null : reader.GetString(8);
                        var plan = reader.IsDBNull(9) ? null : reader.GetString(9);
                        var phase = Volatile.Read(ref current);
                        object[] ops = [];
                        var key = $"{phase}:{hash}:{plan}";
                        if (!reader.IsDBNull(10) && (knownPlans.Count < 64 && !knownPlans.Contains(key)))
                        {
                            try { ops = Operators(reader.GetString(10)); }
                            catch (Exception e) when (e is System.Xml.XmlException or FormatException or OverflowException)
                            { samples.Add(new { probeError = e.GetType().Name }); }
                            if (ops.Length > 0) knownPlans.Add(key);
                        }
                        samples.Add(new {
                            phase, elapsedMs = clock.ElapsedMilliseconds, session = reader.GetInt16(0),
                            requestStatus = Identifier(reader.GetString(1)), wait = reader.IsDBNull(2) ? null : Identifier(reader.GetString(2)),
                            cpuMs = reader.GetInt32(3), sqlElapsedMs = reader.GetInt32(4),
                            logicalReads = reader.GetInt64(5), reads = reader.GetInt64(6),
                            blockingSession = reader.GetInt16(7), queryHash = hash, planHash = plan, operators = ops
                        });
                    }
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            catch (SqlException e) { samples.Add(new { probeError = FormattableString.Invariant($"SQL_{e.Number}_{e.State}") }); }
            catch (Exception e) when (e is InvalidOperationException or InvalidCastException or System.Xml.XmlException or FormatException)
            { samples.Add(new { probeError = e.GetType().Name }); }
        }
    }
}
