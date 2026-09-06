using System.Data;
using System.Data.Common;
using Jornada.Contracts;
using Jornada.Operational.Sql;

namespace Jornada.Processor.Worker;

internal sealed partial class PostgreSqlProcessorRepository : IProcessorRepository
{
    private readonly IOperationalDatabaseAdapter database;
    private readonly PostgreSqlProcessorLeaseRepositoryAdapter leases;
    private readonly RegistryQualityEngine quality;

    public PostgreSqlProcessorRepository(
        IOperationalDatabaseAdapter database,
        PostgreSqlProcessorLeaseRepositoryAdapter leases,
        RegistryQualityEngine quality)
    {
        this.database = database ?? throw new ArgumentNullException(nameof(database));
        this.leases = leases ?? throw new ArgumentNullException(nameof(leases));
        this.quality = quality ?? throw new ArgumentNullException(nameof(quality));
        if (!string.Equals(database.Provider, OperationalDatabaseProviders.PostgreSql, StringComparison.Ordinal))
            throw new ArgumentException("PostgreSqlProcessorRepository exige provider PostgreSql.", nameof(database));
    }

    public Task<int> RecoverExpiredLeasesAsync(int maxAttempts, CancellationToken ct) =>
        leases.RecoverExpiredLeasesAsync(maxAttempts, ct);

    public Task<ReservedBatch?> ReserveNextAsync(string leaseOwner, TimeSpan leaseDuration, CancellationToken ct) =>
        leases.ReserveNextAsync(leaseOwner, leaseDuration, ct);

    public Task<bool> HeartbeatAsync(ReservedBatch batch, TimeSpan leaseDuration, CancellationToken ct) =>
        leases.HeartbeatAsync(batch, leaseDuration, ct);

    public Task MarkRejectedAsync(ReservedBatch batch, string errorCode, CancellationToken ct) =>
        leases.MarkRejectedAsync(batch, errorCode, ct);

    public Task MarkQuarantineAsync(ReservedBatch batch, string errorCode, CancellationToken ct) =>
        leases.MarkQuarantineAsync(batch, errorCode, ct);

    public Task<ProcessingFailureOutcome> ScheduleRetryOrPoisonAsync(
        ReservedBatch batch, string errorCode, int maxAttempts, TimeSpan retryBase, TimeSpan retryMax, CancellationToken ct) =>
        leases.ScheduleRetryOrPoisonAsync(batch, errorCode, maxAttempts, retryBase, retryMax, ct);

