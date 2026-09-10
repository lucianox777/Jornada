from pathlib import Path


def replace_between(path: Path, start_marker: str, end_marker: str, replacement: str) -> None:
    text = path.read_text(encoding="utf-8")
    start = text.index(start_marker)
    end = text.index(end_marker, start)
    path.write_text(text[:start] + replacement + text[end:], encoding="utf-8")


api = Path("Solution/src/Jornada.Api/SqlApiServices.cs")
replace_between(
    api,
    "    public async Task<IngestionStatusResponse?> GetStatusAsync(AccessContext context, Guid entregaId, CancellationToken ct)\n",
    "    private static async Task<ResolvedIngestionContext> ResolveContextAsync(\n",
    '''    public async Task<IngestionStatusResponse?> GetStatusAsync(AccessContext context, Guid entregaId, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);

        Guid resolvedEntregaId;
        string status;
        DateTimeOffset recebidoEm;
        DateTimeOffset ultimaAtualizacao;

        // O Processor grava lote -> entrega. A consulta antiga lia entrega -> lote em uma única
        // instrução e podia formar um ciclo de locks durante a publicação final. Mantemos as leituras
        // em instruções independentes para liberar o shared lock da Entrega antes de tocar Lote.
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT e.entrega_id,e.status,e.recebido_em,e.ultima_atualizacao
                FROM ingestao.entrega e
                JOIN ref.gestor g ON g.gestor_id=e.gestor_id
                WHERE e.entrega_id=@entrega_id AND g.codigo=@gestor;
                """;
            command.Parameters.AddWithValue("@entrega_id", entregaId);
            command.Parameters.Add(new SqlParameter("@gestor", SqlDbType.NVarChar, 30) { Value = context.GestorCodigo });
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return null;
            resolvedEntregaId = reader.GetGuid(0);
            status = reader.GetString(1);
            recebidoEm = reader.GetDateTimeOffset(2);
            ultimaAtualizacao = reader.GetDateTimeOffset(3);
        }

        // Preserva a semântica existente de Erro sem manter locks simultâneos nas duas tabelas.
        string? erro = null;
        await using (var errorCommand = connection.CreateCommand())
        {
            errorCommand.CommandText = """
                SELECT TOP(1) l.erro_codigo
                FROM ingestao.lote l
                WHERE l.entrega_id=@entrega_id AND l.erro_codigo IS NOT NULL
                ORDER BY l.atualizado_em DESC,l.lote_seq DESC;
                """;
            errorCommand.Parameters.AddWithValue("@entrega_id", resolvedEntregaId);
            var value = await errorCommand.ExecuteScalarAsync(ct);
            if (value is not null && value is not DBNull)
                erro = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        return new IngestionStatusResponse(
            resolvedEntregaId,
            status,
            recebidoEm,
            ultimaAtualizacao,
            erro);
    }

''',
)

sh = Path("Solution/scripts/local-e2e.sh")
replace_between(
    sh,
    "wait_processed(){\n",
    "compose_sql(){\n",
    '''wait_processed(){
  local id="$1" out="$2" status='' code=''
  for _ in $(seq 1 120); do
    code="$(curl -sS -o "$out" -w '%{http_code}' "$API_URL/api/v1/ingestao/entregas/$id" -H 'X-Jornada-Gestor: SEHAB' -H "X-Jornada-Access-Key: $access_key" || true)"
    if [[ "$code" != 200 ]]; then
      echo "ERRO: consulta de status da Entrega $id retornou HTTP $code." >&2
      [[ -f "$out" ]] && cat "$out" >&2 || true
      return 1
    fi
    status="$(json_get "$out" status)"
    [[ "$status" == PROCESSADA ]] && return 0
    [[ "$status" == REJEITADA || "$status" == QUARENTENA ]] && { echo "ERRO: Entrega $id terminou $status" >&2; return 1; }
    sleep 1
  done
  echo "ERRO: timeout aguardando Entrega $id; último status=$status" >&2; return 1
}
''',
)

ps1 = Path("Solution/scripts/local-e2e.ps1")
text = ps1.read_text(encoding="utf-8")
start = text.index("    function Wait-Processed([string]$id,[string]$outFile) {\n")
end = text.index("    function Sql([string]$query) {\n", start)
replacement = '''    function Wait-Processed([string]$id,[string]$outFile) {
        for ($i=0; $i -lt 120; $i++) {
            $code = (& curl.exe -sS -o $outFile -w '%{http_code}' "$ApiUrl/api/v1/ingestao/entregas/$id" -H 'X-Jornada-Gestor: SEHAB' -H "X-Jornada-Access-Key: $accessKey" | Out-String).Trim()
            if ($code -ne '200') {
                $body = if (Test-Path $outFile) { Get-Content -Raw -Encoding UTF8 $outFile } else { '<sem corpo>' }
                throw "Consulta de status da Entrega $id retornou HTTP $code. Corpo: $body"
            }
            $status = (Read-Json $outFile).status
            if ($status -eq 'PROCESSADA') { return }
            if ($status -in @('REJEITADA','QUARENTENA')) { throw "Entrega $id terminou $status." }
            Start-Sleep -Seconds 1
        }
        throw "Timeout aguardando Entrega $id."
    }
'''
ps1.write_text(text[:start] + replacement + text[end:], encoding="utf-8")

