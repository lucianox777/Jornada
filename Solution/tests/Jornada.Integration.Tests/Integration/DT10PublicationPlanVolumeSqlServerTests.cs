using Microsoft.Data.SqlClient;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jornada.Operational.Sql;

namespace Jornada.Tests.Integration;

[TestFixture]
[Category("Integration")]
[Category("DT10Evidence")]
[Category("Performance")]
[NonParallelizable]
public sealed class DT10PublicationPlanVolumeSqlServerTests
{
    private static readonly JsonSerializerOptions EvidenceJsonOptions = new() { WriteIndented = true };

    [Test]
    public async Task Scalar_and_set_based_are_measured_relatively_at_growing_synthetic_volumes()
    {
        var cs = RequireSyntheticMeasurementConnection();
        var volumes = ParseVolumes();
        await using var connection = new SqlConnection(cs);
        connection.FireInfoMessageEventOnUserErrors = true;
        await connection.OpenAsync();
        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteCanonicalSchemaAsync(connection, databaseDir);
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Seed_Dev.sql"));

        var model = await ReadModelAsync(connection);
        var sources = await ReadSourcesAsync(connection, volumes.Max());
        Assert.That(sources.Count, Is.GreaterThanOrEqualTo(volumes.Max()),
            $"JornadaSyntheticDev precisa de ao menos {volumes.Max()} origens PROVISORIA sem CPF para a volumetria DT-10.");

        var measurements = new List<Measurement>();
        foreach (var volume in volumes)
        {
            var sample = sources.Take(volume).ToArray();
            var run = Guid.NewGuid();
            await SeedRunAsync(connection, run, model, sample);
            await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                tx.Save("before_scalar");
                var scalar = await MeasureScalarAsync(connection, tx, run, sample);
                tx.Rollback("before_scalar");
                var batch = await MeasureBatchAsync(connection, tx, run);
                measurements.Add(new(volume, scalar, batch));
            }
            finally
            {
                await tx.RollbackAsync();
            }
        }

        var json = JsonSerializer.Serialize(new
        {
            kind = "DT10_RELATIVE_PLAN_VOLUMETRY",
            database = "JornadaSyntheticDev",
            absoluteCapacityClaim = false,
            generatedAtUtc = DateTimeOffset.UtcNow,
            measurements
        }, EvidenceJsonOptions);
        var evidencePath = Path.Combine(TestContext.CurrentContext.WorkDirectory,
            $"dt10-plan-volume-{DateTime.UtcNow:yyyyMMddHHmmss}.json");
        await File.WriteAllTextAsync(evidencePath, json);
        TestContext.AddTestAttachment(evidencePath, "DT-10 relative plan/volumetry evidence (no SLA claim)");
        TestContext.Progress.WriteLine($"DT10_EVIDENCE_FILE={evidencePath}");

