using Jornada.Operational.Sql;
using Jornada.Contracts;
using Jornada.Processor.Worker;
using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Integration;

[TestFixture]
[Category("Integration")]
public sealed class ProcessorRepositoryTests
{
    [Test]
    public async Task Reservation_and_recovery_use_atomic_processor_state_transitions()
    {
        var connectionString = RequireIntegrationConnection();
        await PrepareDatabaseAsync(connectionString);

        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = """
                DECLARE @lote UNIQUEIDENTIFIER=(SELECT TOP(1) lote_id FROM ingestao.lote ORDER BY criado_em,lote_id);
                UPDATE ingestao.lote SET status='PENDENTE',erro_codigo=NULL,atualizado_em=SYSDATETIMEOFFSET() WHERE lote_id=@lote;
                UPDATE e SET status='RECEBIDA',ultima_atualizacao=SYSDATETIMEOFFSET()
                FROM ingestao.entrega e JOIN ingestao.lote l ON l.entrega_id=e.entrega_id WHERE l.lote_id=@lote;
                """;
            await command.ExecuteNonQueryAsync();
        }

        var repository = CreateRepository(connectionString);
        var reserved = await repository.ReserveNextAsync(CancellationToken.None);
        Assert.That(reserved, Is.Not.Null);

        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT status FROM ingestao.lote WHERE lote_id=@id;";
            command.Parameters.AddWithValue("@id", reserved!.LoteId);
            Assert.That(Convert.ToString(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo("VALIDANDO"));

            command.Parameters.Clear();
            command.CommandText = "UPDATE ingestao.lote SET lease_expira_em=DATEADD(MINUTE,-1,SYSUTCDATETIME()) WHERE lote_id=@id;";
            command.Parameters.AddWithValue("@id", reserved.LoteId);
            await command.ExecuteNonQueryAsync();
        }

