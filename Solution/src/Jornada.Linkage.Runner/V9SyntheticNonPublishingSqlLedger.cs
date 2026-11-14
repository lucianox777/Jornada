using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Test-only, disposable-SQL synthetic persistence ledger. This is NOT the
/// real identidade.linkage_run / linkage_resultado contract and grants no
/// production model activation or TEST scientific approval.
/// </summary>
public sealed record V9SyntheticNonPublishingLedgerReceipt(
    Guid RehearsalId,
    Guid ModelId,
    string WinnerSha256,
    LinkageRunStatus TerminalStatusInSyntheticLedger,
    int Eligible,
    int Evaluated,
    int BatchCount,
    int DecisionRows,
    string CandidateGraphSha256,
    string DecisionsSha256,
    string IdentityGroupsSha256,
    bool SyntheticLedgerPersisted = true,
    bool ProductionLinkageRunPersisted = false,
    bool Published = false,
    bool ProductionBatchPipelineCertified = false);

/// <summary>
/// Writes only synthetic accounting and SHA receipts in a ONE-TRANSACTION
/// disposable SQL ledger. Rollback makes any partial batch invisible. Does
/// not write personal fields, CPF, Gold, Serving or production linkage tables.
/// </summary>
public static class V9SyntheticNonPublishingSqlLedger
{
    public static async Task<V9SyntheticNonPublishingLedgerReceipt> PersistAsync(
        SqlConnection connection,
        Guid rehearsalId,
        V9NonPublishingBatchExecutionReceipt proof,
        IReadOnlyCollection<V9PhysicalCalibrationObservation> syntheticTest,
        IReadOnlyList<(int A, int B)> physicalSqlEdges,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(proof);
        ArgumentNullException.ThrowIfNull(syntheticTest);
        ArgumentNullException.ThrowIfNull(physicalSqlEdges);
        if (rehearsalId == Guid.Empty)
            throw new InvalidDataException("Ensaio SQL exige ID único não vazio.");
        if (connection.State != ConnectionState.Open)
            throw new InvalidOperationException("Ensaio SQL exige conexão já aberta.");
        cancellationToken.ThrowIfCancellationRequested();

        // The guard MUST precede a model-table read and any transaction/write.
        await using (var guard = new SqlCommand("SELECT DB_NAME();", connection))
        {
            var name = (string?)await guard.ExecuteScalarAsync(cancellationToken);
            if (!string.Equals(name, "JornadaHarness", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Ledger sintético V9 só pode existir em banco descartável JornadaHarness.");
        }

        var rows = syntheticTest.OrderBy(x => x.ObservationId).ToArray();
        if (rows.Length is < 2 or > V9OperationalPinnedCohortRuntime.MaximumAuditObservations
            || rows.Any(row => row is null || row.Partition != "TEST"
                || row.ObservationId <= 0 || string.IsNullOrWhiteSpace(row.Gestor))
            || rows.Select(row => row.ObservationId).Distinct().Count() != rows.Length)
            throw new InvalidDataException("Ledger V9 exige coorte TEST sintética única de 2..128.");
        static bool Sha(string? candidate) => candidate is { Length: 64 } &&
            candidate.All(Uri.IsHexDigit);
        if (proof.PinnedModelId == Guid.Empty
            || !Sha(proof.WinnerSha256)
            || !Sha(proof.CandidateGraphSha256)
            || !Sha(proof.DecisionsSha256)
            || !Sha(proof.IdentityGroupsSha256)
            || proof.PersistedRun || proof.Published || proof.ProductionBatchPipelineCertified
            || proof.ExpectedTerminalStatus != LinkageRunStatus.CONCLUIDO_SEM_PUBLICACAO
            || proof.Eligible != rows.Length || proof.Evaluated != rows.Length
            || proof.Resolved < 0 || proof.Unresolved < 0 || proof.Conflicts < 0
            || proof.NoCandidateInBlock < 0 || proof.NoCandidateInBlock > proof.Unresolved
            || proof.Resolved + proof.Unresolved + proof.Conflicts != rows.Length
            || proof.BatchSize is < 1 or > V9OperationalPinnedCohortRuntime.MaximumAuditObservations
            || proof.BatchCount != (rows.Length + proof.BatchSize - 1) / proof.BatchSize)
            throw new InvalidDataException("Prova V9 não reconciliada, parcial ou certificada indevidamente.");

        // Verify the pinned winner against the immutable TEST_ONLY operational
        // model row again; never use active model or fitted fallback.
        var pinned = await V9OperationalPinnedCohortRuntime.LoadPinnedAsync(
            connection, proof.PinnedModelId, proof.WinnerSha256, cancellationToken);
        if (pinned.WinnerSha256 != proof.WinnerSha256)
            throw new InvalidDataException("Vencedor do ledger não corresponde ao SHA SQL pinado.");

        // Recompute the SAME scientific decisions/groups from the original
        // frozen TEST SQL graph. This prevents accepting forged count/hash-only
        // receipts or silently persisting a different winner's observations.
        var replay = pinned.ReplayTest(rows, physicalSqlEdges);
        static string Digest(string text) => Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        var edgesDigest = Digest(string.Concat(physicalSqlEdges
            .OrderBy(x => x.A).ThenBy(x => x.B)
            .Select(x => FormattableString.Invariant($"{x.A}:{x.B}\n"))));
        var decisionDigest = Digest(JsonSerializer.Serialize(replay.Decisions));
        var groupDigest = Digest(string.Join("\n", replay.IdentityByObservation
            .OrderBy(x => x.Key)
            .Select(x => FormattableString.Invariant($"{x.Key}:{x.Value:D}"))));
        var resolved = replay.Decisions.Count(x => x.Decision.Status == ResolutionStatus.RESOLVIDO);
        var conflicts = replay.Decisions.Count(x => x.Decision.Status == ResolutionStatus.CONFLITO);
        var unresolved = replay.Decisions.Count - resolved - conflicts;
        var noCandidate = replay.Decisions.Count(x =>
            x.Decision.Status == ResolutionStatus.NAO_RESOLVIDO
            && ProbabilisticLinkageBatchRunner.IsNoCandidateReason(x.Decision.Motivo));
        if (replay.Decisions.Count != rows.Length
            || replay.IdentityByObservation.Count != rows.Length
            || edgesDigest != proof.CandidateGraphSha256
            || decisionDigest != proof.DecisionsSha256
            || groupDigest != proof.IdentityGroupsSha256
            || resolved != proof.Resolved || unresolved != proof.Unresolved
            || conflicts != proof.Conflicts || noCandidate != proof.NoCandidateInBlock)
            throw new InvalidDataException(
                "Decisões/agrupamentos congelados divergem do recibo SQL: persistência recusada.");
        var decisionsById = replay.Decisions.ToDictionary(x => x.ObservationId);
        if (decisionsById.Count != rows.Length)
            throw new InvalidDataException("Decisões TEST não cobrem cada observação exatamente uma vez.");

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            await using (var create = new SqlCommand(
                """
                INSERT identidade.v9_synthetic_rehearsal_run
                    (rehearsal_run_id,modelo_id,winner_sha256,
                     candidate_graph_sha256,decisions_sha256,identity_groups_sha256,
                     status,elegiveis,avaliados,resolvidos,nao_resolvidos,conflitos,
                     sem_candidato,batch_size,expected_batches,completed_batches)
                VALUES (@id,@model,@winner,@edges,@decisions,@groups,
                    N'PREPARANDO',@eligible,0,@resolved,@unresolved,@conflicts,
                    @no_candidate,@batch_size,@batch_count,0);
                UPDATE identidade.v9_synthetic_rehearsal_run
                   SET status=N'EXECUTANDO'
                 WHERE rehearsal_run_id=@id AND status=N'PREPARANDO';
                IF @@ROWCOUNT <> 1 THROW 52541, 'Ledger V9 não entrou em EXECUTANDO.', 1;
                """, connection, transaction))
            {
                Add(create, "@id", SqlDbType.UniqueIdentifier, rehearsalId);
                Add(create, "@model", SqlDbType.UniqueIdentifier, proof.PinnedModelId);
                Add(create, "@winner", SqlDbType.Char, proof.WinnerSha256, 64);
                Add(create, "@edges", SqlDbType.Char, proof.CandidateGraphSha256, 64);
                Add(create, "@decisions", SqlDbType.Char, proof.DecisionsSha256, 64);
                Add(create, "@groups", SqlDbType.Char, proof.IdentityGroupsSha256, 64);
                Add(create, "@eligible", SqlDbType.Int, proof.Eligible);
                Add(create, "@resolved", SqlDbType.Int, proof.Resolved);
                Add(create, "@unresolved", SqlDbType.Int, proof.Unresolved);
                Add(create, "@conflicts", SqlDbType.Int, proof.Conflicts);
                Add(create, "@no_candidate", SqlDbType.Int, proof.NoCandidateInBlock);
                Add(create, "@batch_size", SqlDbType.Int, proof.BatchSize);
                Add(create, "@batch_count", SqlDbType.Int, proof.BatchCount);
                await create.ExecuteNonQueryAsync(cancellationToken);
            }

            var batchNumber = 0;
            for (var offset = 0; offset < rows.Length; offset += proof.BatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batch = rows.Skip(offset).Take(proof.BatchSize).ToArray();
                batchNumber++;
                var canonicalIds = string.Join("\n", batch.Select(r =>
                    r.ObservationId.ToString(CultureInfo.InvariantCulture)));
                var idsSha = Convert.ToHexString(SHA256.HashData(
                    Encoding.UTF8.GetBytes(canonicalIds))).ToLowerInvariant();
                await using var store = new SqlCommand(
                    """
                    INSERT identidade.v9_synthetic_rehearsal_batch
                        (rehearsal_run_id,batch_number,observation_count,
                         first_observation_id,last_observation_id,observation_ids_sha256)
                    VALUES (@id,@batch,@count,@first,@last,@ids_sha);
                    UPDATE identidade.v9_synthetic_rehearsal_run
                       SET avaliados=avaliados+@count,
                           completed_batches=completed_batches+1
                     WHERE rehearsal_run_id=@id AND status=N'EXECUTANDO'
                       AND completed_batches=@previous_batch
                       AND avaliados=@previous_count;
                    IF @@ROWCOUNT <> 1 THROW 52542, 'Contadores V9 não reconciliados por lote.', 1;
                    """, connection, transaction);
                Add(store, "@id", SqlDbType.UniqueIdentifier, rehearsalId);
                Add(store, "@batch", SqlDbType.Int, batchNumber);
                Add(store, "@count", SqlDbType.Int, batch.Length);
                Add(store, "@first", SqlDbType.Int, batch[0].ObservationId);
                Add(store, "@last", SqlDbType.Int, batch[^1].ObservationId);
                Add(store, "@ids_sha", SqlDbType.Char, idsSha, 64);
                Add(store, "@previous_batch", SqlDbType.Int, batchNumber - 1);
                Add(store, "@previous_count", SqlDbType.Int, offset);
                await store.ExecuteNonQueryAsync(cancellationToken);

                // Evidence is inserted under the SAME transaction as the run
                // and batch receipt. No decision is silently skipped, duplicated
                // or summarized without its original frozen group identity.
                foreach (var row in batch)
                {
                    var decision = decisionsById[row.ObservationId];
                    var d = decision.Decision;
                    var groupId = replay.IdentityByObservation[row.ObservationId];
                    var evidenceSha = Digest(JsonSerializer.Serialize(decision) + "\n" +
                        groupId.ToString("D"));
                    await using var insertDecision = new SqlCommand(
                        """
                        INSERT identidade.v9_synthetic_rehearsal_decision
                           (rehearsal_run_id,observation_id,batch_number,identity_uuid,
                            status,motivo,pessoa_uuid_resolvido,melhor_candidato_uuid,
                            score_melhor,segundo_candidato_uuid,score_segundo,margem,
                            evidence_sha256)
                        VALUES (@run_id,@observation_id,@batch_number,@identity_uuid,
                            @status,@motivo,@resolved_uuid,@best_uuid,@best_score,
                            @second_uuid,@second_score,@margin,@evidence_sha);
                        """, connection, transaction);
                    Add(insertDecision, "@run_id", SqlDbType.UniqueIdentifier, rehearsalId);
                    Add(insertDecision, "@observation_id", SqlDbType.Int, row.ObservationId);
                    Add(insertDecision, "@batch_number", SqlDbType.Int, batchNumber);
                    Add(insertDecision, "@identity_uuid", SqlDbType.UniqueIdentifier, groupId);
                    Add(insertDecision, "@status", SqlDbType.NVarChar, d.Status.ToString(), 30);
                    Add(insertDecision, "@motivo", SqlDbType.NVarChar,
                        (object?)d.Motivo ?? DBNull.Value, 120);
                    Add(insertDecision, "@resolved_uuid", SqlDbType.UniqueIdentifier,
                        (object?)d.PessoaUuidResolvido ?? DBNull.Value);
                    Add(insertDecision, "@best_uuid", SqlDbType.UniqueIdentifier,
                        (object?)d.MelhorCandidatoUuid ?? DBNull.Value);
                    Add(insertDecision, "@best_score", SqlDbType.Decimal, d.MelhorScore);
                    Add(insertDecision, "@second_uuid", SqlDbType.UniqueIdentifier,
                        (object?)d.SegundoCandidatoUuid ?? DBNull.Value);
                    Add(insertDecision, "@second_score", SqlDbType.Decimal,
                        (object?)d.SegundoScore ?? DBNull.Value);
                    Add(insertDecision, "@margin", SqlDbType.Decimal,
                        (object?)d.Margem ?? DBNull.Value);
                    Add(insertDecision, "@evidence_sha", SqlDbType.Char, evidenceSha, 64);
                    if (await insertDecision.ExecuteNonQueryAsync(cancellationToken) != 1)
                        throw new InvalidDataException("Evidência de decisão SQL não foi inserida.");
                }
            }

            await using (var complete = new SqlCommand(
                """
                UPDATE r SET status=N'CONCLUIDO_SEM_PUBLICACAO',
                    encerrado_em=SYSUTCDATETIME()
                FROM identidade.v9_synthetic_rehearsal_run r
                WHERE r.rehearsal_run_id=@id AND r.status=N'EXECUTANDO'
                  AND r.avaliados=r.elegiveis
                  AND r.completed_batches=r.expected_batches
                  AND (SELECT COUNT_BIG(*) FROM identidade.v9_synthetic_rehearsal_batch b
                       WHERE b.rehearsal_run_id=r.rehearsal_run_id)=r.expected_batches
                  AND (SELECT SUM(b.observation_count) FROM identidade.v9_synthetic_rehearsal_batch b
                       WHERE b.rehearsal_run_id=r.rehearsal_run_id)=r.elegiveis
                  AND (SELECT COUNT_BIG(*) FROM identidade.v9_synthetic_rehearsal_decision d
                       WHERE d.rehearsal_run_id=r.rehearsal_run_id)=r.elegiveis
                  AND (SELECT COUNT_BIG(*) FROM identidade.v9_synthetic_rehearsal_decision d
                       WHERE d.rehearsal_run_id=r.rehearsal_run_id AND d.status=N'RESOLVIDO')=r.resolvidos
                  AND (SELECT COUNT_BIG(*) FROM identidade.v9_synthetic_rehearsal_decision d
                       WHERE d.rehearsal_run_id=r.rehearsal_run_id AND d.status=N'NAO_RESOLVIDO')=r.nao_resolvidos
                  AND (SELECT COUNT_BIG(*) FROM identidade.v9_synthetic_rehearsal_decision d
                       WHERE d.rehearsal_run_id=r.rehearsal_run_id AND d.status=N'CONFLITO')=r.conflitos;
                IF @@ROWCOUNT <> 1 THROW 52543, 'Fechamento V9 incompleto: rollback obrigatório.', 1;
                """, connection, transaction))
            {
                Add(complete, "@id", SqlDbType.UniqueIdentifier, rehearsalId);
                await complete.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }

        return new(rehearsalId, proof.PinnedModelId, proof.WinnerSha256,
            LinkageRunStatus.CONCLUIDO_SEM_PUBLICACAO, rows.Length, rows.Length,
            proof.BatchCount, rows.Length, proof.CandidateGraphSha256, proof.DecisionsSha256,
            proof.IdentityGroupsSha256);
    }

    private static void Add(SqlCommand command, string name, SqlDbType type, object value, int size = 0)
    {
        var parameter = command.Parameters.Add(name, type);
        if (size > 0) parameter.Size = size;
        if (type == SqlDbType.Decimal)
        {
            parameter.Precision = 9;
            parameter.Scale = 8;
        }
        parameter.Value = value;
    }
}