        Assert.Multiple(() =>
        {
            Assert.That(measurements, Has.Count.EqualTo(volumes.Length));
            Assert.That(measurements.Select(x => x.Volume), Is.Ordered.Ascending);
            Assert.That(measurements, Has.All.Matches<Measurement>(x => x.Scalar.DurationMs >= 0 && x.Batch.DurationMs >= 0));
            Assert.That(measurements, Has.All.Matches<Measurement>(x => x.Scalar.LogicalReads >= 0 && x.Batch.LogicalReads >= 0));
            Assert.That(measurements, Has.All.Matches<Measurement>(x =>
                !string.IsNullOrWhiteSpace(x.Scalar.PlanXml) && !string.IsNullOrWhiteSpace(x.Batch.PlanXml)));
        });
    }

    private static async Task<PathMeasurement> MeasureScalarAsync(
        SqlConnection connection, SqlTransaction tx, Guid run, IReadOnlyList<SourceFixture> sources)
    {
        long reads = 0;
        SqlInfoMessageEventHandler handler = (_, e) => reads += ParseLogicalReads(e.Message);
        connection.InfoMessage += handler;
        var sw = Stopwatch.StartNew();
        string? plan = null;
        try
        {
            foreach (var source in sources)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = tx;
                command.CommandTimeout = 120;
                command.CommandText = """
                    SET STATISTICS IO ON;
                    SET STATISTICS XML ON;
                    DECLARE @v BIGINT;
                    EXEC identidade.sp_publicar_resolucao_progressiva_linkage
                         @linkage_run_id=@run,@pessoa_observacao_id=@obs,@versao_resultado=@v OUTPUT;
                    SET STATISTICS XML OFF;
                    SET STATISTICS IO OFF;
                    """;
                command.Parameters.AddWithValue("@run", run);
                command.Parameters.AddWithValue("@obs", source.ObservationId);
                plan ??= await ExecuteAndCapturePlanAsync(command);
            }
        }
        finally
        {
            sw.Stop();
            connection.InfoMessage -= handler;
        }
        return new("scalar", sw.Elapsed.TotalMilliseconds, reads, plan ?? string.Empty);
    }

    private static async Task<PathMeasurement> MeasureBatchAsync(SqlConnection connection, SqlTransaction tx, Guid run)
    {
        long reads = 0;
        SqlInfoMessageEventHandler handler = (_, e) => reads += ParseLogicalReads(e.Message);
        connection.InfoMessage += handler;
        var sw = Stopwatch.StartNew();
        string? plan;
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = tx;
            command.CommandTimeout = 120;
            command.CommandText = """
                SET STATISTICS IO ON;
                SET STATISTICS XML ON;
                EXEC identidade.sp_publicar_resolucao_progressiva_linkage_lote @linkage_run_id=@run;
                SET STATISTICS XML OFF;
                SET STATISTICS IO OFF;
                """;
            command.Parameters.AddWithValue("@run", run);
            plan = await ExecuteAndCapturePlanAsync(command);
        }
        finally
        {
            sw.Stop();
            connection.InfoMessage -= handler;
        }
        return new("set-based", sw.Elapsed.TotalMilliseconds, reads, plan ?? string.Empty);
    }

    private static async Task<string?> ExecuteAndCapturePlanAsync(SqlCommand command)
    {
        await using var reader = await command.ExecuteReaderAsync();
        string? plan = null;
        do
        {
            if (reader.FieldCount == 1 && reader.GetName(0).Contains("Showplan", StringComparison.OrdinalIgnoreCase))
            {
                while (await reader.ReadAsync())
                    if (!reader.IsDBNull(0)) plan = reader.GetString(0);
            }
            else
            {
                while (await reader.ReadAsync()) { }
            }
        } while (await reader.NextResultAsync());
        return plan;
    }

    private static long ParseLogicalReads(string message)
    {
        long total = 0;
        foreach (Match match in Regex.Matches(message, @"logical reads (?<n>\d+)",
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            total += long.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture);
        return total;
    }

    private static int[] ParseVolumes()
    {
        var raw = Environment.GetEnvironmentVariable("JORNADA_DT10_VOLUMES") ?? "10,100,500";
        var values = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => int.Parse(x, CultureInfo.InvariantCulture)).Distinct().Order().ToArray();
        Assert.That(values.Length, Is.GreaterThanOrEqualTo(3));
        Assert.That(values[0], Is.GreaterThan(0));
        return values;
    }

    private static async Task SeedRunAsync(
        SqlConnection connection, Guid run, ModelFixture model, IReadOnlyList<SourceFixture> sources)
    {
        await using (var header = connection.CreateCommand())
        {
            header.CommandText = """
                INSERT identidade.linkage_run(
                    linkage_run_id,modelo_id,modelo_versao,tipo_run,status,limite_solicitado,escopo_json,batch_size,max_parallelism,
                    pessoa_observacao_id_high_watermark,registros_elegiveis,avaliados,resolvidos,nao_resolvidos,conflitos,
                    sem_candidato_no_bloco,solicitado_por,motivo,correlation_id,iniciado_em)
                VALUES(@run,@model,@version,N'FULL',N'EXECUTANDO',@count,N'{"test":"dt10-volume"}',@count,1,
                       @high,@count,@count,0,@count,0,@count,N'CI',N'DT10 relative measurement',NEWID(),SYSUTCDATETIME());
                """;
            header.Parameters.AddWithValue("@run", run);
            header.Parameters.AddWithValue("@model", model.ModelId);
            header.Parameters.AddWithValue("@version", model.Version);
            header.Parameters.AddWithValue("@count", sources.Count);
            header.Parameters.AddWithValue("@high", sources.Max(x => x.ObservationId));
            await header.ExecuteNonQueryAsync();
        }
        foreach (var source in sources)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT identidade.linkage_run_item(linkage_run_id,pessoa_observacao_id) VALUES(@run,@obs);
                INSERT identidade.linkage_resultado(
                    linkage_run_id,modelo_id,modelo_versao,pessoa_observacao_id,pessoa_uuid_resolvido,melhor_candidato_uuid,score_melhor,
                    segundo_candidato_uuid,score_segundo,margem,status,motivo,calculado_em,resultado_publicacao,pessoa_uuid_publicado,
                    status_publicacao,motivo_publicacao,pessoa_origem_id_publicado,politica_publicacao_versao,universo_referencia,publicado_em)
                VALUES(@run,@model,@version,@obs,NULL,NULL,0,NULL,NULL,NULL,N'NAO_RESOLVIDO',
                       N'SEM_CANDIDATO_NO_RULESET_BLOCKING',SYSUTCDATETIME(),N'NOVA_IDENTIDADE',@initial,N'RESOLVIDO',
                       N'NOVA_IDENTIDADE_APOS_BUSCA_COMPLETA',@source,N'LINKAGE_PROGRESSIVE_PUBLICATION_V1',
                       N'RUN_COMPLETO_DT10_VOLUME',SYSUTCDATETIME());
                """;
            command.Parameters.AddWithValue("@run", run);
            command.Parameters.AddWithValue("@model", model.ModelId);
            command.Parameters.AddWithValue("@version", model.Version);
            command.Parameters.AddWithValue("@obs", source.ObservationId);
            command.Parameters.AddWithValue("@initial", source.InitialUuid);
            command.Parameters.AddWithValue("@source", source.SourceId);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task<List<SourceFixture>> ReadSourcesAsync(SqlConnection connection, int count)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP(@count) po.pessoa_observacao_id,po.pessoa_origem_id,p.initial_uuid
            FROM silver.pessoa_observacao po
            JOIN identidade.pessoa_origem_progressiva p ON p.pessoa_origem_id=po.pessoa_origem_id
            WHERE po.pessoa_origem_id IS NOT NULL AND po.cpf IS NULL AND p.estado=N'PROVISORIA'
            ORDER BY po.pessoa_observacao_id;
            """;
        command.Parameters.AddWithValue("@count", count);
        var rows = new List<SourceFixture>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) rows.Add(new(reader.GetInt64(0), reader.GetInt64(1), reader.GetGuid(2)));
        return rows;
    }

    private static async Task<ModelFixture> ReadModelAsync(SqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT TOP(1) modelo_id,versao FROM identidade.modelo_linkage ORDER BY CASE WHEN status=N'ATIVO' THEN 0 ELSE 1 END,versao DESC;";
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new InvalidOperationException("Fixture sem modelo de Linkage.");
        return new(reader.GetGuid(0), reader.GetInt32(1));
    }

    private static string RequireSyntheticMeasurementConnection()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("JORNADA_DT10_EVIDENCE"), "1", StringComparison.Ordinal))
            Assert.Pass("DT-10 volumetria permanece desligada por padrão; defina JORNADA_DT10_EVIDENCE=1 para executar a evidência.");
        var cs = Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(cs)) Assert.Fail("Defina JORNADA_TEST_SQL_CONNECTION para JornadaSyntheticDev.");
        var db = new SqlConnectionStringBuilder(cs!).InitialCatalog;
        Assert.That(db, Is.EqualTo("JornadaSyntheticDev").IgnoreCase,
            "Medição DT-10 só pode executar no banco isolado JornadaSyntheticDev; JornadaLocal é proibido.");
        return cs!;
    }

    private sealed record SourceFixture(long ObservationId, long SourceId, Guid InitialUuid);
    private sealed record ModelFixture(Guid ModelId, int Version);
    private sealed record PathMeasurement(string Path, double DurationMs, long LogicalReads, string PlanXml);
    private sealed record Measurement(int Volume, PathMeasurement Scalar, PathMeasurement Batch);
}