        var recovered = await repository.RecoverExpiredLeasesAsync(5, CancellationToken.None);
        Assert.That(recovered, Is.EqualTo(1));

        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT status,recuperacao_count,lease_id FROM ingestao.lote WHERE lote_id=@id;";
            command.Parameters.AddWithValue("@id", reserved!.LoteId);
            using var reader = await command.ExecuteReaderAsync();
            Assert.That(await reader.ReadAsync(), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(reader.GetString(0), Is.EqualTo("PENDENTE"));
                Assert.That(reader.GetInt32(1), Is.EqualTo(1));
                Assert.That(reader.IsDBNull(2), Is.True);
            });
        }
    }

    [Test]
    public async Task Rejected_batch_marks_lote_and_entrega_without_publishing_completeness()
    {
        var connectionString = RequireIntegrationConnection();
        await PrepareDatabaseAsync(connectionString);
        var repository = CreateRepository(connectionString);

        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = """
                DECLARE @lote UNIQUEIDENTIFIER=(SELECT TOP(1) lote_id FROM ingestao.lote ORDER BY criado_em,lote_id);
                UPDATE ingestao.lote SET status='PENDENTE',erro_codigo=NULL,atualizado_em=SYSDATETIMEOFFSET() WHERE lote_id=@lote;
                UPDATE e SET status='RECEBIDA',ultima_atualizacao=SYSDATETIMEOFFSET()
                FROM ingestao.entrega e JOIN ingestao.lote l ON l.entrega_id=e.entrega_id WHERE l.lote_id=@lote;
                """;
            await command.ExecuteNonQueryAsync();
        }

        var reserved = await repository.ReserveNextAsync(CancellationToken.None);
        Assert.That(reserved, Is.Not.Null);
        await repository.MarkRejectedAsync(reserved!, "TESTE_REJEICAO", CancellationToken.None);

        await using var verify = new SqlConnection(connectionString);
        await verify.OpenAsync();
        using var query = verify.CreateCommand();
        query.CommandText = """
            SELECT l.status,l.erro_codigo,e.status,COALESCE(c.entrega_completa,0)
            FROM ingestao.lote l JOIN ingestao.entrega e ON e.entrega_id=l.entrega_id
            LEFT JOIN ingestao.v_entrega_completude c ON c.entrega_id=e.entrega_id
            WHERE l.lote_id=@id;
            """;
        query.Parameters.AddWithValue("@id", reserved!.LoteId);
        using var reader = await query.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetString(0), Is.EqualTo("REJEITADO"));
            Assert.That(reader.GetString(1), Is.EqualTo("TESTE_REJEICAO"));
            Assert.That(reader.GetString(2), Is.EqualTo("REJEITADA"));
            Assert.That(reader.GetInt32(3), Is.Zero);
        });
    }

    [Test]
    public async Task Successful_batch_publishes_person_fact_qc_and_completeness_atomically()
    {
        var connectionString = RequireIntegrationConnection();
        await PrepareDatabaseAsync(connectionString);
        var repository = CreateRepository(connectionString);

        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = """
                DECLARE @lote UNIQUEIDENTIFIER=(SELECT TOP(1) l.lote_id
                  FROM ingestao.lote l JOIN ingestao.entrega e ON e.entrega_id=l.entrega_id
                 WHERE e.natureza='BENEFICIO'
                 ORDER BY l.criado_em,l.lote_id);
                UPDATE ingestao.lote SET status='PENDENTE',erro_codigo=NULL,atualizado_em=SYSDATETIMEOFFSET() WHERE lote_id=@lote;
                UPDATE e SET status='RECEBIDA',ultima_atualizacao=SYSDATETIMEOFFSET()
                FROM ingestao.entrega e JOIN ingestao.lote l ON l.entrega_id=e.entrega_id WHERE l.lote_id=@lote;
                """;
            await command.ExecuteNonQueryAsync();
        }

        var batch = await repository.ReserveNextAsync(CancellationToken.None);
        Assert.That(batch, Is.Not.Null);
        var reservedBatch = batch!;

        var person = new ParsedPerson(
            "PROC-V325", "PROC-V325", new string('a',64), "TX-PROC-V325", "98765432100", null, "Pessoa Teste Processor", new DateOnly(1990,1,1), "Mae Teste", [], []);
        var fact = reservedBatch.Natureza == IntegrationNature.BENEFICIO
            ? new ParsedFact("PROC-V325", "REG-PROC-V325", RegistroOperacao.INCLUSAO, new string('b',64), DateOnly.FromDateTime(DateTime.UtcNow.Date), null, null, null, null, null, "VIGENTE", null, null, 25m, null, null)
            : new ParsedFact("PROC-V325", "REG-PROC-V325", RegistroOperacao.INCLUSAO, new string('b',64), null, null, null, DateTimeOffset.UtcNow, "UNIDADE TESTE", "REALIZADO", null, null, null, null, null, null);
        var manifest = new IngestionPackageManifest(2, reservedBatch.PessoaSchemaVersao,
            reservedBatch.CodigoSistemaOrigem, reservedBatch.Natureza, reservedBatch.CodigoTipo, reservedBatch.TipoVersao, reservedBatch.DataReferencia);

        await repository.PersistValidatedAsync(reservedBatch, new ParsedPackage(manifest, [person], [fact]), CancellationToken.None);

        await using var verify = new SqlConnection(connectionString);
        await verify.OpenAsync();
        using var query = verify.CreateCommand();
        query.CommandText = """
            SELECT l.status,e.status,COALESCE(c.entrega_completa,0),
                   (SELECT COUNT(*) FROM silver.pessoa_observacao
                     WHERE lote_id=l.lote_id AND codigo_pessoa_origem='PROC-V325'),
                   (SELECT COUNT(*) FROM silver.registro_observacao
                     WHERE lote_id=l.lote_id AND codigo_registro_origem='REG-PROC-V325'),
                   (SELECT COUNT(*) FROM serving.registro_integrado
                     WHERE entrega_id=e.entrega_id AND codigo_registro_origem='REG-PROC-V325' AND entrega_completa=1),
                   (SELECT COUNT(*) FROM ingestao.item_processado
                     WHERE lote_id=l.lote_id AND codigo_origem IN('PROC-V325','REG-PROC-V325'))
            FROM ingestao.lote l JOIN ingestao.entrega e ON e.entrega_id=l.entrega_id
            LEFT JOIN ingestao.v_entrega_completude c ON c.entrega_id=e.entrega_id
            WHERE l.lote_id=@id;
            """;
        query.Parameters.AddWithValue("@id", reservedBatch.LoteId);
        using var reader = await query.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetString(0), Is.EqualTo("PROCESSADO"));
            Assert.That(reader.GetString(1), Is.EqualTo("PROCESSADA"));
            Assert.That(reader.GetInt32(2), Is.EqualTo(1));
            Assert.That(reader.GetInt32(3), Is.EqualTo(1));
            Assert.That(reader.GetInt32(4), Is.EqualTo(1));
            Assert.That(reader.GetInt32(5), Is.GreaterThanOrEqualTo(1));
            Assert.That(reader.GetInt32(6), Is.EqualTo(2), "Pessoa e Registro devem produzir resultado individual de processamento.");
        });
    }

    [Test]
    public async Task Shared_cpf_conflict_marks_identifier_without_breaking_canonical_assignment()
    {
        var connectionString = RequireIntegrationConnection();
        await PrepareDatabaseAsync(connectionString);
        var repository = CreateRepository(connectionString);

        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = """
                DECLARE @lote UNIQUEIDENTIFIER=(SELECT TOP(1) l.lote_id
                  FROM ingestao.lote l JOIN ingestao.entrega e ON e.entrega_id=l.entrega_id
                 WHERE e.natureza='BENEFICIO'
                 ORDER BY l.criado_em,l.lote_id);
                UPDATE ingestao.lote SET status='PENDENTE',erro_codigo=NULL,atualizado_em=SYSDATETIMEOFFSET() WHERE lote_id=@lote;
                UPDATE e SET status='RECEBIDA',ultima_atualizacao=SYSDATETIMEOFFSET()
                FROM ingestao.entrega e JOIN ingestao.lote l ON l.entrega_id=e.entrega_id WHERE l.lote_id=@lote;
                """;
            await command.ExecuteNonQueryAsync();
        }

        var batch = await repository.ReserveNextAsync(CancellationToken.None);
        Assert.That(batch, Is.Not.Null);

        // O seed associa 52998224725 a João de Souza, nascido em 1977-09-22.
        // A nova observação é incompatível, mas não há base para escolhê-la como a observação errada.
        var person = new ParsedPerson(
            "CPF-COMPARTILHADO-FILHO", "CPF-COMPARTILHADO-FILHO", new string('e',64), "TX-CPF-COMPARTILHADO", "52998224725", null,
            "Pedro Henrique Santos", new DateOnly(2017,8,21), "Joana Santos", [], []);
        var fact = new ParsedFact(
            "CPF-COMPARTILHADO-FILHO", "REG-CPF-COMPARTILHADO", RegistroOperacao.INCLUSAO, new string('f',64),
            DateOnly.FromDateTime(DateTime.UtcNow.Date), null, null, null, null, null, "VIGENTE", null, null, 25m, null, null);
        var manifest = new IngestionPackageManifest(2, batch!.PessoaSchemaVersao,
            batch.CodigoSistemaOrigem, batch.Natureza, batch.CodigoTipo, batch.TipoVersao, batch.DataReferencia);

        await repository.PersistValidatedAsync(batch, new ParsedPackage(manifest, [person], [fact]), CancellationToken.None);

        await using var verify = new SqlConnection(connectionString);
        await verify.OpenAsync();
        using var query = verify.CreateCommand();
        query.CommandText = """
            SELECT
              vf.status,vf.motivo,vf.pessoa_uuid,
              (SELECT COUNT(*) FROM silver.registro_observacao ro WHERE ro.codigo_registro_origem='REG-CPF-COMPARTILHADO'),
              (SELECT COUNT(*) FROM gold.beneficio_concedido b WHERE b.codigo_registro_origem='REG-CPF-COMPARTILHADO'),
              (SELECT TOP(1) estado_atribuicao_identidade FROM gold.beneficio_concedido b WHERE b.codigo_registro_origem='REG-CPF-COMPARTILHADO'),
              (SELECT TOP(1) cpf_declarado FROM gold.beneficio_concedido b WHERE b.codigo_registro_origem='REG-CPF-COMPARTILHADO'),
              (SELECT TOP(1) pessoa_uuid FROM gold.beneficio_concedido b WHERE b.codigo_registro_origem='REG-CPF-COMPARTILHADO'),
              (SELECT COUNT(*) FROM serving.v_bi_pendencias_identidade p WHERE p.pessoa_observacao_id=po.pessoa_observacao_id),
              (SELECT TOP(1) estado FROM identidade.identity_map WHERE tipo='CPF' AND identificador='52998224725' AND vigencia_fim IS NULL),
              (SELECT TOP(1) estado_motivo FROM identidade.identity_map WHERE tipo='CPF' AND identificador='52998224725' AND vigencia_fim IS NULL),
              (SELECT pessoa_uuid FROM identidade.cpf_ancora WHERE cpf='52998224725'),
              (SELECT TOP(1) cpf_classificacao FROM serving.v_bi_qualidade_identidade_origem q WHERE q.pessoa_observacao_id=po.pessoa_observacao_id),
              (SELECT TOP(1) cpf_problema FROM serving.v_bi_qualidade_identidade_origem q WHERE q.pessoa_observacao_id=po.pessoa_observacao_id)
            FROM silver.pessoa_observacao po
            JOIN identidade.v_vinculo_corrente vf ON vf.pessoa_observacao_id=po.pessoa_observacao_id
            WHERE po.codigo_pessoa_origem='CPF-COMPARTILHADO-FILHO'
            ORDER BY po.pessoa_observacao_id DESC;
            """;
        using var reader = await query.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetString(0), Is.EqualTo("RESOLVIDO"));
            Assert.That(reader.IsDBNull(1), Is.True, "O motivo pertence ao CPF, não à observação que revelou a divergência.");
            Assert.That(reader.IsDBNull(2), Is.False, "CPF válido continua atribuindo a observação ao UUID ancorado.");
            Assert.That(reader.GetInt32(3), Is.EqualTo(1));
            Assert.That(reader.GetInt32(4), Is.EqualTo(1));
            Assert.That(reader.GetString(5), Is.EqualTo("ATRIBUIDA"));
            Assert.That(reader.GetString(6), Is.EqualTo("52998224725"));
            Assert.That(reader.IsDBNull(7), Is.False, "O fato continua atribuído pelo CPF determinístico.");
            Assert.That(reader.GetInt32(8), Is.Zero, "A observação não vira pendência individual por revelar um conflito global do CPF.");
            Assert.That(reader.GetString(9), Is.EqualTo("EM_CONFLITO"));
            Assert.That(reader.GetString(10), Is.EqualTo("CPF_COMPARTILHADO_SUSPEITO"));
            Assert.That(reader.GetGuid(2), Is.EqualTo(reader.GetGuid(7)));
            Assert.That(reader.GetGuid(2), Is.EqualTo(reader.GetGuid(11)), "Observação, fato e âncora devem preservar o mesmo UUID.");
            Assert.That(reader.GetString(12), Is.EqualTo("CPF_CONFLITO_DETERMINISTICO"),
                "QC/BI deve tornar o conflito global do CPF explicitamente visível.");
            Assert.That(reader.GetInt32(13), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Codigo_pessoa_origem_may_equal_cpf_format_without_being_interpreted_as_cpf()
    {
        var connectionString = RequireIntegrationConnection();
        await PrepareDatabaseAsync(connectionString);
        var repository = CreateRepository(connectionString);
        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = """
                DECLARE @lote UNIQUEIDENTIFIER=(SELECT TOP(1) l.lote_id FROM ingestao.lote l JOIN ingestao.entrega e ON e.entrega_id=l.entrega_id WHERE e.natureza='BENEFICIO' ORDER BY l.criado_em,l.lote_id);
                UPDATE ingestao.lote SET status='PENDENTE',erro_codigo=NULL,atualizado_em=SYSDATETIMEOFFSET() WHERE lote_id=@lote;
                UPDATE e SET status='RECEBIDA',ultima_atualizacao=SYSDATETIMEOFFSET() FROM ingestao.entrega e JOIN ingestao.lote l ON l.entrega_id=e.entrega_id WHERE l.lote_id=@lote;
                """;
            await command.ExecuteNonQueryAsync();
        }
        var batch = await repository.ReserveNextAsync(CancellationToken.None);
        Assert.That(batch, Is.Not.Null);
        var person = new ParsedPerson(
            "16899535009", "16899535009", new string('a',64), "TX-OPAQUE-CODE", null, "SEM_CPF",
            "Pessoa Sem CPF", new DateOnly(1991,4,13), "Mae Sem CPF", [], []);
        var fact = new ParsedFact(
            "16899535009", "REG-OPAQUE-CODE", RegistroOperacao.INCLUSAO, new string('b',64),
            DateOnly.FromDateTime(DateTime.UtcNow.Date), null, null, null, null, null, "VIGENTE", null, null, 10m, null, null);
        var manifest = new IngestionPackageManifest(2, batch!.PessoaSchemaVersao,
            batch.CodigoSistemaOrigem, batch.Natureza, batch.CodigoTipo, batch.TipoVersao, batch.DataReferencia);
        await repository.PersistValidatedAsync(batch, new ParsedPackage(manifest, [person], [fact]), CancellationToken.None);

        await using var verify = new SqlConnection(connectionString);
        await verify.OpenAsync();
        using var query = verify.CreateCommand();
        query.CommandText = """
            SELECT b.codigo_pessoa_origem,b.cpf_declarado,b.pessoa_uuid,b.estado_atribuicao_identidade,
                   (SELECT COUNT(*) FROM identidade.identity_map WHERE tipo='CPF' AND identificador='16899535009' AND vigencia_fim IS NULL)
            FROM gold.beneficio_concedido b WHERE b.codigo_registro_origem='REG-OPAQUE-CODE';
            """;
        using var reader = await query.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetString(0), Is.EqualTo("16899535009"));
            Assert.That(reader.IsDBNull(1), Is.True, "codigo_pessoa_origem não adquire semântica de CPF pelo formato.");
            Assert.That(reader.IsDBNull(2), Is.True);
            Assert.That(reader.GetString(3), Is.EqualTo("PENDENTE_IDENTIDADE"));
            Assert.That(reader.GetInt32(4), Is.Zero, "A Jornada nunca deve inferir CPF a partir do código opaco de origem.");
        });
    }

    [Test]
    public async Task Existing_cpf_map_without_comparable_core_marks_identifier_but_keeps_uuid()
    {
        var connectionString = RequireIntegrationConnection();
        await PrepareDatabaseAsync(connectionString);
        var repository = CreateRepository(connectionString);

        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = """
                DECLARE @u UNIQUEIDENTIFIER=NEWID();
                INSERT identidade.pessoa(pessoa_uuid,status) VALUES(@u,'ATIVO');
                INSERT identidade.identity_map(
                    pessoa_uuid,tipo,identificador,vigencia_inicio,metodo_resolucao)
                VALUES(@u,'CPF','16899535009',SYSDATETIMEOFFSET(),'CPF_DETERMINISTICO');

                DECLARE @lote UNIQUEIDENTIFIER=(SELECT TOP(1) l.lote_id
                  FROM ingestao.lote l JOIN ingestao.entrega e ON e.entrega_id=l.entrega_id
                 WHERE e.natureza='BENEFICIO'
                 ORDER BY l.criado_em,l.lote_id);
                UPDATE ingestao.lote SET status='PENDENTE',erro_codigo=NULL,atualizado_em=SYSDATETIMEOFFSET() WHERE lote_id=@lote;
                UPDATE e SET status='RECEBIDA',ultima_atualizacao=SYSDATETIMEOFFSET()
                FROM ingestao.entrega e JOIN ingestao.lote l ON l.entrega_id=e.entrega_id WHERE l.lote_id=@lote;
                """;
            await command.ExecuteNonQueryAsync();
        }

        var batch = await repository.ReserveNextAsync(CancellationToken.None);
        Assert.That(batch, Is.Not.Null);

        var person = new ParsedPerson(
            "CPF-SEM-NUCLEO", "CPF-SEM-NUCLEO", new string('a',64), "TX-CPF-SEM-NUCLEO", "16899535009", null,
            "Pessoa de Teste", new DateOnly(1991,5,17), "Mae de Teste", [], []);
        var manifest = new IngestionPackageManifest(2, batch!.PessoaSchemaVersao,
            batch.CodigoSistemaOrigem, batch.Natureza, batch.CodigoTipo, batch.TipoVersao, batch.DataReferencia);

        await repository.PersistValidatedAsync(batch, new ParsedPackage(manifest, [person], []), CancellationToken.None);

        await using var verify = new SqlConnection(connectionString);
        await verify.OpenAsync();
        using var query = verify.CreateCommand();
        query.CommandText = """
            SELECT vf.status,vf.motivo,vf.pessoa_uuid,
                   im.estado,im.estado_motivo,a.pessoa_uuid
            FROM silver.pessoa_observacao po
            JOIN identidade.v_vinculo_corrente vf ON vf.pessoa_observacao_id=po.pessoa_observacao_id
            JOIN identidade.identity_map im ON im.tipo='CPF' AND im.identificador='16899535009' AND im.vigencia_fim IS NULL
            JOIN identidade.cpf_ancora a ON a.cpf='16899535009'
            WHERE po.codigo_pessoa_origem='CPF-SEM-NUCLEO'
            ORDER BY po.pessoa_observacao_id DESC;
            """;
        using var reader = await query.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetString(0), Is.EqualTo("RESOLVIDO"));
            Assert.That(reader.IsDBNull(1), Is.True);
            Assert.That(reader.IsDBNull(2), Is.False);
            Assert.That(reader.GetString(3), Is.EqualTo("EM_CONFLITO"));
            Assert.That(reader.GetString(4), Is.EqualTo("CPF_NUCLEO_EXISTENTE_INDISPONIVEL"));
            Assert.That(reader.GetGuid(2), Is.EqualTo(reader.GetGuid(5)));
        });
    }

    [Test]
    public async Task Concurrent_workers_reserve_distinct_lotes()
    {
        var connectionString = RequireIntegrationConnection();
        await PrepareDatabaseAsync(connectionString);

        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = """
                DECLARE @selecionados TABLE(lote_id UNIQUEIDENTIFIER PRIMARY KEY,entrega_id UNIQUEIDENTIFIER);
                INSERT @selecionados(lote_id,entrega_id)
                SELECT TOP(2) lote_id,entrega_id FROM ingestao.lote ORDER BY criado_em,lote_id;
                IF (SELECT COUNT(*) FROM @selecionados) < 2 THROW 51000,'Seed precisa conter ao menos dois lotes.',1;
                UPDATE l SET status='PENDENTE',erro_codigo=NULL,atualizado_em=SYSDATETIMEOFFSET()
                  FROM ingestao.lote l JOIN @selecionados s ON s.lote_id=l.lote_id;
                UPDATE e SET status='RECEBIDA',ultima_atualizacao=SYSDATETIMEOFFSET()
                  FROM ingestao.entrega e WHERE EXISTS(SELECT 1 FROM @selecionados s WHERE s.entrega_id=e.entrega_id);
                """;
            await command.ExecuteNonQueryAsync();
        }

        var firstRepository = CreateRepository(connectionString);
        var secondRepository = CreateRepository(connectionString);
        var reservations = await Task.WhenAll(
            firstRepository.ReserveNextAsync(CancellationToken.None),
            secondRepository.ReserveNextAsync(CancellationToken.None));

        var first = reservations[0];
        var second = reservations[1];
        Assert.That(first is not null || second is not null, Is.True,
            "Ao menos um worker deve reservar trabalho durante a disputa concorrente.");

        if (first is null)
            first = await firstRepository.ReserveNextAsync(CancellationToken.None);
        if (second is null)
            second = await secondRepository.ReserveNextAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.Null, "Worker que perdeu a primeira disputa deve progredir no poll seguinte.");
            Assert.That(second, Is.Not.Null, "Worker que perdeu a primeira disputa deve progredir no poll seguinte.");
            Assert.That(first!.LoteId, Is.Not.EqualTo(second!.LoteId),
                "UPDLOCK/READPAST deve impedir dois workers de reservar o mesmo lote.");
        });
    }

    [Test]
    public async Task Transaction_failure_after_partial_persistence_rolls_back_silver_identity_and_gold()
    {
        var connectionString = RequireIntegrationConnection();
        await PrepareDatabaseAsync(connectionString);
        var repository = CreateRepository(connectionString);

        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = """
                DECLARE @lote UNIQUEIDENTIFIER=(SELECT TOP(1) l.lote_id
                  FROM ingestao.lote l JOIN ingestao.entrega e ON e.entrega_id=l.entrega_id
                 WHERE e.natureza IS NULL AND e.tipo_registro_id IS NULL ORDER BY l.criado_em,l.lote_id);
                IF @lote IS NULL THROW 51000,'Seed precisa conter Entrega cadastral.',1;
                UPDATE ingestao.lote SET status='PENDENTE',erro_codigo=NULL,atualizado_em=SYSDATETIMEOFFSET() WHERE lote_id=@lote;
                UPDATE e SET status='RECEBIDA',ultima_atualizacao=SYSDATETIMEOFFSET()
                FROM ingestao.entrega e JOIN ingestao.lote l ON l.entrega_id=e.entrega_id WHERE l.lote_id=@lote;
                """;
            await command.ExecuteNonQueryAsync();
        }

        var batch = await repository.ReserveNextAsync(CancellationToken.None);
        Assert.That(batch, Is.Not.Null);
        var reservedBatch = batch!;

        var valid = new ParsedPerson(
            "PROC-V325-ROLLBACK-A", "PROC-V325-ROLLBACK-A", new string('c',64), "TX-ROLLBACK-A", "31415926590", null, "Pessoa Rollback A", new DateOnly(1991, 2, 3), "Mae Rollback A", [], []);
        var failsAfterSilver = new ParsedPerson(
            "PROC-V325-ROLLBACK-B", "PROC-V325-ROLLBACK-B", new string('d',64), "TX-ROLLBACK-B", "27182818205", null, "Pessoa Rollback B", new DateOnly(1992, 3, 4), "Mae Rollback B",
            [new ParsedTransversalAttribute("PROC-V320-EVID", "ENDERECO_RESIDENCIAL", "CEP=01001000|NUMERO=1",
                "COMPROVADO", "DOCUMENTO", null, null, DateTimeOffset.UtcNow, null, GeographicResolutionStatus.NAO_RESOLVIDA_ORIGEM, null)], []);
        var manifest = new IngestionPackageManifest(2, reservedBatch.PessoaSchemaVersao,
            reservedBatch.CodigoSistemaOrigem, null, null, null, reservedBatch.DataReferencia);

        Assert.ThrowsAsync<InvalidDataException>(async () =>
            await repository.PersistValidatedAsync(reservedBatch, new ParsedPackage(manifest, [valid, failsAfterSilver], []), CancellationToken.None));

        await using var verify = new SqlConnection(connectionString);
        await verify.OpenAsync();
        using var query = verify.CreateCommand();
        query.CommandText = """
            SELECT
              (SELECT COUNT(*) FROM silver.pessoa_observacao
                WHERE lote_id=@lote AND codigo_pessoa_origem IN('PROC-V325-ROLLBACK-A','PROC-V325-ROLLBACK-B')),
              (SELECT COUNT(*) FROM silver.pessoa_atributo_observacao pa
                JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=pa.pessoa_observacao_id
                WHERE po.lote_id=@lote AND po.codigo_pessoa_origem IN('PROC-V325-ROLLBACK-A','PROC-V325-ROLLBACK-B')),
              (SELECT COUNT(*) FROM identidade.identity_map WHERE identificador IN('31415926590','27182818205')),
              (SELECT COUNT(*) FROM gold.pessoa WHERE cpf IN('31415926590','27182818205')),
              (SELECT COUNT(*) FROM ingestao.item_processado
                WHERE lote_id=@lote AND codigo_origem IN('PROC-V325-ROLLBACK-A','PROC-V325-ROLLBACK-B')),
              (SELECT status FROM ingestao.lote WHERE lote_id=@lote);
            """;
        query.Parameters.AddWithValue("@lote", reservedBatch.LoteId);
        using var reader = await query.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetInt32(0), Is.Zero);
            Assert.That(reader.GetInt32(1), Is.Zero);
            Assert.That(reader.GetInt32(2), Is.Zero);
            Assert.That(reader.GetInt32(3), Is.Zero);
            Assert.That(reader.GetInt32(4), Is.Zero);
            Assert.That(reader.GetString(5), Is.EqualTo("VALIDANDO"), "A reserva externa à transação permanece recuperável pelo stale recovery.");
        });
    }

    [Test]
    public async Task Heartbeat_renews_only_the_current_lease()
    {
        var connectionString = RequireIntegrationConnection();
        await PrepareDatabaseAsync(connectionString);
        await SetOnePendingAsync(connectionString);
        var repository = CreateRepository(connectionString);
        var batch = await repository.ReserveNextAsync("worker-heartbeat", TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.That(batch, Is.Not.Null);

        DateTimeOffset before;
        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            using var query = connection.CreateCommand();
            query.CommandText = "SELECT lease_expira_em FROM ingestao.lote WHERE lote_id=@id;";
            query.Parameters.AddWithValue("@id", batch!.LoteId);
            before = (DateTimeOffset)(await query.ExecuteScalarAsync())!;
        }

        await Task.Delay(25);
        Assert.That(await repository.HeartbeatAsync(batch!, TimeSpan.FromMinutes(2), CancellationToken.None), Is.True);

        await using var verify = new SqlConnection(connectionString);
        await verify.OpenAsync();
        using var command = verify.CreateCommand();
        command.CommandText = "SELECT lease_expira_em,heartbeat_em FROM ingestao.lote WHERE lote_id=@id;";
        command.Parameters.AddWithValue("@id", batch!.LoteId);
        using var reader = await command.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetDateTimeOffset(0), Is.GreaterThan(before));
        Assert.That(reader.IsDBNull(1), Is.False);
    }

    [Test]
    public async Task Rejected_batch_rolls_back_lote_when_delivery_recalculation_fails()
    {
        var connectionString = RequireIntegrationConnection();
        await PrepareDatabaseAsync(connectionString);
        await SetOnePendingAsync(connectionString);
        var repository = CreateRepository(connectionString);

        var reserved = await repository.ReserveNextAsync("worker-atomic-reject", TimeSpan.FromMinutes(2), CancellationToken.None);
        Assert.That(reserved, Is.Not.Null);

        const string trigger = "ingestao.tr_test_atomic_processor_failure";
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        try
        {
            await using (var create = connection.CreateCommand())
            {
                create.CommandText = $"""
                    CREATE OR ALTER TRIGGER {trigger} ON ingestao.entrega AFTER UPDATE AS
                    BEGIN
                      SET NOCOUNT ON;
                      IF EXISTS(SELECT 1 FROM inserted WHERE entrega_id=CONVERT(uniqueidentifier,'{reserved!.EntregaId:D}'))
                        THROW 51985,'Falha injetada para provar atomicidade lote+Entrega.',1;
                    END;
                    """;
                await create.ExecuteNonQueryAsync();
            }

            var ex = Assert.ThrowsAsync<SqlException>(async () =>
                await repository.MarkRejectedAsync(reserved!, "TESTE_ATOMICIDADE", CancellationToken.None));
            Assert.That(ex!.Number, Is.EqualTo(51985));

            await using var verify = connection.CreateCommand();
            verify.CommandText = "SELECT status,erro_codigo,lease_id FROM ingestao.lote WHERE lote_id=@id;";
            verify.Parameters.AddWithValue("@id", reserved!.LoteId);
            await using var reader = await verify.ExecuteReaderAsync();
            Assert.That(await reader.ReadAsync(), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(reader.GetString(0), Is.EqualTo("VALIDANDO"));
                Assert.That(reader.IsDBNull(1), Is.True, "Erro do lote deve voltar ao valor anterior.");
                Assert.That(reader.GetGuid(2), Is.EqualTo(reserved.LeaseId), "Lease deve sobreviver ao rollback.");
            });
        }
        finally
        {
            await using var drop = connection.CreateCommand();
            drop.CommandText = $"IF OBJECT_ID(N'{trigger}',N'TR') IS NOT NULL DROP TRIGGER {trigger};";
            await drop.ExecuteNonQueryAsync();
        }
    }

    [Test]
    public async Task Retry_is_scheduled_then_lote_becomes_poison_at_max_attempts()
    {
        var connectionString = RequireIntegrationConnection();
        await PrepareDatabaseAsync(connectionString);
        await SetOnePendingAsync(connectionString);
        var repository = CreateRepository(connectionString);

        var first = await repository.ReserveNextAsync("worker-retry-1", TimeSpan.FromMinutes(2), CancellationToken.None);
        Assert.That(first, Is.Not.Null);
        var firstOutcome = await repository.ScheduleRetryOrPoisonAsync(first!, "TESTE_TRANSIENTE", 2, TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1), CancellationToken.None);
        Assert.That(firstOutcome, Is.EqualTo(ProcessingFailureOutcome.RetryScheduled));

        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            using var makeDue = connection.CreateCommand();
            makeDue.CommandText = "UPDATE ingestao.lote SET proxima_tentativa_em=DATEADD(SECOND,-1,SYSUTCDATETIME()) WHERE lote_id=@id;";
            makeDue.Parameters.AddWithValue("@id", first!.LoteId);
            await makeDue.ExecuteNonQueryAsync();
        }

        var second = await repository.ReserveNextAsync("worker-retry-2", TimeSpan.FromMinutes(2), CancellationToken.None);
        Assert.That(second, Is.Not.Null);
        Assert.That(second!.LoteId, Is.EqualTo(first!.LoteId));
        Assert.That(second.AttemptNumber, Is.EqualTo(2));
        var secondOutcome = await repository.ScheduleRetryOrPoisonAsync(second, "TESTE_TRANSIENTE", 2, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), CancellationToken.None);
        Assert.That(secondOutcome, Is.EqualTo(ProcessingFailureOutcome.Poison));

        await using var verify = new SqlConnection(connectionString);
        await verify.OpenAsync();
        using var query = verify.CreateCommand();
        query.CommandText = "SELECT status,tentativa_count,poison_em,lease_id FROM ingestao.lote WHERE lote_id=@id;";
        query.Parameters.AddWithValue("@id", second.LoteId);
        using var reader = await query.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetString(0), Is.EqualTo("POISON"));
            Assert.That(reader.GetInt32(1), Is.EqualTo(2));
            Assert.That(reader.IsDBNull(2), Is.False);
            Assert.That(reader.IsDBNull(3), Is.True);
        });
    }

    [Test]
    public async Task V4_originless_person_with_zero_identifiers_is_persisted_and_left_for_linkage()
    {
        var connectionString = RequireIntegrationConnection();
        await PrepareDatabaseAsync(connectionString);
        await SetOneCadastralBatchPendingAsync(connectionString);
        var repository = CreateRepository(connectionString);
        var batch = await repository.ReserveNextAsync(CancellationToken.None);
        Assert.That(batch, Is.Not.Null);

        var person = new ParsedPerson(
            "DELIVERY-V4-NO-ID", null, new string('7', 64), "TX-V4-NO-ID", null, "SEM_CPF",
            "Pessoa V4 Sem Identificador", new DateOnly(1993, 5, 17), "Mae V4 Sem Identificador",
            [], [], Array.Empty<ParsedPersonIdentifier>());
        var manifest = new IngestionPackageManifest(
            2, 4, batch!.CodigoSistemaOrigem, null, null, null, batch.DataReferencia);

        await repository.PersistValidatedAsync(
            batch, new ParsedPackage(manifest, [person], []), CancellationToken.None);

        await using var verify = new SqlConnection(connectionString);
        await verify.OpenAsync();
        await using var query = verify.CreateCommand();
        query.CommandText = """
            SELECT po.pessoa_origem_id,po.codigo_pessoa_origem,
                   (SELECT COUNT(*) FROM silver.pessoa_identificador_observacao i
                     WHERE i.pessoa_observacao_id=po.pessoa_observacao_id),
                   vc.status,vc.metodo_resolucao,
                   (SELECT COUNT(*) FROM ingestao.item_processado ip
                     WHERE ip.lote_id=po.lote_id AND ip.classe_item='PESSOA'
                       AND ip.codigo_origem='DELIVERY-V4-NO-ID'),
                   (SELECT TOP(1) cpf_classificacao FROM serving.v_bi_qualidade_identidade_origem q
                     WHERE q.pessoa_observacao_id=po.pessoa_observacao_id),
                   (SELECT TOP(1) cpf_problema FROM serving.v_bi_qualidade_identidade_origem q
                     WHERE q.pessoa_observacao_id=po.pessoa_observacao_id)
            FROM silver.pessoa_observacao po
            JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=po.pessoa_observacao_id
            WHERE po.lote_id=@lote AND po.id_pessoa_entrega='DELIVERY-V4-NO-ID';
            """;
        query.Parameters.AddWithValue("@lote", batch.LoteId);
        await using var reader = await query.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.IsDBNull(0), Is.True);
            Assert.That(reader.IsDBNull(1), Is.True);
            Assert.That(reader.GetInt32(2), Is.Zero);
            Assert.That(reader.GetString(3), Is.EqualTo("NAO_RESOLVIDO"));
            Assert.That(reader.GetString(4), Is.EqualTo("PENDENTE_PROBABILISTICO"));
            Assert.That(reader.GetInt32(5), Is.EqualTo(1));
            Assert.That(reader.GetString(6), Is.EqualTo("CPF_AUSENTE"),
                "Pessoa sem origem persistente também deve aparecer no QC/BI.");
            Assert.That(reader.GetInt32(7), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task V4_two_originless_people_in_same_delivery_keep_distinct_delivery_links()
    {
        var connectionString = RequireIntegrationConnection();
        await PrepareDatabaseAsync(connectionString);
        await SetOneCadastralBatchPendingAsync(connectionString);
        var repository = CreateRepository(connectionString);
        var batch = await repository.ReserveNextAsync(CancellationToken.None);
        Assert.That(batch, Is.Not.Null);

        var first = new ParsedPerson(
            "DELIVERY-ORIGINLESS-A", null, new string('1',64), "TX-ORIGINLESS-A", null, "SEM_CPF",
            "Pessoa Sem Origem A", new DateOnly(1990,1,2), "Mae A",
            [], [], Array.Empty<ParsedPersonIdentifier>());
        var second = new ParsedPerson(
            "DELIVERY-ORIGINLESS-B", null, new string('2',64), "TX-ORIGINLESS-B", null, "SEM_CPF",
            "Pessoa Sem Origem B", new DateOnly(1991,2,3), "Mae B",
            [], [], Array.Empty<ParsedPersonIdentifier>());
        var manifest = new IngestionPackageManifest(
            2, 4, batch!.CodigoSistemaOrigem, null, null, null, batch.DataReferencia);

        await repository.PersistValidatedAsync(
            batch, new ParsedPackage(manifest, [first, second], []), CancellationToken.None);

        await using var verify = new SqlConnection(connectionString);
        await verify.OpenAsync();
        await using var query = verify.CreateCommand();
        query.CommandText = """
            SELECT COUNT(*),COUNT(DISTINCT id_pessoa_entrega),
                   SUM(CASE WHEN pessoa_origem_id IS NULL AND codigo_pessoa_origem IS NULL THEN 1 ELSE 0 END),
                   (SELECT COUNT(*) FROM ingestao.item_processado ip
                     WHERE ip.lote_id=@lote AND ip.classe_item='PESSOA'
                       AND ip.codigo_origem IN('DELIVERY-ORIGINLESS-A','DELIVERY-ORIGINLESS-B'))
            FROM silver.pessoa_observacao
            WHERE lote_id=@lote
              AND id_pessoa_entrega IN('DELIVERY-ORIGINLESS-A','DELIVERY-ORIGINLESS-B');
            """;
        query.Parameters.AddWithValue("@lote", batch.LoteId);
        await using var reader = await query.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetInt32(0), Is.EqualTo(2));
            Assert.That(reader.GetInt32(1), Is.EqualTo(2));
            Assert.That(reader.GetInt32(2), Is.EqualTo(2));
            Assert.That(reader.GetInt32(3), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task V4_fact_links_to_originless_person_by_delivery_id()
    {
        var connectionString = RequireIntegrationConnection();
        await PrepareDatabaseAsync(connectionString);
        await using (var setup = new SqlConnection(connectionString))
        {
            await setup.OpenAsync();
            await using var command = setup.CreateCommand();
            command.CommandText = """
                DECLARE @lote UNIQUEIDENTIFIER=(
                    SELECT TOP(1) l.lote_id
                    FROM ingestao.lote l
                    JOIN ingestao.entrega e ON e.entrega_id=l.entrega_id
                    WHERE e.natureza='BENEFICIO'
                    ORDER BY l.criado_em,l.lote_id);
                UPDATE ingestao.lote SET status='PENDENTE',erro_codigo=NULL,lease_id=NULL,lease_owner=NULL,
                    lease_adquirido_em=NULL,heartbeat_em=NULL,lease_expira_em=NULL,proxima_tentativa_em=NULL,
                    poison_em=NULL,atualizado_em=SYSUTCDATETIME()
                WHERE lote_id=@lote;
                UPDATE e SET status='RECEBIDA',ultima_atualizacao=SYSDATETIMEOFFSET()
                FROM ingestao.entrega e JOIN ingestao.lote l ON l.entrega_id=e.entrega_id
                WHERE l.lote_id=@lote;
                """;
            await command.ExecuteNonQueryAsync();
        }

        var repository = CreateRepository(connectionString);
        var batch = await repository.ReserveNextAsync(CancellationToken.None);
        Assert.That(batch, Is.Not.Null);
        Assert.That(batch!.Natureza, Is.EqualTo(IntegrationNature.BENEFICIO));

        const string deliveryPersonId = "DELIVERY-V4-FACT-NO-SOURCE";
        var person = new ParsedPerson(
            deliveryPersonId, null, new string('6',64), "TX-V4-FACT-NO-SOURCE", null, "SEM_CPF",
            "Pessoa V4 Fato Sem Codigo Local", new DateOnly(1994,6,18), "Mae V4 Fato Sem Codigo Local",
            [], [], Array.Empty<ParsedPersonIdentifier>());
        var fact = new ParsedFact(
            deliveryPersonId, "REG-V4-FACT-NO-SOURCE", RegistroOperacao.INCLUSAO, new string('5',64),
            DateOnly.FromDateTime(DateTime.UtcNow.Date), null, null, null, null, null,
            "VIGENTE", null, null, 15m, null, null);
        var manifest = new IngestionPackageManifest(
            2, 4, batch.CodigoSistemaOrigem, batch.Natureza, batch.CodigoTipo, batch.TipoVersao, batch.DataReferencia);

        await repository.PersistValidatedAsync(
            batch, new ParsedPackage(manifest, [person], [fact]), CancellationToken.None);

        await using var verify = new SqlConnection(connectionString);
        await verify.OpenAsync();
        await using var query = verify.CreateCommand();
        query.CommandText = """
            SELECT po.pessoa_origem_id,po.codigo_pessoa_origem,
                   ro.pessoa_observacao_id,
                   b.pessoa_origem_id,b.codigo_pessoa_origem,b.estado_atribuicao_identidade,
                   ri.pessoa_origem_id,ri.codigo_pessoa_origem,ri.estado_atribuicao_identidade
            FROM silver.pessoa_observacao po
            JOIN silver.registro_observacao ro ON ro.pessoa_observacao_id=po.pessoa_observacao_id
            JOIN gold.beneficio_concedido b ON b.registro_observacao_id=ro.registro_observacao_id
            JOIN serving.registro_integrado ri ON ri.registro_observacao_id=ro.registro_observacao_id
            WHERE po.lote_id=@lote AND ro.codigo_registro_origem='REG-V4-FACT-NO-SOURCE';
            """;
        query.Parameters.AddWithValue("@lote", batch.LoteId);
        await using var reader = await query.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.IsDBNull(0), Is.True, "Pessoa sem código local não cria pessoa_origem artificial.");
            Assert.That(reader.IsDBNull(1), Is.True);
            Assert.That(reader.GetInt64(2), Is.GreaterThan(0), "O vínculo obrigatório do fato é pessoa_observacao_id.");
            Assert.That(reader.IsDBNull(3), Is.True);
            Assert.That(reader.IsDBNull(4), Is.True);
            Assert.That(reader.GetString(5), Is.EqualTo("PENDENTE_IDENTIDADE"));
            Assert.That(reader.IsDBNull(6), Is.True);
            Assert.That(reader.IsDBNull(7), Is.True);
            Assert.That(reader.GetString(8), Is.EqualTo("PENDENTE_IDENTIDADE"));
        });
    }

    [Test]
    public async Task V4_runtime_resolves_authorized_shared_base_and_persists_multiple_identifiers()
    {
        var connectionString = RequireIntegrationConnection();
        await PrepareDatabaseAsync(connectionString);
        await SetOneCadastralBatchPendingAsync(connectionString);
        var repository = CreateRepository(connectionString);
        var batch = await repository.ReserveNextAsync(CancellationToken.None);
        Assert.That(batch, Is.Not.Null);

        var suffix = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var baseCode = $"TEST_SHARED_{suffix}";
        var sourceCode = $"P-{suffix}";
        await using (var setup = new SqlConnection(connectionString))
        {
            await setup.OpenAsync();
            await using var command = setup.CreateCommand();
            command.CommandText = """
                INSERT ref.base_pessoa_origem(codigo,nome,gestor_custodiante_id,escopo,confianca_identidade)
                VALUES(@base,@base,@gestor,'COMPARTILHADA','HOMOLOGADA_DETERMINISTICA');
                DECLARE @base_id BIGINT=SCOPE_IDENTITY();
                INSERT ref.sistema_origem_base_pessoa(sistema_origem_id,base_pessoa_origem_id,padrao,ativo)
                VALUES(@sistema,@base_id,0,1);
                """;
            command.Parameters.AddWithValue("@base", baseCode);
            command.Parameters.AddWithValue("@gestor", batch!.GestorId);
            command.Parameters.AddWithValue("@sistema", batch.SistemaOrigemId);
            await command.ExecuteNonQueryAsync();
        }

        var identifiers = new ParsedPersonIdentifier[]
        {
            new("CODIGO_BASE_ORIGEM", baseCode, sourceCode, sourceCode, null, null, "DECLARADO", null, null, false),
            new("CNS", "BR", "898001160018261", "898001160018261", null, null, "DECLARADO", null, null, false),
            new("RG", "SSP_SP", "12.345.678-9", "123456789", "SSP", "SP", "DECLARADO", null, null, false)
        };
        var person = new ParsedPerson(
            sourceCode, sourceCode, new string('8', 64), $"TX-{suffix}", null, "SEM_CPF",
            "Pessoa V4 Base Compartilhada", new DateOnly(1987, 8, 9), "Mae V4 Base Compartilhada",
            [], [], identifiers);
        var manifest = new IngestionPackageManifest(
            2, 4, batch.CodigoSistemaOrigem, null, null, null, batch.DataReferencia, baseCode);

        await repository.PersistValidatedAsync(
            batch, new ParsedPackage(manifest, [person], []), CancellationToken.None);

        await using var verify = new SqlConnection(connectionString);
        await verify.OpenAsync();
        await using var query = verify.CreateCommand();
        query.CommandText = """
            SELECT b.codigo,COUNT(i.pessoa_identificador_observacao_id),
                   MAX(CASE WHEN i.tipo_identificador_codigo='CODIGO_BASE_ORIGEM' THEN i.status_validacao END),
                   MAX(CASE WHEN i.tipo_identificador_codigo='CNS' THEN i.status_validacao END)
            FROM silver.pessoa_observacao po
            JOIN silver.pessoa_origem porg ON porg.pessoa_origem_id=po.pessoa_origem_id
            JOIN ref.base_pessoa_origem b ON b.base_pessoa_origem_id=porg.base_pessoa_origem_id
            LEFT JOIN silver.pessoa_identificador_observacao i ON i.pessoa_observacao_id=po.pessoa_observacao_id
            WHERE po.lote_id=@lote AND po.codigo_pessoa_origem=@codigo
            GROUP BY b.codigo;
            """;
        query.Parameters.AddWithValue("@lote", batch.LoteId);
        query.Parameters.AddWithValue("@codigo", sourceCode);
        await using var reader = await query.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetString(0), Is.EqualTo(baseCode));
            Assert.That(reader.GetInt32(1), Is.EqualTo(3));
            Assert.That(reader.GetString(2), Is.EqualTo("VALIDO"));
            Assert.That(reader.GetString(3), Is.EqualTo("NAO_VALIDADO"));
        });
    }

    [Test]
    public async Task V4_jornada_uuid_feedback_reuses_existing_canonical_person_without_creating_external_map()
    {
        var connectionString = RequireIntegrationConnection();
        await PrepareDatabaseAsync(connectionString);
        await SetOneCadastralBatchPendingAsync(connectionString);
        var repository = CreateRepository(connectionString);
        var batch = await repository.ReserveNextAsync(CancellationToken.None);
        Assert.That(batch, Is.Not.Null);

        var uuid = Guid.NewGuid();
        await using (var setup = new SqlConnection(connectionString))
        {
            await setup.OpenAsync();
            await using var command = setup.CreateCommand();
            command.CommandText = "INSERT identidade.pessoa(pessoa_uuid,status) VALUES(@uuid,'ATIVO');";
            command.Parameters.AddWithValue("@uuid", uuid);
            await command.ExecuteNonQueryAsync();
        }

        var identifier = new ParsedPersonIdentifier(
            "UUID_JORNADA", "JORNADA", uuid.ToString("D"), uuid.ToString("D"),
            null, null, "DECLARADO", null, null, false);
        var person = new ParsedPerson(
            "DELIVERY-V4-UUID", null, new string('9', 64), "TX-V4-UUID", null, "SEM_CPF",
            "Pessoa V4 UUID Jornada", new DateOnly(1995, 2, 11), "Mae V4 UUID Jornada",
            [], [], [identifier]);
        var manifest = new IngestionPackageManifest(
            2, 4, batch!.CodigoSistemaOrigem, null, null, null, batch.DataReferencia);

        await repository.PersistValidatedAsync(
            batch, new ParsedPackage(manifest, [person], []), CancellationToken.None);

        await using var verify = new SqlConnection(connectionString);
        await verify.OpenAsync();
        await using var query = verify.CreateCommand();
        query.CommandText = """
            SELECT vc.status,vc.metodo_resolucao,vc.pessoa_uuid,i.status_validacao,
                   (SELECT COUNT(*) FROM identidade.identity_map m WHERE m.pessoa_uuid=@uuid)
            FROM silver.pessoa_observacao po
            JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=po.pessoa_observacao_id
            JOIN silver.pessoa_identificador_observacao i
              ON i.pessoa_observacao_id=po.pessoa_observacao_id
             AND i.tipo_identificador_codigo='UUID_JORNADA'
            WHERE po.lote_id=@lote AND po.id_pessoa_entrega='DELIVERY-V4-UUID';
            """;
        query.Parameters.AddWithValue("@uuid", uuid);
        query.Parameters.AddWithValue("@lote", batch.LoteId);
        await using var reader = await query.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetString(0), Is.EqualTo("RESOLVIDO"));
            Assert.That(reader.GetString(1), Is.EqualTo("UUID_JORNADA_RETROALIMENTACAO"));
            Assert.That(reader.GetGuid(2), Is.EqualTo(uuid));
            Assert.That(reader.GetString(3), Is.EqualTo("VALIDO"));
            Assert.That(reader.GetInt32(4), Is.Zero);
        });
    }

    [Test]
    public async Task Expired_lease_is_fenced_after_recovery_and_new_reservation()
    {
        var connectionString = RequireIntegrationConnection();
        await PrepareDatabaseAsync(connectionString);
        await SetOnePendingAsync(connectionString);
        var repository = CreateRepository(connectionString);
        var oldBatch = await repository.ReserveNextAsync("worker-old", TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.That(oldBatch, Is.Not.Null);

        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            using var expire = connection.CreateCommand();
            expire.CommandText = "UPDATE ingestao.lote SET lease_expira_em=DATEADD(SECOND,-1,SYSUTCDATETIME()) WHERE lote_id=@id;";
            expire.Parameters.AddWithValue("@id", oldBatch!.LoteId);
            await expire.ExecuteNonQueryAsync();
        }
        Assert.That(await repository.RecoverExpiredLeasesAsync(5, CancellationToken.None), Is.EqualTo(1));
        var newBatch = await repository.ReserveNextAsync("worker-new", TimeSpan.FromMinutes(2), CancellationToken.None);
        Assert.That(newBatch, Is.Not.Null);
        Assert.That(newBatch!.LeaseId, Is.Not.EqualTo(oldBatch!.LeaseId));

        Assert.ThrowsAsync<SqlException>(async () =>
            await repository.MarkRejectedAsync(oldBatch!, "STALE_WORKER", CancellationToken.None));
    }

    private static async Task SetOneCadastralBatchPendingAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DECLARE @lote UNIQUEIDENTIFIER=(
                SELECT TOP(1) l.lote_id
                FROM ingestao.lote l
                JOIN ingestao.entrega e ON e.entrega_id=l.entrega_id
                WHERE e.natureza IS NULL
                ORDER BY l.criado_em,l.lote_id);
            IF @lote IS NULL THROW 51299,'Fixture sem lote cadastral.',1;
            UPDATE ingestao.lote
               SET status='PENDENTE',erro_codigo=NULL,tentativa_count=0,recuperacao_count=0,
                   lease_id=NULL,lease_owner=NULL,lease_adquirido_em=NULL,heartbeat_em=NULL,lease_expira_em=NULL,
                   ultima_tentativa_em=NULL,proxima_tentativa_em=NULL,poison_em=NULL,atualizado_em=SYSUTCDATETIME()
             WHERE lote_id=@lote;
            UPDATE e SET status='RECEBIDA',ultima_atualizacao=SYSUTCDATETIME()
            FROM ingestao.entrega e
            JOIN ingestao.lote l ON l.entrega_id=e.entrega_id
            WHERE l.lote_id=@lote;
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SetOnePendingAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = """
            DECLARE @lote UNIQUEIDENTIFIER=(SELECT TOP(1) lote_id FROM ingestao.lote ORDER BY criado_em,lote_id);
            UPDATE ingestao.lote SET status='PENDENTE',erro_codigo=NULL,tentativa_count=0,recuperacao_count=0,
                lease_id=NULL,lease_owner=NULL,lease_adquirido_em=NULL,heartbeat_em=NULL,lease_expira_em=NULL,
                ultima_tentativa_em=NULL,proxima_tentativa_em=NULL,poison_em=NULL,atualizado_em=SYSUTCDATETIME() WHERE lote_id=@lote;
            UPDATE e SET status='RECEBIDA',ultima_atualizacao=SYSUTCDATETIME()
            FROM ingestao.entrega e JOIN ingestao.lote l ON l.entrega_id=e.entrega_id WHERE l.lote_id=@lote;
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static SqlProcessorRepository CreateRepository(string connectionString)
    {
        return new SqlProcessorRepository(
            new OperationalSqlAdapter(connectionString!),
            new RegistryQualityEngine(new IRegistryQualityEvaluator[]
            {
                new PositiveGrantedValueRegistryQcEvaluator("AA01", 1),
                new PositiveGrantedValueRegistryQcEvaluator("POT1", 1)
            }));
    }

    private static string RequireIntegrationConnection()
    {
        var connectionString = Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString)) Assert.Ignore("Defina JORNADA_TEST_SQL_CONNECTION para executar testes SQL Server.");
        var csb = new SqlConnectionStringBuilder(connectionString);
        var db = csb.InitialCatalog ?? string.Empty;
        if (!db.Contains("test", StringComparison.OrdinalIgnoreCase) && !db.Contains("dev", StringComparison.OrdinalIgnoreCase) && !db.Contains("local", StringComparison.OrdinalIgnoreCase))
            Assert.Fail("Por segurança, o banco de integração deve conter 'Test', 'Dev' ou 'Local' no nome.");
        return connectionString!;
    }

    private static async Task PrepareDatabaseAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteCanonicalSchemaAsync(connection, databaseDir);
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Seed_Dev.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection,
            Path.Combine(databaseDir, "migrations", "20260913_Base_Pessoa_Origem.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection,
            Path.Combine(databaseDir, "migrations", "20260913_Pessoa_Identificadores_Multiplos.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection,
            Path.Combine(databaseDir, "migrations", "20260913_Pessoa_Observacao_Sem_Identificador.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection,
            Path.Combine(databaseDir, "migrations", "20260919_Pessoa_Origem_Runtime_V4_Cutover.sql"));
        using var reset = connection.CreateCommand();
        reset.CommandText = """
            UPDATE ingestao.lote SET status='PROCESSADO',erro_codigo=NULL,lease_id=NULL,lease_owner=NULL,lease_adquirido_em=NULL,heartbeat_em=NULL,lease_expira_em=NULL,proxima_tentativa_em=NULL,poison_em=NULL,atualizado_em=SYSUTCDATETIME();
            UPDATE ingestao.entrega SET status='PROCESSADA',ultima_atualizacao=SYSDATETIMEOFFSET();
            """;
        await reset.ExecuteNonQueryAsync();
    }
}
