using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Runner;

/// <summary>DEV disposable census diagnostics; no SQL text, XML, parameters or row data leave the private session.</summary>
internal static class V9DevCensusDiagnostics
{
    private static readonly XNamespace PlanNs = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

    private static string? SafeIdentifier(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 128) return null;
        return value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '[' or ']' or ' ' or '-')
            ? value : null;
    }

    internal static async Task ReadAsync(OperationalSqlAdapter sql, SqlConnection connection,
        SqlCommand census, StringBuilder json, Guid attempt)
    {
        await using var session = new SqlCommand("SELECT @@SPID;", connection);
        var spid = Convert.ToInt32(await session.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        var outputRoot = Environment.GetEnvironmentVariable("RUNNER_TEMP")
            ?? throw new InvalidOperationException("RUNNER_V9_DEV_E2E_CENSUS_DIAGNOSTIC_ROOT");
        if (!Path.IsPathFullyQualified(outputRoot) || !Directory.Exists(outputRoot))
            throw new InvalidOperationException("RUNNER_V9_DEV_E2E_CENSUS_DIAGNOSTIC_ROOT");
        using var stop = new CancellationTokenSource();
        var samples = new List<object>();
        var watch = Stopwatch.StartNew();
        var sampler = SampleAsync();
        string status = "FAILED";
        try
        {
            await using var reader = await census.ExecuteReaderAsync();
            while (await reader.ReadAsync()) json.Append(reader.GetString(0));
            status = "QUERY_COMPLETED";
        }
        catch (SqlException e)
        {
            status = FormattableString.Invariant($"SQL_{e.Number}_{e.State}");
            throw;
        }
        finally
        {
            await stop.CancelAsync();
            try { await sampler; }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            await using var file = new FileStream(Path.Combine(outputRoot, "v9-census-diagnostic.json"),
                FileMode.CreateNew, FileAccess.Write);
            await JsonSerializer.SerializeAsync(file, new {
                schema = "V9_DEV_CENSUS_DIAGNOSTIC_V1", status,
                elapsedMs = watch.ElapsedMilliseconds, timeoutSeconds = census.CommandTimeout,
                planSamples = samples, rawPlanExported = false, qualityMeasured = false
            });
        }

        async Task SampleAsync()
        {
            try
            {
                await using var probe = await sql.OpenAsync(stop.Token);
                await V9ControlledDevSelection.VerifyDisposableAttemptDatabaseAsync(probe, attempt, stop.Token);
                while (!stop.IsCancellationRequested && samples.Count < 30)
                {
                    await Task.Delay(TimeSpan.FromSeconds(4), stop.Token);
                    await using var cmd = new SqlCommand("""
                        SELECT r.status,r.wait_type,r.cpu_time,r.total_elapsed_time,r.logical_reads,r.reads,
                               CONVERT(nvarchar(max),p.query_plan)
                        FROM sys.dm_exec_requests r
                        OUTER APPLY sys.dm_exec_query_statistics_xml(r.session_id) p
                        WHERE r.session_id=@session AND r.database_id=DB_ID();
                        """, probe) { CommandTimeout = 5 };
                    cmd.Parameters.Add("@session", SqlDbType.Int).Value = spid;
                    await using var reader = await cmd.ExecuteReaderAsync(stop.Token);
                    if (!await reader.ReadAsync(stop.Token)) continue;
                    var ops = new List<object>();
                    if (!reader.IsDBNull(6))
                    {
                        var raw = reader.GetString(6);
                        if (raw.Length <= 8 * 1024 * 1024)
                        {
                            var doc = XDocument.Parse(raw);
                            foreach (var node in doc.Descendants(PlanNs + "RelOp").Take(512))
                            {
                                var counters = node.Elements(PlanNs + "RunTimeInformation")
                                    .Elements(PlanNs + "RunTimeCountersPerThread");
                                double Sum(string name) => counters.Sum(x =>
                                    double.TryParse((string?)x.Attribute(name), NumberStyles.Float,
                                        CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : 0);
                                ops.Add(new {
                                    nodeId = (int?)node.Attribute("NodeId"),
                                    physical = SafeIdentifier((string?)node.Attribute("PhysicalOp")),
                                    logical = SafeIdentifier((string?)node.Attribute("LogicalOp")),
                                    estimatedRows = (double?)node.Attribute("EstimateRows"),
                                    actualRows = Sum("ActualRows"), actualExecutions = Sum("ActualExecutions"),
                                    objects = node.Elements().SelectMany(x => x.Elements(PlanNs + "Object")).Select(x => new {
                                        schema = SafeIdentifier((string?)x.Attribute("Schema")),
                                        table = SafeIdentifier((string?)x.Attribute("Table")),
                                        index = SafeIdentifier((string?)x.Attribute("Index"))
                                    }).ToArray()
                                });
                            }
                        }
                    }
                    samples.Add(new {
                        elapsedMs = watch.ElapsedMilliseconds, requestStatus = SafeIdentifier(reader.GetString(0)),
                        wait = reader.IsDBNull(1) ? null : SafeIdentifier(reader.GetString(1)),
                        cpuMs = reader.GetInt32(2), sqlElapsedMs = reader.GetInt32(3),
                        logicalReads = reader.GetInt64(4), reads = reader.GetInt64(5), operators = ops
                    });
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            catch (SqlException e) { samples.Add(new { probeError = FormattableString.Invariant($"SQL_{e.Number}_{e.State}") }); }
            catch (Exception e) when (e is InvalidOperationException or System.Xml.XmlException or FormatException)
            { samples.Add(new { probeError = e.GetType().Name }); }
        }
    }
}