    public async Task PersistValidatedAsync(ReservedBatch batch, ParsedPackage package, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            await SetProcessingAsync(connection, tx, batch, ct);
            var peopleBySource = new Dictionary<string, PgProcessedPerson>(StringComparer.Ordinal);
            foreach (var person in package.Pessoas)
            {
                var processed = await PersistPersonAsync(connection, tx, batch, person, ct);
                peopleBySource[person.CodigoPessoaOrigem] = processed;
            }

            foreach (var fact in package.Registros)
            {
                if (!peopleBySource.TryGetValue(fact.CodigoPessoaOrigem, out var person))
                    throw new InvalidDataException($"Pessoa de origem não encontrada para registro: {fact.CodigoPessoaOrigem}.");
                await PersistFactAsync(connection, tx, batch, person, fact, ct);
            }

            await using (var finish = Command(connection, tx, """
                UPDATE ingestao.lote
                   SET qtd_pessoas=@qtd_pessoas,qtd_registros=@qtd_registros,status='PROCESSADO',erro_codigo=NULL,
                       lease_id=NULL,lease_owner=NULL,lease_adquirido_em=NULL,lease_expira_em=NULL,heartbeat_em=NULL,
                       proxima_tentativa_em=NULL,atualizado_em=CURRENT_TIMESTAMP
                 WHERE lote_id=@lote_id AND lease_id=@lease_id AND lease_owner=@lease_owner
                RETURNING lote_id;
                """))
            {
                Add(finish, "@qtd_pessoas", DbType.Int32, package.Pessoas.Count);
                Add(finish, "@qtd_registros", DbType.Int32, package.Registros.Count);
                Add(finish, "@lote_id", DbType.Guid, batch.LoteId);
                Add(finish, "@lease_id", DbType.Guid, batch.LeaseId);
                Add(finish, "@lease_owner", DbType.String, batch.LeaseOwner, 200);
                if (await finish.ExecuteScalarAsync(ct) is null)
                    throw new InvalidOperationException("Lease perdido antes da publicação final do lote.");
            }

            await using (var recalc = Command(connection, tx, "SELECT ingestao.recalcular_entrega(@entrega_id);"))
            {
                Add(recalc, "@entrega_id", DbType.Guid, batch.EntregaId);
                await recalc.ExecuteNonQueryAsync(ct);
            }

            await using (var serving = Command(connection, tx, """
                UPDATE serving.registro_integrado ri
                   SET entrega_completa=TRUE,atualizado_em=CURRENT_TIMESTAMP
                  FROM ingestao.entrega e
                 WHERE e.entrega_id=@entrega_id
                   AND e.status='PROCESSADA'
                   AND ri.entrega_id=e.entrega_id;
                """))
            {
                Add(serving, "@entrega_id", DbType.Guid, batch.EntregaId);
                await serving.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task SetProcessingAsync(DbConnection connection, DbTransaction tx, ReservedBatch batch, CancellationToken ct)
    {
        await using var command = Command(connection, tx, """
            UPDATE ingestao.lote
               SET status='PROCESSANDO',atualizado_em=CURRENT_TIMESTAMP
             WHERE lote_id=@lote_id AND lease_id=@lease_id AND lease_owner=@lease_owner
            RETURNING lote_id;
            """);
        Add(command, "@lote_id", DbType.Guid, batch.LoteId);
        Add(command, "@lease_id", DbType.Guid, batch.LeaseId);
        Add(command, "@lease_owner", DbType.String, batch.LeaseOwner, 200);
        if (await command.ExecuteScalarAsync(ct) is null)
            throw new InvalidOperationException("Lease perdido antes do início da persistência PostgreSQL.");
    }

    private async Task<PgProcessedPerson> PersistPersonAsync(
        DbConnection connection, DbTransaction tx, ReservedBatch batch, ParsedPerson person, CancellationToken ct)
    {
        var pessoaOrigemId = await EnsurePersonOriginAsync(connection, tx, batch.SistemaOrigemId, person.CodigoPessoaOrigem, ct);
        var latest = await GetLatestPersonVersionAsync(connection, tx, pessoaOrigemId, ct);
        if (latest is not null && string.Equals(latest.ConteudoHash, person.ConteudoHash, StringComparison.Ordinal))
        {
            await TouchPersonOriginAsync(connection, tx, pessoaOrigemId, batch.DataReferencia, ct);
            await RecordProcessedItemAsync(connection, tx, batch, "PESSOA", pessoaOrigemId, null,
                person.CodigoPessoaOrigem, "RETRANSMITIDO", latest.VersaoInterna, person.ConteudoHash, ct);
            var retransmitted = await LoadProcessedPersonAsync(connection, tx, latest.ObservationId, ct);
            if (retransmitted.PessoaUuid is Guid retransmittedUuid)
                await RefreshGoldPersonAsync(connection, tx, retransmittedUuid, ct);
            return retransmitted;
        }

        var internalVersion = (latest?.VersaoInterna ?? 0) + 1;
        var nomeCmp = IdentityComparison.NormalizeText(person.NomeCompleto) ?? person.NomeCompleto.ToUpperInvariant();
        var maeCmp = IdentityComparison.NormalizeText(person.NomeMae) ?? person.NomeMae.ToUpperInvariant();
        long observationId;
        await using (var insert = Command(connection, tx, """
            INSERT INTO silver.pessoa_observacao(
                pessoa_origem_id,lote_id,gestor_id,codigo_pessoa_origem,versao_interna,conteudo_hash,
                cpf,cpf_ausente_motivo,nome_completo,nome_cmp,data_nascimento,nome_mae,nome_mae_cmp,source_as_of)
            VALUES(@pessoa_origem_id,@lote_id,@gestor_id,@codigo,@versao,@hash,
                   @cpf,@cpf_motivo,@nome,@nome_cmp,@nascimento,@mae,@mae_cmp,@source_as_of)
            RETURNING pessoa_observacao_id;
            """))
        {
            Add(insert, "@pessoa_origem_id", DbType.Int64, pessoaOrigemId);
            Add(insert, "@lote_id", DbType.Guid, batch.LoteId);
            Add(insert, "@gestor_id", DbType.Int64, batch.GestorId);
            Add(insert, "@codigo", DbType.String, person.CodigoPessoaOrigem, 255);
            Add(insert, "@versao", DbType.Int32, internalVersion);
            Add(insert, "@hash", DbType.AnsiStringFixedLength, person.ConteudoHash, 64);
            Add(insert, "@cpf", DbType.AnsiStringFixedLength, person.Cpf, 11);
            Add(insert, "@cpf_motivo", DbType.String, person.CpfAusenteMotivo, 30);
            Add(insert, "@nome", DbType.String, person.NomeCompleto, 500);
            Add(insert, "@nome_cmp", DbType.String, nomeCmp, 500);
            AddDate(insert, "@nascimento", person.DataNascimento);
            Add(insert, "@mae", DbType.String, person.NomeMae, 500);
            Add(insert, "@mae_cmp", DbType.String, maeCmp, 500);
            Add(insert, "@source_as_of", DbType.DateTimeOffset, batch.DataReferencia);
            observationId = Convert.ToInt64(await insert.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
        }

        var cpf = string.IsNullOrWhiteSpace(person.Cpf) ? null : CpfRules.NormalizeAndValidate(person.Cpf);
        InternalIdentityResolution identity;
        if (!string.IsNullOrWhiteSpace(person.Cpf) && cpf is null)
        {
            identity = new InternalIdentityResolution(
                ResolutionStatus.CONFLITO, null, ResolutionMethod.CPF_DETERMINISTICO, Motivo: "CPF_INVALIDO");
        }
        else if (cpf is not null)
        {
            identity = await PostgreSqlIdentityPersistence.ResolveOrCreateByCpfAsync(
                connection, tx, cpf,
                new IdentityCore(person.NomeCompleto, person.DataNascimento, person.NomeMae),
                batch.GestorId, person.CodigoPessoaOrigem, ct);
        }
        else
        {
            identity = new InternalIdentityResolution(
                ResolutionStatus.NAO_RESOLVIDO, null, ResolutionMethod.PENDENTE_PROBABILISTICO,
                Motivo: "AGUARDA_LINKAGE_SOB_DEMANDA");
        }

        await using (var link = Command(connection, tx, """
            INSERT INTO identidade.vinculo_fonte(
                pessoa_observacao_id,pessoa_uuid,metodo_resolucao,score,status,modelo_id,ativo,resolvido_em,motivo)
            VALUES(@obs,@uuid,@metodo,NULL,@status,NULL,TRUE,@resolvido_em,@motivo);
            """))
        {
            Add(link, "@obs", DbType.Int64, observationId);
            Add(link, "@uuid", DbType.Guid, identity.PessoaUuid);
            Add(link, "@metodo", DbType.String, identity.MetodoResolucao.ToString(), 40);
            Add(link, "@status", DbType.String, identity.Status.ToString(), 30);
            Add(link, "@resolvido_em", DbType.DateTimeOffset, identity.PessoaUuid.HasValue ? DateTimeOffset.UtcNow : null);
            Add(link, "@motivo", DbType.String, identity.Motivo, 120);
            await link.ExecuteNonQueryAsync(ct);
        }

        if (identity.Status == ResolutionStatus.CONFLITO)
            await RecordIdentityDivergenceAsync(connection, tx, batch.GestorId, observationId, person.CodigoPessoaOrigem,
                identity.Motivo ?? "CONFLITO_IDENTIDADE", ct);

        foreach (var verification in person.ConferenciasDocumentais)
        {
            await using var verify = Command(connection, tx, """
                INSERT INTO silver.pessoa_campo_verificacao_observacao(
                    pessoa_observacao_id,gestor_id,campo_codigo,evidencia_tipo,referencia_evidencia,verificado_em,source_transaction_id)
                VALUES(@obs,@gestor,@campo,@evidencia,@referencia,@verificado,@source_transaction);
                """);
            Add(verify, "@obs", DbType.Int64, observationId);
            Add(verify, "@gestor", DbType.Int64, batch.GestorId);
            Add(verify, "@campo", DbType.String, verification.CampoCodigo, 40);
            Add(verify, "@evidencia", DbType.String, verification.EvidenciaTipo, 80);
            Add(verify, "@referencia", DbType.String, verification.ReferenciaEvidencia, 255);
            Add(verify, "@verificado", DbType.DateTimeOffset, verification.VerificadoEm);
            Add(verify, "@source_transaction", DbType.String, person.SourceTransactionId, 255);
            await verify.ExecuteNonQueryAsync(ct);
        }

        var attributes = new List<PgPersistedAttribute>();
        var attributeInstances = new HashSet<string>(StringComparer.Ordinal);
        foreach (var attribute in person.Atributos)
        {
            if (string.Equals(attribute.StatusEvidencia, "COMPROVADO", StringComparison.OrdinalIgnoreCase)
                && !attribute.VerificadoEm.HasValue)
                throw new InvalidDataException($"Atributo COMPROVADO sem verificadoEm: {attribute.AtributoCodigo}.");

            var identityRule = await ResolveAttributeIdentityRuleAsync(connection, tx, attribute.AtributoCodigo, ct);
            var instanceKey = TransversalAttributeInstanceKey.Compute(identityRule.Cardinality, identityRule.InstanceKeyRule, attribute.Valor);
            if (!attributeInstances.Add(attribute.AtributoCodigo + "\u001f" + instanceKey))
                throw new InvalidDataException(
                    $"pessoas.jsonl: atributo/instância duplicado na mesma Pessoa: {attribute.AtributoCodigo} / {instanceKey}.");

            long attributeObservationId;
            await using (var attr = Command(connection, tx, """
                INSERT INTO silver.pessoa_atributo_observacao(
                    source_record_id,pessoa_observacao_id,fonte_gestor_id,atributo_codigo,atributo_instancia_chave,valor,
                    status_evidencia,evidencia_tipo,referencia_evidencia,verificado_em,atualizado_em_origem,ingested_at)
                VALUES(@source_record_id,@pessoa_observacao_id,@gestor_id,@codigo,@instancia,@valor,
                       @status,@evidencia_tipo,@referencia,@verificado,@atualizado,CURRENT_TIMESTAMP)
                RETURNING pessoa_atributo_observacao_id;
                """))
            {
                Add(attr, "@source_record_id", DbType.String, attribute.SourceRecordId, 255);
                Add(attr, "@pessoa_observacao_id", DbType.Int64, observationId);
                Add(attr, "@gestor_id", DbType.Int64, batch.GestorId);
                Add(attr, "@codigo", DbType.String, attribute.AtributoCodigo, 80);
                Add(attr, "@instancia", DbType.String, instanceKey, 512);
                Add(attr, "@valor", DbType.String, attribute.Valor, 2000);
                Add(attr, "@status", DbType.String, attribute.StatusEvidencia, 20);
                Add(attr, "@evidencia_tipo", DbType.String, attribute.EvidenciaTipo, 80);
                Add(attr, "@referencia", DbType.DateTimeOffset, attribute.ReferenciaEvidencia);
                Add(attr, "@verificado", DbType.DateTimeOffset, attribute.VerificadoEm);
                Add(attr, "@atualizado", DbType.DateTimeOffset, attribute.AtualizadoEmOrigem);
                attributeObservationId = Convert.ToInt64(await attr.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
            }
            var persisted = new PgPersistedAttribute(attributeObservationId, attribute, instanceKey, identityRule.Cardinality);
            attributes.Add(persisted);

            long? subprefeituraId = null;
            long? distritoId = null;
            if (attribute.Geografia is not null)
                (subprefeituraId, distritoId) = await EnsureGeographyIdsAsync(connection, tx, attribute.Geografia, ct);

            if (string.Equals(attribute.AtributoCodigo, "ENDERECO_RESIDENCIAL", StringComparison.OrdinalIgnoreCase))
            {
                await InsertTerritorialReferenceAsync(connection, tx, attributeObservationId, TerritorialReferenceNature.DOMICILIAR,
                    "ENDERECO_RESIDENCIAL", subprefeituraId, distritoId, attribute.SituacaoGeografia, attribute.Geografia, batch.DataReferencia, ct);
            }
            else if (string.Equals(attribute.AtributoCodigo, "REFERENCIA_TERRITORIAL", StringComparison.OrdinalIgnoreCase))
            {
                if (!attribute.NaturezaReferenciaTerritorial.HasValue)
                    throw new InvalidDataException("REFERENCIA_TERRITORIAL sem natureza declarada pela fonte.");
                await InsertTerritorialReferenceAsync(connection, tx, attributeObservationId, attribute.NaturezaReferenciaTerritorial.Value,
                    "REFERENCIA_TERRITORIAL", subprefeituraId, distritoId, attribute.SituacaoGeografia, attribute.Geografia, batch.DataReferencia, ct);
            }
        }

        var selectedGeography = await SelectTerritorialReferenceAsync(connection, tx, observationId, ct);
        if (identity.PessoaUuid is Guid uuid)
        {
            await RefreshGoldPersonAsync(connection, tx, uuid, ct);
            foreach (var attribute in attributes.Where(a =>
                         string.Equals(a.Value.StatusEvidencia, "COMPROVADO", StringComparison.OrdinalIgnoreCase)))
                await PromoteAttributeAsync(connection, tx, batch.GestorId, uuid, attribute, ct);
        }

        await TouchPersonOriginAsync(connection, tx, pessoaOrigemId, batch.DataReferencia, ct);
        await RecordProcessedItemAsync(connection, tx, batch, "PESSOA", pessoaOrigemId, null,
            person.CodigoPessoaOrigem, latest is null ? "INCLUIDO" : "VERSIONADO", internalVersion, person.ConteudoHash, ct);

        return new PgProcessedPerson(observationId, pessoaOrigemId, batch.SistemaOrigemId, person.CodigoPessoaOrigem,
            person.Cpf, person.CpfAusenteMotivo, identity.PessoaUuid, ToAssignmentState(identity.Status),
            selectedGeography.ReferenciaTerritorialObservacaoId, selectedGeography.NaturezaReferenciaTerritorial,
            selectedGeography.SubprefeituraId, selectedGeography.DistritoId);
    }

    private static async Task<long> EnsurePersonOriginAsync(
        DbConnection connection, DbTransaction tx, long sistemaOrigemId, string code, CancellationToken ct)
    {
        await using var command = Command(connection, tx, """
            INSERT INTO silver.pessoa_origem(sistema_origem_id,codigo_pessoa_origem,ultima_recepcao_em)
            VALUES(@sistema,@codigo,CURRENT_TIMESTAMP)
            ON CONFLICT(sistema_origem_id,codigo_pessoa_origem)
            DO UPDATE SET ultima_recepcao_em=CURRENT_TIMESTAMP
            RETURNING pessoa_origem_id;
            """);
        Add(command, "@sistema", DbType.Int64, sistemaOrigemId);
        Add(command, "@codigo", DbType.String, code, 255);
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<PgLatestPerson?> GetLatestPersonVersionAsync(
        DbConnection connection, DbTransaction tx, long pessoaOrigemId, CancellationToken ct)
    {
        await using var command = Command(connection, tx, """
            SELECT pessoa_observacao_id,versao_interna,conteudo_hash
              FROM silver.pessoa_observacao
             WHERE pessoa_origem_id=@id
             ORDER BY versao_interna DESC
             LIMIT 1
             FOR UPDATE;
            """);
        Add(command, "@id", DbType.Int64, pessoaOrigemId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new PgLatestPerson(reader.GetInt64(0), reader.GetInt32(1), reader.GetString(2))
            : null;
    }

    private static async Task<PgProcessedPerson> LoadProcessedPersonAsync(
        DbConnection connection, DbTransaction tx, long observationId, CancellationToken ct)
    {
        await using var command = Command(connection, tx, """
            SELECT po.pessoa_observacao_id,po.pessoa_origem_id,porg.sistema_origem_id,po.codigo_pessoa_origem,
                   po.cpf,po.cpf_ausente_motivo,v.pessoa_uuid,v.status,
                   rt.referencia_territorial_observacao_id,rt.natureza_referencia,rt.subprefeitura_id,rt.distrito_id
              FROM silver.pessoa_observacao po
              JOIN silver.pessoa_origem porg ON porg.pessoa_origem_id=po.pessoa_origem_id
              LEFT JOIN identidade.v_vinculo_corrente v ON v.pessoa_observacao_id=po.pessoa_observacao_id
              LEFT JOIN silver.v_pessoa_referencia_territorial rt ON rt.pessoa_observacao_id=po.pessoa_observacao_id
             WHERE po.pessoa_observacao_id=@obs;
            """);
        Add(command, "@obs", DbType.Int64, observationId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new InvalidOperationException($"Observação PostgreSQL não encontrada: {observationId}.");
        var status = reader.IsDBNull(7) ? ResolutionStatus.NAO_RESOLVIDO : Enum.Parse<ResolutionStatus>(reader.GetString(7));
        return new PgProcessedPerson(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetFieldValue<Guid>(6), ToAssignmentState(status),
            reader.IsDBNull(8) ? null : reader.GetInt64(8), reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetInt64(10), reader.IsDBNull(11) ? null : reader.GetInt64(11));
    }

    private static async Task<PgAttributeRule> ResolveAttributeIdentityRuleAsync(
        DbConnection connection, DbTransaction tx, string attributeCode, CancellationToken ct)
    {
        await using var command = Command(connection, tx,
            "SELECT cardinalidade,chave_instancia_codigo FROM ref.atributo_transversal WHERE atributo_codigo=@codigo AND ativo=TRUE;");
        Add(command, "@codigo", DbType.String, attributeCode, 80);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new InvalidDataException($"Atributo transversal inexistente/inativo: {attributeCode}.");
        return new PgAttributeRule(reader.GetString(0), reader.GetString(1));
    }

    private static async Task<(long? SubprefeituraId, long? DistritoId)> EnsureGeographyIdsAsync(
        DbConnection connection, DbTransaction tx, ReferenceGeography geography, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(geography.DistritoCodigo) || string.IsNullOrWhiteSpace(geography.SubprefeituraCodigo)
            || string.IsNullOrWhiteSpace(geography.DistritoNome) || string.IsNullOrWhiteSpace(geography.SubprefeituraNome))
            return (null, null);

        long subprefeituraId;
        await using (var sub = Command(connection, tx, """
            INSERT INTO ref.subprefeitura(codigo,nome,observado_em)
            VALUES(@codigo,@nome,@observado)
            ON CONFLICT(codigo,nome) DO UPDATE SET observado_em=ref.subprefeitura.observado_em
            RETURNING subprefeitura_id;
            """))
        {
            Add(sub, "@codigo", DbType.String, geography.SubprefeituraCodigo, 30);
            Add(sub, "@nome", DbType.String, geography.SubprefeituraNome, 150);
            Add(sub, "@observado", DbType.DateTimeOffset, geography.ResolvidoEm ?? DateTimeOffset.UtcNow);
            subprefeituraId = Convert.ToInt64(await sub.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
        }

        await using var district = Command(connection, tx, """
            INSERT INTO ref.distrito(subprefeitura_id,codigo,nome,observado_em)
            VALUES(@sub,@codigo,@nome,@observado)
            ON CONFLICT(subprefeitura_id,codigo,nome) DO UPDATE SET observado_em=ref.distrito.observado_em
            RETURNING distrito_id;
            """);
        Add(district, "@sub", DbType.Int64, subprefeituraId);
        Add(district, "@codigo", DbType.String, geography.DistritoCodigo, 30);
        Add(district, "@nome", DbType.String, geography.DistritoNome, 150);
        Add(district, "@observado", DbType.DateTimeOffset, geography.ResolvidoEm ?? DateTimeOffset.UtcNow);
        var distritoId = Convert.ToInt64(await district.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
        return (subprefeituraId, distritoId);
    }

    private static async Task InsertTerritorialReferenceAsync(
        DbConnection connection, DbTransaction tx, long attributeObservationId, TerritorialReferenceNature nature,
        string semanticSource, long? subprefeituraId, long? distritoId, GeographicResolutionStatus? geographyStatus,
        ReferenceGeography? geography, DateTimeOffset dataReferencia, CancellationToken ct)
    {
        await using var command = Command(connection, tx, """
            INSERT INTO silver.referencia_territorial_observacao(
                pessoa_atributo_observacao_id,natureza_referencia,fonte_semantica,subprefeitura_id,distrito_id,
                situacao_geografia,origem_geografia,referencia_malha,resolvido_em)
            VALUES(@atributo,@natureza,@fonte,@subprefeitura,@distrito,@situacao,@origem,@malha,@resolvido);
            """);
        Add(command, "@atributo", DbType.Int64, attributeObservationId);
        Add(command, "@natureza", DbType.String, nature.ToString(), 50);
        Add(command, "@fonte", DbType.String, semanticSource, 30);
        Add(command, "@subprefeitura", DbType.Int64, subprefeituraId);
        Add(command, "@distrito", DbType.Int64, distritoId);
        Add(command, "@situacao", DbType.String, geographyStatus?.ToString(), 40);
        Add(command, "@origem", DbType.String, geography?.Origem.ToString(), 30);
        Add(command, "@malha", DbType.String, geography?.ReferenciaMalha, 120);
        Add(command, "@resolvido", DbType.DateTimeOffset, geography is null ? null : geography.ResolvidoEm ?? dataReferencia);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<PgTerritorialSelection> SelectTerritorialReferenceAsync(
        DbConnection connection, DbTransaction tx, long observationId, CancellationToken ct)
    {
        await using var command = Command(connection, tx, """
            SELECT referencia_territorial_observacao_id,natureza_referencia,subprefeitura_id,distrito_id
              FROM silver.v_pessoa_referencia_territorial WHERE pessoa_observacao_id=@pessoa;
            """);
        Add(command, "@pessoa", DbType.Int64, observationId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct) || reader.IsDBNull(0))
            return new PgTerritorialSelection(null, null, null, null);
        return new PgTerritorialSelection(reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetInt64(2), reader.IsDBNull(3) ? null : reader.GetInt64(3));
    }

    private static async Task TouchPersonOriginAsync(
        DbConnection connection, DbTransaction tx, long pessoaOrigemId, DateTimeOffset dataReferencia, CancellationToken ct)
    {
        await using var command = Command(connection, tx, """
            UPDATE silver.pessoa_origem SET ultima_recepcao_em=CURRENT_TIMESTAMP,
                ultima_referencia_recebida=CASE WHEN ultima_referencia_recebida IS NULL OR @ref > ultima_referencia_recebida
                    THEN @ref ELSE ultima_referencia_recebida END
             WHERE pessoa_origem_id=@id;
            """);
        Add(command, "@ref", DbType.DateTimeOffset, dataReferencia);
        Add(command, "@id", DbType.Int64, pessoaOrigemId);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task RecordIdentityDivergenceAsync(
        DbConnection connection, DbTransaction tx, long gestorId, long observationId, string sourceCode,
        string reason, CancellationToken ct)
    {
        await using var command = Command(connection, tx, """
            INSERT INTO qualidade.divergencia_gestor(
                gestor_id,tipo,motivo,pessoa_observacao_id,codigo_pessoa_origem,status)
            VALUES(@gestor,'DIVERGENCIA_IDENTIDADE',@motivo,@obs,@codigo,'ABERTA')
            ON CONFLICT(gestor_id,pessoa_observacao_id,tipo,motivo) WHERE status='ABERTA' DO NOTHING;
            """);
        Add(command, "@gestor", DbType.Int64, gestorId);
        Add(command, "@obs", DbType.Int64, observationId);
        Add(command, "@codigo", DbType.String, sourceCode, 255);
        Add(command, "@motivo", DbType.String, reason, 120);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task RefreshGoldPersonAsync(DbConnection connection, DbTransaction tx, Guid uuid, CancellationToken ct)
    {
        var stats = await LoadGoldPersonStatsAsync(connection, tx, uuid, ct);
        if (stats is null)
        {
            await using var delete = Command(connection, tx, "DELETE FROM gold.pessoa WHERE pessoa_uuid=@uuid;");
            Add(delete, "@uuid", DbType.Guid, uuid);
            await delete.ExecuteNonQueryAsync(ct);
            return;
        }

        var cpf = await ScalarStringAsync(connection, tx, """
            SELECT identificador FROM identidade.identity_map
             WHERE pessoa_uuid=@uuid AND tipo='CPF' AND vigencia_fim IS NULL AND estado='ATIVO'
             ORDER BY vigencia_inicio DESC,identity_map_id DESC LIMIT 1;
            """, uuid, ct) ?? await PreferredObservationStringAsync(connection, tx, uuid, "CPF", "cpf", ct);
        var cpfMissingReason = await PreferredObservationStringAsync(connection, tx, uuid, "CPF", "cpf_ausente_motivo", ct);
        var name = await PreferredObservationStringAsync(connection, tx, uuid, "NOME_COMPLETO", "nome_completo", ct)
            ?? throw new InvalidOperationException("Pessoa resolvida sem nome para materialização Gold.");
        var mother = await PreferredObservationStringAsync(connection, tx, uuid, "NOME_MAE", "nome_mae", ct)
            ?? throw new InvalidOperationException("Pessoa resolvida sem nome da mãe para materialização Gold.");
        var birth = await PreferredObservationDateAsync(connection, tx, uuid, "DATA_NASCIMENTO", ct)
            ?? throw new InvalidOperationException("Pessoa resolvida sem data de nascimento para materialização Gold.");

        var agreement = stats.Value.Divergent ? "DIVERGENTE" : stats.Value.DistinctManagers > 1 ? "CORROBORADO" : "BASELINE_FONTE_UNICA";
        var cpfStatus = cpf is not null ? "PRESENTE" : string.Equals(cpfMissingReason, "EM_REGULARIZACAO", StringComparison.Ordinal)
            ? "EM_REGULARIZACAO" : "SEM_CPF";

        await using var upsert = Command(connection, tx, """
            INSERT INTO gold.pessoa(
                pessoa_uuid,cpf,status_cpf,nome_completo,data_nascimento,nome_mae,fontes_distintas,estado_concordancia,atualizado_em)
            VALUES(@uuid,@cpf,@status_cpf,@nome,@nascimento,@mae,@fontes,@concordancia,CURRENT_TIMESTAMP)
            ON CONFLICT(pessoa_uuid) DO UPDATE SET
                cpf=EXCLUDED.cpf,status_cpf=EXCLUDED.status_cpf,nome_completo=EXCLUDED.nome_completo,
                data_nascimento=EXCLUDED.data_nascimento,nome_mae=EXCLUDED.nome_mae,
                fontes_distintas=EXCLUDED.fontes_distintas,estado_concordancia=EXCLUDED.estado_concordancia,
                atualizado_em=CURRENT_TIMESTAMP;
            """);
        Add(upsert, "@uuid", DbType.Guid, uuid);
        Add(upsert, "@cpf", DbType.AnsiStringFixedLength, cpf, 11);
        Add(upsert, "@status_cpf", DbType.String, cpfStatus, 30);
        Add(upsert, "@nome", DbType.String, name, 500);
        AddDate(upsert, "@nascimento", birth);
        Add(upsert, "@mae", DbType.String, mother, 500);
        Add(upsert, "@fontes", DbType.Int32, stats.Value.DistinctManagers);
        Add(upsert, "@concordancia", DbType.String, agreement, 40);
        await upsert.ExecuteNonQueryAsync(ct);
    }

    private static async Task<(int DistinctManagers, bool Divergent)?> LoadGoldPersonStatsAsync(
        DbConnection connection, DbTransaction tx, Guid uuid, CancellationToken ct)
    {
        await using var command = Command(connection, tx, """
            SELECT COUNT(DISTINCT po.gestor_id)::int,
                   COUNT(DISTINCT (po.nome_cmp || '|' || po.data_nascimento::text || '|' || po.nome_mae_cmp)) > 1
              FROM silver.pessoa_observacao po
              JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=po.pessoa_observacao_id
             WHERE vc.pessoa_uuid=@uuid AND vc.status='RESOLVIDO';
            """);
        Add(command, "@uuid", DbType.Guid, uuid);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct) || reader.GetInt32(0) == 0) return null;
        return (reader.GetInt32(0), reader.GetBoolean(1));
    }

    private static async Task<string?> PreferredObservationStringAsync(
        DbConnection connection, DbTransaction tx, Guid uuid, string fieldCode, string column, CancellationToken ct)
    {
        var allowed = column is "cpf" or "cpf_ausente_motivo" or "nome_completo" or "nome_mae"
            ? column : throw new ArgumentOutOfRangeException(nameof(column));
        await using var command = Command(connection, tx, $"""
            SELECT po.{allowed}
              FROM silver.pessoa_observacao po
              JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=po.pessoa_observacao_id
              LEFT JOIN silver.pessoa_campo_verificacao_observacao v
                ON v.pessoa_observacao_id=po.pessoa_observacao_id AND v.campo_codigo=@campo
             WHERE vc.pessoa_uuid=@uuid AND vc.status='RESOLVIDO' AND po.{allowed} IS NOT NULL
             ORDER BY (v.pessoa_campo_verificacao_id IS NULL),v.verificado_em DESC NULLS LAST,
                      po.source_as_of DESC,po.pessoa_observacao_id DESC LIMIT 1;
            """);
        Add(command, "@campo", DbType.String, fieldCode, 40);
        Add(command, "@uuid", DbType.Guid, uuid);
        var value = await command.ExecuteScalarAsync(ct);
        return value is null or DBNull ? null : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<DateOnly?> PreferredObservationDateAsync(
        DbConnection connection, DbTransaction tx, Guid uuid, string fieldCode, CancellationToken ct)
    {
        await using var command = Command(connection, tx, """
            SELECT po.data_nascimento
              FROM silver.pessoa_observacao po
              JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=po.pessoa_observacao_id
              LEFT JOIN silver.pessoa_campo_verificacao_observacao v
                ON v.pessoa_observacao_id=po.pessoa_observacao_id AND v.campo_codigo=@campo
             WHERE vc.pessoa_uuid=@uuid AND vc.status='RESOLVIDO'
             ORDER BY (v.pessoa_campo_verificacao_id IS NULL),v.verificado_em DESC NULLS LAST,
                      po.source_as_of DESC,po.pessoa_observacao_id DESC LIMIT 1;
            """);
        Add(command, "@campo", DbType.String, fieldCode, 40);
        Add(command, "@uuid", DbType.Guid, uuid);
        var raw = await command.ExecuteScalarAsync(ct);
        return raw switch { DateOnly d => d, DateTime dt => DateOnly.FromDateTime(dt), _ => null };
    }

    private static async Task<string?> ScalarStringAsync(
        DbConnection connection, DbTransaction tx, string sql, Guid uuid, CancellationToken ct)
    {
        await using var command = Command(connection, tx, sql);
        Add(command, "@uuid", DbType.Guid, uuid);
        var raw = await command.ExecuteScalarAsync(ct);
        return raw is null or DBNull ? null : Convert.ToString(raw, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task PromoteAttributeAsync(
        DbConnection connection, DbTransaction tx, long gestorId, Guid uuid, PgPersistedAttribute persisted, CancellationToken ct)
    {
        var value = persisted.Value;
        if (!value.VerificadoEm.HasValue)
            throw new InvalidDataException($"Atributo COMPROVADO sem verificadoEm: {value.AtributoCodigo}.");
        var precedence = value.ReferenciaEvidencia ?? value.VerificadoEm.Value;

        long? currentId = null;
        string? currentValue = null;
        DateTimeOffset? currentPrecedence = null;
        DateTimeOffset? currentVerified = null;
        await using (var current = Command(connection, tx, """
            SELECT pessoa_atributo_id,valor,precedencia_em,verificado_em
              FROM gold.pessoa_atributo
             WHERE pessoa_uuid=@uuid AND atributo_codigo=@codigo AND atributo_instancia_chave=@instancia
               AND vigencia_fim IS NULL FOR UPDATE;
            """))
        {
            Add(current, "@uuid", DbType.Guid, uuid);
            Add(current, "@codigo", DbType.String, value.AtributoCodigo, 80);
            Add(current, "@instancia", DbType.String, persisted.InstanceKey, 512);
            await using var reader = await current.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                currentId = reader.GetInt64(0);
                currentValue = reader.GetString(1);
                currentPrecedence = ReadDateTimeOffset(reader, 2);
                currentVerified = ReadDateTimeOffset(reader, 3);
            }
        }

        if (currentId.HasValue)
        {
            if (precedence < currentPrecedence!.Value || (precedence == currentPrecedence.Value && value.VerificadoEm.Value <= currentVerified!.Value))
            {
                if (string.Equals(persisted.Cardinality, "SINGLE", StringComparison.OrdinalIgnoreCase)
                    && precedence == currentPrecedence.Value && !string.Equals(currentValue, value.Valor, StringComparison.Ordinal))
                {
                    await using var conflict = Command(connection, tx,
                        "UPDATE gold.pessoa SET estado_concordancia='CONFLITO_EVIDENCIA',atualizado_em=CURRENT_TIMESTAMP WHERE pessoa_uuid=@uuid;");
                    Add(conflict, "@uuid", DbType.Guid, uuid);
                    await conflict.ExecuteNonQueryAsync(ct);
                }
                return;
            }

            await using var close = Command(connection, tx,
                "UPDATE gold.pessoa_atributo SET vigencia_fim=@fim,atualizado_em=CURRENT_TIMESTAMP WHERE pessoa_atributo_id=@id;");
            Add(close, "@fim", DbType.DateTimeOffset, precedence);
            Add(close, "@id", DbType.Int64, currentId.Value);
            await close.ExecuteNonQueryAsync(ct);
        }

        await using var insert = Command(connection, tx, """
            INSERT INTO gold.pessoa_atributo(
                pessoa_uuid,atributo_codigo,atributo_instancia_chave,valor,fonte_gestor_id,pessoa_atributo_observacao_id,
                source_record_id,evidencia_tipo,referencia_evidencia,verificado_em,precedencia_em,vigencia_inicio,atualizado_em)
            VALUES(@uuid,@codigo,@instancia,@valor,@gestor,@obs,@source_record,@evidencia,
                   @referencia,@verificado,@precedencia,@precedencia,CURRENT_TIMESTAMP);
            """);
        Add(insert, "@uuid", DbType.Guid, uuid);
        Add(insert, "@codigo", DbType.String, value.AtributoCodigo, 80);
        Add(insert, "@instancia", DbType.String, persisted.InstanceKey, 512);
        Add(insert, "@valor", DbType.String, value.Valor, 2000);
        Add(insert, "@gestor", DbType.Int64, gestorId);
        Add(insert, "@obs", DbType.Int64, persisted.ObservationId);
        Add(insert, "@source_record", DbType.String, value.SourceRecordId, 255);
        Add(insert, "@evidencia", DbType.String, value.EvidenciaTipo, 80);
        Add(insert, "@referencia", DbType.DateTimeOffset, value.ReferenciaEvidencia);
        Add(insert, "@verificado", DbType.DateTimeOffset, value.VerificadoEm.Value);
        Add(insert, "@precedencia", DbType.DateTimeOffset, precedence);
        await insert.ExecuteNonQueryAsync(ct);
    }

    private static string ToAssignmentState(ResolutionStatus status) => status switch
    {
        ResolutionStatus.RESOLVIDO => "ATRIBUIDA",
        ResolutionStatus.CONFLITO => "CONFLITO_IDENTIDADE",
        _ => "PENDENTE_IDENTIDADE"
    };

    private static DbCommand Command(DbConnection connection, DbTransaction tx, string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = sql;
        return command;
    }

    private static DbParameter Add(DbCommand command, string name, DbType type, object? value, int? size = null)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        if (size.HasValue) parameter.Size = size.Value;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
        return parameter;
    }

    private static DbParameter AddDate(DbCommand command, string name, DateOnly? value) =>
        Add(command, name, DbType.Date, value.HasValue ? value.Value.ToDateTime(TimeOnly.MinValue) : null);

    private static DateTimeOffset ReadDateTimeOffset(DbDataReader reader, int ordinal)
    {
        var raw = reader.GetValue(ordinal);
        return raw switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
            _ => throw new InvalidCastException($"Valor temporal PostgreSQL inesperado: {raw.GetType().FullName}.")
        };
    }

    private sealed record PgLatestPerson(long ObservationId, int VersaoInterna, string ConteudoHash);
    private sealed record PgProcessedPerson(
        long ObservationId, long PessoaOrigemId, long SistemaOrigemId, string CodigoPessoaOrigem,
        string? CpfDeclarado, string? CpfAusenteMotivo, Guid? PessoaUuid, string EstadoAtribuicaoIdentidade,
        long? ReferenciaTerritorialObservacaoId, string? NaturezaReferenciaTerritorial, long? SubprefeituraId, long? DistritoId);
    private sealed record PgTerritorialSelection(
        long? ReferenciaTerritorialObservacaoId, string? NaturezaReferenciaTerritorial, long? SubprefeituraId, long? DistritoId);
    private sealed record PgPersistedAttribute(long ObservationId, ParsedTransversalAttribute Value, string InstanceKey, string Cardinality);
    private sealed record PgAttributeRule(string Cardinality, string InstanceKeyRule);
}