test = Path("Solution/tests/Jornada.Integration.Tests/Integration/IngestionStatusConcurrencyTests.cs")
test.write_text(r'''using System.Data;
using Jornada.Api;
using Jornada.Contracts;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Integration;

[TestFixture]
[Category("Integration")]
[NonParallelizable]
public sealed class IngestionStatusConcurrencyTests
{
    private static readonly Guid EntregaId = Guid.Parse("20000000-0000-4000-8000-000000000001");
    private static readonly Guid LoteId = Guid.Parse("20000000-0000-4000-8000-000000000002");

    [Test]
    public async Task Status_polling_does_not_deadlock_with_processor_lote_then_entrega_lock_order()
    {
        var connectionString = RequireIntegrationConnection();
        await PrepareAsync(connectionString);

        var service = new SqlIngestionService(new OperationalSqlAdapter(connectionString), null!);
        var context = new AccessContext(Guid.Empty, AccessCredentialType.GESTOR, "SEHAB", "SEHAB", null, [], []);

        await using var writer = new SqlConnection(connectionString);
        await writer.OpenAsync();
        await using var tx = (SqlTransaction)await writer.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        await using (var lockLote = writer.CreateCommand())
        {
            lockLote.Transaction = tx;
            lockLote.CommandText = "UPDATE ingestao.lote SET atualizado_em=SYSDATETIMEOFFSET() WHERE lote_id=@lote_id;";
            lockLote.Parameters.AddWithValue("@lote_id", LoteId);
            Assert.That(await lockLote.ExecuteNonQueryAsync(), Is.EqualTo(1));
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var statusTask = service.GetStatusAsync(context, EntregaId, cts.Token);

        // Dá tempo para a leitura chegar à consulta de Lote. Na implementação antiga, a mesma
        // instrução ainda mantinha lock na Entrega, formando o ciclo com lote -> entrega do Processor.
        await Task.Delay(300, cts.Token);

        await using (var finishEntrega = writer.CreateCommand())
        {
            finishEntrega.Transaction = tx;
            finishEntrega.CommandText = "UPDATE ingestao.entrega SET ultima_atualizacao=SYSDATETIMEOFFSET() WHERE entrega_id=@entrega_id;";
            finishEntrega.Parameters.AddWithValue("@entrega_id", EntregaId);
            Assert.That(await finishEntrega.ExecuteNonQueryAsync(cts.Token), Is.EqualTo(1));
        }

        await tx.CommitAsync(cts.Token);
        var response = await statusTask;

        Assert.That(response, Is.Not.Null);
        Assert.That(response!.EntregaId, Is.EqualTo(EntregaId));
        Assert.That(response.Status, Is.EqualTo("PROCESSANDO"));
    }

    [Test]
    public async Task Query_split_preserves_latest_lote_error()
    {
        var connectionString = RequireIntegrationConnection();
        await PrepareAsync(connectionString);

        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE ingestao.lote
                   SET status='REJEITADO',erro_codigo='TESTE_STATUS_ERRO',atualizado_em=SYSDATETIMEOFFSET()
                 WHERE lote_id=@lote_id;
                UPDATE ingestao.entrega
                   SET status='REJEITADA',ultima_atualizacao=SYSDATETIMEOFFSET()
                 WHERE entrega_id=@entrega_id;
                """;
            command.Parameters.AddWithValue("@lote_id", LoteId);
            command.Parameters.AddWithValue("@entrega_id", EntregaId);
            await command.ExecuteNonQueryAsync();
        }

        var service = new SqlIngestionService(new OperationalSqlAdapter(connectionString), null!);
        var context = new AccessContext(Guid.Empty, AccessCredentialType.GESTOR, "SEHAB", "SEHAB", null, [], []);
        var response = await service.GetStatusAsync(context, EntregaId, CancellationToken.None);

        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Status, Is.EqualTo("REJEITADA"));
        Assert.That(response.Erro, Is.EqualTo("TESTE_STATUS_ERRO"));
    }

    private static async Task PrepareAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Fase1.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Seed_Dev.sql"));

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ingestao.lote
               SET status='PROCESSANDO',erro_codigo=NULL,atualizado_em=SYSDATETIMEOFFSET()
             WHERE lote_id=@lote_id;
            UPDATE ingestao.entrega
               SET status='PROCESSANDO',ultima_atualizacao=SYSDATETIMEOFFSET()
             WHERE entrega_id=@entrega_id;
            """;
        command.Parameters.AddWithValue("@lote_id", LoteId);
        command.Parameters.AddWithValue("@entrega_id", EntregaId);
        await command.ExecuteNonQueryAsync();
    }

    private static string RequireIntegrationConnection()
    {
        var connectionString = Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString)) Assert.Ignore("Defina JORNADA_TEST_SQL_CONNECTION.");
        var csb = new SqlConnectionStringBuilder(connectionString!);
        var db = csb.InitialCatalog ?? string.Empty;
        if (!db.Contains("test", StringComparison.OrdinalIgnoreCase)
            && !db.Contains("dev", StringComparison.OrdinalIgnoreCase)
            && !db.Contains("local", StringComparison.OrdinalIgnoreCase))
            Assert.Fail("Banco de integração deve conter Test, Dev ou Local.");
        return connectionString!;
    }
}
''', encoding="utf-8")
