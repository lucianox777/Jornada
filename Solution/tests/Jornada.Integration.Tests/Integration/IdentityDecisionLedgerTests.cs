using Jornada.Api;
using Jornada.Contracts;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;
using NUnit.Framework;
using System.Data;

namespace Jornada.Tests.Integration;

[TestFixture, Category("Integration"), NonParallelizable]
public sealed class IdentityDecisionLedgerTests
{
    [Test]
    public async Task Governed_case_service_writes_actor_bound_append_only_decision_events()
    {
        var connectionString = RequireIntegrationConnection();
        await PrepareAsync(connectionString);

        var (credentialId, observationId, originalUuid) = await LoadFixtureAsync(connectionString);
        var context = new AccessContext(
            credentialId, AccessCredentialType.GESTOR, "SMADS", "SMADS", null, [], []);

        var service = new SqlIdentityCorrectionService(new OperationalSqlAdapter(connectionString));
        var openCorrelation = Guid.NewGuid();
        var applyCorrelation = Guid.NewGuid();
        var ato = $"TESTE-LEDGER-{Guid.NewGuid():N}";

        var opened = await service.OpenCaseAsync(
            context,
            new IdentityGovernedCaseOpenRequest(
                "OUTRO", [observationId], ato,
                "Prova de autoria canônica e atomicidade do ledger de decisão.",
                new IdentityDecisionEvidence("DOCUMENTO_VERIFICADO", "RG")),
            openCorrelation,
            CancellationToken.None);

        var applied = await service.ApplyCaseAsync(
            context,
            opened.CasoId,
            new IdentityGovernedCaseApplyRequest(
                [new IdentityCorrectionGroupRequest("ORIGINAL", originalUuid, [observationId])]),
            applyCorrelation,
            CancellationToken.None);

        Assert.That(applied.Status, Is.EqualTo("APLICADO"));

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT e.evento_tipo,e.operacao_id,e.credencial_id,e.codigo_publico,
                   e.correlation_id,e.ato_referencia,e.resultado_codigo,g.codigo,
                   e.evidencia_tipo,e.documento_tipo_codigo
            FROM auditoria.decisao_identidade_evento e
            JOIN ref.gestor g ON g.gestor_id=e.gestor_id
            WHERE e.caso_id=@case
            ORDER BY e.decisao_identidade_evento_id;
            """;
        command.Parameters.AddWithValue("@case", opened.CasoId);

        var rows = new List<(string Event, Guid Operation, Guid Credential, string PublicCode, Guid? Correlation, string? Act, string Result, string Gestor, string Evidence, string? Document)>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                rows.Add((
                    reader.GetString(0),
                    reader.GetGuid(1),
                    reader.GetGuid(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetGuid(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    reader.GetString(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9)));
            }
        }

        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(rows[0].Event, Is.EqualTo("CASO_CONFLITO_ABERTO"));
            Assert.That(rows[0].Result, Is.EqualTo("ABERTO"));
            Assert.That(rows[0].Correlation, Is.EqualTo(openCorrelation));
            Assert.That(rows[0].Evidence, Is.EqualTo("DOCUMENTO_VERIFICADO"));
            Assert.That(rows[0].Document, Is.EqualTo("RG"));
            Assert.That(rows[1].Event, Is.EqualTo("CASO_CONFLITO_APLICADO"));
            Assert.That(rows[1].Result, Is.EqualTo("APLICADO"));
            Assert.That(rows[1].Correlation, Is.EqualTo(applyCorrelation));
            Assert.That(rows[1].Evidence, Is.EqualTo("ATO_GOVERNADO_SEM_NOVA_EVIDENCIA"));
            Assert.That(rows[1].Document, Is.Null);
            Assert.That(rows.All(r => r.Credential == credentialId), Is.True);
            Assert.That(rows.All(r => r.PublicCode == "SMADS" && r.Gestor == "SMADS"), Is.True);
            Assert.That(rows.All(r => r.Act == ato), Is.True);
            Assert.That(rows.All(r => r.Operation != Guid.Empty), Is.True);
            Assert.That(rows.Select(r => r.Operation).Distinct().Count(), Is.EqualTo(2));
            Assert.That(rows.All(r => r.Operation != openCorrelation && r.Operation != applyCorrelation), Is.True,
                "operacao_id é gerado pelo SQL Server e não reutiliza correlation_id.");
        });

        await using var immutable = connection.CreateCommand();
        immutable.CommandText = """
            UPDATE auditoria.decisao_identidade_evento
            SET justificativa=N'alteração proibida'
            WHERE caso_id=@case;
            """;
        immutable.Parameters.AddWithValue("@case", opened.CasoId);
        var immutableError = Assert.ThrowsAsync<SqlException>(async () => await immutable.ExecuteNonQueryAsync());
        Assert.That(immutableError!.Number, Is.EqualTo(51941));
    }

    [Test]
    public void Document_evidence_without_document_type_is_rejected_before_mutation()
    {
        var service = new SqlIdentityCorrectionService(new OperationalSqlAdapter(
            "Server=localhost;Database=unused;Integrated Security=true;"));
        var context = new AccessContext(
            Guid.NewGuid(), AccessCredentialType.GESTOR, "SMADS", "SMADS", null, [], []);

        var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
            await service.OpenCaseAsync(
                context,
                new IdentityGovernedCaseOpenRequest(
                    "OUTRO", [1], "TESTE",
                    "Evidência documental sem tipo deve falhar antes de abrir conexão.",
                    new IdentityDecisionEvidence("DOCUMENTO_VERIFICADO")),
                Guid.NewGuid(),
                CancellationToken.None));

        Assert.That(ex!.Message, Does.Contain("documentoTipoCodigo"));
    }

    [Test]
    public async Task Ledger_failure_rolls_back_governed_identity_mutation()
    {
        var connectionString = RequireIntegrationConnection();
        await PrepareAsync(connectionString);

        var (credentialId, observationId, originalUuid) = await LoadFixtureAsync(connectionString);
        var context = new AccessContext(
            credentialId, AccessCredentialType.GESTOR, "SMADS", "SMADS", null, [], []);
        var service = new SqlIdentityCorrectionService(new OperationalSqlAdapter(connectionString));
        var ato = $"TESTE-LEDGER-ROLLBACK-{Guid.NewGuid():N}";
        const string trigger = "auditoria.tr_test_decision_ledger_failure";

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        try
        {
            await using (var create = connection.CreateCommand())
            {
                create.CommandText = $"""
                    CREATE OR ALTER TRIGGER {trigger}
                    ON auditoria.decisao_identidade_evento
                    AFTER INSERT
                    AS
                    BEGIN
                      SET NOCOUNT ON;
                      THROW 51990,'Falha injetada no ledger canônico.',1;
                    END;
                    """;
                await create.ExecuteNonQueryAsync();
            }

            var ex = Assert.ThrowsAsync<SqlException>(async () =>
                await service.OpenCaseAsync(
                    context,
                    new IdentityGovernedCaseOpenRequest(
                        "OUTRO", [observationId], ato,
                        "A mutação deve ser revertida quando o ledger não puder ser persistido.",
                        new IdentityDecisionEvidence("CONFIRMACAO_INSTITUCIONAL_SEM_DOCUMENTO")),
                    Guid.NewGuid(),
                    CancellationToken.None));
            Assert.That(ex!.Number, Is.EqualTo(51990));

            await using var verify = connection.CreateCommand();
            verify.CommandText = """
                SELECT
                  (SELECT COUNT(*) FROM identidade.caso_conflito_identidade WHERE ato_referencia=@ato),
                  (SELECT COUNT(*) FROM auditoria.decisao_identidade_evento WHERE ato_referencia=@ato),
                  (SELECT COUNT(*) FROM identidade.v_vinculo_corrente
                   WHERE pessoa_observacao_id=@obs AND pessoa_uuid=@uuid AND status='RESOLVIDO');
                """;
            verify.Parameters.AddWithValue("@ato", ato);
            verify.Parameters.AddWithValue("@obs", observationId);
            verify.Parameters.AddWithValue("@uuid", originalUuid);
            await using var reader = await verify.ExecuteReaderAsync();
            Assert.That(await reader.ReadAsync(), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(reader.GetInt32(0), Is.EqualTo(0), "O caso não pode sobreviver sem seu evento canônico.");
                Assert.That(reader.GetInt32(1), Is.EqualTo(0), "O evento que falhou não pode ficar parcialmente gravado.");
                Assert.That(reader.GetInt32(2), Is.EqualTo(1), "O vínculo corrente original deve ser restaurado pelo rollback.");
            });
        }
        finally
        {
            await using var drop = connection.CreateCommand();
            drop.CommandText = $"DROP TRIGGER IF EXISTS {trigger};";
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static async Task PrepareAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteCanonicalSchemaAsync(connection, databaseDir);
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Seed_Dev.sql"));
    }

    private static async Task<(Guid CredentialId, long ObservationId, Guid OriginalUuid)> LoadFixtureAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        Guid credentialId;
        await using (var credential = connection.CreateCommand())
        {
            credential.CommandText = """
                SELECT TOP(1) c.credencial_id
                FROM controle.credencial_api c
                JOIN ref.gestor g ON g.gestor_id=c.gestor_id
                WHERE c.tipo_credencial='GESTOR' AND c.codigo_publico='SMADS'
                  AND c.ativo=1 AND g.codigo='SMADS' AND g.ativo=1;
                """;
            credentialId = (Guid)(await credential.ExecuteScalarAsync()
                ?? throw new InvalidOperationException("Credencial DEV SMADS não encontrada."));
        }

        await using var observation = connection.CreateCommand();
        observation.CommandText = """
            SELECT TOP(1) vc.pessoa_observacao_id,vc.pessoa_uuid
            FROM identidade.v_vinculo_corrente vc
            WHERE vc.status='RESOLVIDO' AND vc.pessoa_uuid IS NOT NULL
            ORDER BY vc.pessoa_observacao_id;
            """;
        await using var reader = await observation.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new InvalidOperationException("Fixture sem observação resolvida.");
        return (credentialId, reader.GetInt64(0), reader.GetGuid(1));
    }

    private static string RequireIntegrationConnection() =>
        Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION")
        ?? throw new InvalidOperationException("JORNADA_TEST_SQL_CONNECTION não configurada.");
}
