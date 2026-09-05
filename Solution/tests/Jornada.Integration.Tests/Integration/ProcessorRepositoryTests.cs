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
            "PROC-V325", new string('a',64), "TX-PROC-V325", "98765432100", null, "Pessoa Teste Processor", new DateOnly(1990,1,1), "Mae Teste", [], []);
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
    public async Task Shared_cpf_conflict_materializes_fact_in_gold_without_canonical_assignment()
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
        // A observação abaixo simula o CPF do adulto informado no cadastro de uma criança.
        var person = new ParsedPerson(
            "CPF-COMPARTILHADO-FILHO", new string('e',64), "TX-CPF-COMPARTILHADO", "52998224725", null,
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
              (SELECT COUNT(*) FROM serving.v_bi_pendencias_identidade p WHERE p.pessoa_observacao_id=po.pessoa_observacao_id AND p.motivo='CPF_COMPARTILHADO_SUSPEITO'),
              (SELECT TOP(1) estado FROM identidade.identity_map WHERE tipo='CPF' AND identificador='52998224725' AND vigencia_fim IS NULL),
              (SELECT TOP(1) estado_motivo FROM identidade.identity_map WHERE tipo='CPF' AND identificador='52998224725' AND vigencia_fim IS NULL)
            FROM silver.pessoa_observacao po
            JOIN identidade.v_vinculo_corrente vf ON vf.pessoa_observacao_id=po.pessoa_observacao_id
            WHERE po.codigo_pessoa_origem='CPF-COMPARTILHADO-FILHO'
            ORDER BY po.pessoa_observacao_id DESC;
            """;
        using var reader = await query.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetString(0), Is.EqualTo("CONFLITO"));
            Assert.That(reader.GetString(1), Is.EqualTo("CPF_COMPARTILHADO_SUSPEITO"));
            Assert.That(reader.IsDBNull(2), Is.True, "Conflito de CPF não pode receber pessoa_uuid.");
            Assert.That(reader.GetInt32(3), Is.EqualTo(1), "O fato bruto permanece em Silver para auditoria/reprocessamento.");
            Assert.That(reader.GetInt32(4), Is.EqualTo(1), "O fato declarado deve permanecer materializado na Gold, independentemente da identidade canônica.");
            Assert.That(reader.GetString(5), Is.EqualTo("CONFLITO_IDENTIDADE"));
            Assert.That(reader.GetString(6), Is.EqualTo("52998224725"), "O CPF declarado é snapshot imutável da declaração factual.");
            Assert.That(reader.IsDBNull(7), Is.True, "Fato em conflito não pode ser atribuído a uma Pessoa canônica.");
            Assert.That(reader.GetInt32(8), Is.EqualTo(1), "O conflito precisa alimentar a fila de pendências de identidade.");
            Assert.That(reader.GetString(9), Is.EqualTo("EM_CONFLITO"), "O conflito deve subir para o próprio identificador CPF.");
            Assert.That(reader.GetString(10), Is.EqualTo("CPF_COMPARTILHADO_SUSPEITO"));
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
            "16899535009", new string('a',64), "TX-OPAQUE-CODE", null, "SEM_CPF",
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
    public async Task Existing_cpf_map_without_comparable_core_fails_closed_without_uuid()
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
            "CPF-SEM-NUCLEO", new string('a',64), "TX-CPF-SEM-NUCLEO", "16899535009", null,
            "Pessoa de Teste", new DateOnly(1991,5,17), "Mae de Teste", [], []);
        var manifest = new IngestionPackageManifest(2, batch!.PessoaSchemaVersao,
            batch.CodigoSistemaOrigem, batch.Natureza, batch.CodigoTipo, batch.TipoVersao, batch.DataReferencia);

        await repository.PersistValidatedAsync(batch, new ParsedPackage(manifest, [person], []), CancellationToken.None);

        await using var verify = new SqlConnection(connectionString);
        await verify.OpenAsync();
        using var query = verify.CreateCommand();
        query.CommandText = """
            SELECT vf.status,vf.motivo,vf.pessoa_uuid
            FROM silver.pessoa_observacao po
            JOIN identidade.v_vinculo_corrente vf ON vf.pessoa_observacao_id=po.pessoa_observacao_id
            WHERE po.codigo_pessoa_origem='CPF-SEM-NUCLEO'
            ORDER BY po.pessoa_observacao_id DESC;
            """;
        using var reader = await query.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetString(0), Is.EqualTo("CONFLITO"));
            Assert.That(reader.GetString(1), Is.EqualTo("CPF_NUCLEO_EXISTENTE_INDISPONIVEL"));
            Assert.That(reader.IsDBNull(2), Is.True);
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

        // READPAST é fail-fast por desenho: sob contenção o SQL Server pode fazer uma tentativa
        // retornar null mesmo havendo outro lote que ficará visível no próximo poll (por exemplo,
        // se a granularidade efetiva do lock for maior que uma linha). O contrato do worker é
        // segurança + progresso eventual; ProcessorWorker já repete o poll quando recebe null.
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
            "PROC-V325-ROLLBACK-A", new string('c',64), "TX-ROLLBACK-A", "31415926590", null, "Pessoa Rollback A", new DateOnly(1991, 2, 3), "Mae Rollback A", [], []);
        var failsAfterSilver = new ParsedPerson(
            "PROC-V325-ROLLBACK-B", new string('d',64), "TX-ROLLBACK-B", "27182818205", null, "Pessoa Rollback B", new DateOnly(1992, 3, 4), "Mae Rollback B",
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
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Fase1.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Seed_Dev.sql"));
        // Isola os testes do estado deixado por uma execução anterior no mesmo banco DEV/Test.
        using var reset = connection.CreateCommand();
        reset.CommandText = """
            UPDATE ingestao.lote SET status='PROCESSADO',erro_codigo=NULL,lease_id=NULL,lease_owner=NULL,lease_adquirido_em=NULL,heartbeat_em=NULL,lease_expira_em=NULL,proxima_tentativa_em=NULL,poison_em=NULL,atualizado_em=SYSUTCDATETIME();
            UPDATE ingestao.entrega SET status='PROCESSADA',ultima_atualizacao=SYSDATETIMEOFFSET();
            """;
        await reset.ExecuteNonQueryAsync();
    }
}
