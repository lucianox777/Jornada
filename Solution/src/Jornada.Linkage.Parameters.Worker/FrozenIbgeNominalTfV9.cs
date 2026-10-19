using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jornada.Contracts;
using Jornada.Linkage.Runner;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Immutable IBGE first-name marginal snapshot bound to an explicitly pinned,
/// already published SQL reference version. This is a V9 PREVIEW bridge only:
/// it does not estimate U, a TF weight, a joint surname frequency or an
/// operational model. Full-name/first-last-joint/last-surname are unsupported
/// by this FIRST-NAME-only materialization.
/// </summary>
public sealed class FrozenIbgeNominalTfV9
{
    private readonly IReadOnlyDictionary<string, decimal> personFirst;
    private readonly IReadOnlyDictionary<string, decimal> motherFirst;
    public long ReferenceVersionId { get; }
    public string ReferenceCode { get; }
    public string PublishedReferenceSha256 { get; }
    public string MaterializedFrequencySha256 { get; }
    public string BlockingFrameFingerprint { get; }
    public NominalSyntheticIbgeBootstrapV9 Bootstrap { get; }

    // Exports the exact pinned values, not a fresh SQL lookup or an ATIVA
    // reference. The portable artifact digest binds these values with TRAIN U.
    internal (NominalFrequencyReferenceContractV9 Reference,
        IReadOnlyDictionary<string, decimal> Frequencies) ExportForSnapshot(
        CompositeNominalTfSideV9 side)
    {
        if (!Enum.IsDefined(side))
            throw new InvalidDataException("Lado TF do snapshot desconhecido.");
        var values = side == CompositeNominalTfSideV9.Person ? personFirst : motherFirst;
        return (MakeReference(side, ReferenceCode, PublishedReferenceSha256,
                BlockingFrameFingerprint, Bootstrap),
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, decimal>(
                values.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal)));
    }

    private FrozenIbgeNominalTfV9(
        PreparedNominalTermFrequency prepared,
        string referenceCode,
        string publishedReferenceSha256,
        string blockingFrameFingerprint,
        NominalSyntheticIbgeBootstrapV9 bootstrap)
    {
        ReferenceVersionId = prepared.ReferenceVersionId;
        ReferenceCode = referenceCode;
        PublishedReferenceSha256 = publishedReferenceSha256;
        BlockingFrameFingerprint = blockingFrameFingerprint;
        Bootstrap = bootstrap;
        personFirst = prepared.Snapshot.PersonFirstNames.ToDictionary(
            x => x.ValueNormalized, x => x.Frequency, StringComparer.Ordinal);
        motherFirst = prepared.Snapshot.MotherFirstNames.ToDictionary(
            x => x.ValueNormalized, x => x.Frequency, StringComparer.Ordinal);
        MaterializedFrequencySha256 = ComputeMaterializedDigest(prepared.Entries);
    }

    /// <summary>
    /// This is the required SQL-backed entry point. The supplied reference ID
    /// is fixed by the caller, NEVER looked up by current ATIVA and never
    /// silently replaced by another version. OBSOLETA is permitted only for
    /// reproducible historical read-only replay.
    /// </summary>
    public static async Task<FrozenIbgeNominalTfV9> LoadAsync(
        SqlConnection connection,
        long referenceVersionId,
        string blockingFrameFingerprint,
        NominalSyntheticIbgeBootstrapV9 bootstrap,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(referenceVersionId);

        string code;
        string hash;
        await using (var command = new SqlCommand(
            """
            SELECT codigo,status,conteudo_sha256
            FROM ref.frequencia_nome_versao
            WHERE frequencia_nome_versao_id=@ref;
            """, connection))
        {
            command.Parameters.Add("@ref", SqlDbType.BigInt).Value = referenceVersionId;
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                throw new InvalidOperationException("Versão IBGE fixada inexistente.");
            code = reader.GetString(0);
            var status = reader.GetString(1);
            if (status is not ("ATIVA" or "OBSOLETA") || reader.IsDBNull(2))
                throw new InvalidOperationException(
                    "TF V9 exige versão IBGE publicada/imutável; CARREGANDO e VALIDADA são rejeitadas.");
            var bytes = (byte[])reader.GetValue(2);
            if (bytes.Length != 32)
                throw new InvalidOperationException("Digest SHA-256 publicado da referência IBGE inválido.");
            hash = Convert.ToHexString(bytes);
            if (await reader.ReadAsync(ct))
                throw new InvalidOperationException("Identidade de versão IBGE ambígua.");
        }

        // The SQL store already constrains the two different reference
        // geographies: person=SP municipality 3550308/TODOS, mother=BRASIL/FEMININO.
        var prepared = await NominalTermFrequencyReferenceStore.PrepareAsync(
            connection, referenceVersionId, ct);
        return BindPublished(prepared, code, hash, blockingFrameFingerprint, bootstrap);
    }

    /// <summary>
    /// Pure binder for already verified SQL input, also exercised in unit tests.
    /// Only LoadAsync authenticates publication metadata against ref SQL.
    /// Caller-supplied mock PreparedNominalTermFrequency is NOT certification.
    /// </summary>
    public static FrozenIbgeNominalTfV9 BindPublished(
        PreparedNominalTermFrequency prepared,
        string referenceCode,
        string publishedReferenceSha256,
        string blockingFrameFingerprint,
        NominalSyntheticIbgeBootstrapV9 bootstrap)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(bootstrap);
        if (prepared.ReferenceVersionId <= 0
            || string.IsNullOrWhiteSpace(referenceCode)
            || string.IsNullOrWhiteSpace(blockingFrameFingerprint)
            || !IsSha256(publishedReferenceSha256))
            throw new InvalidOperationException("TF V9 exige identidade/hashes de referência publicados e frame de blocking.");

        var reference = MakeReference(
            CompositeNominalTfSideV9.Person, referenceCode,
            publishedReferenceSha256, blockingFrameFingerprint, bootstrap);
        CompositeNominalTermFrequencyContractV9.RequireMatchingReference(
            CompositeNominalLevelV9.ExactFirst, reference, 1);

        // Reconstruct the exact snapshot from frozen entries and verify all
        // published frequencies were not independently altered in the DTO.
        var rebuilt = NominalTermFrequencySnapshot.Create(prepared.Entries);
        if (!Same(rebuilt.PersonFirstNames, prepared.Snapshot.PersonFirstNames)
            || !Same(rebuilt.MotherFirstNames, prepared.Snapshot.MotherFirstNames)
            || rebuilt.PersonLastSurnameCount != prepared.Snapshot.PersonLastSurnameCount
            || rebuilt.MotherLastSurnameCount != prepared.Snapshot.MotherLastSurnameCount)
            throw new InvalidOperationException("Entradas TF divergentes do snapshot materializado.");
        if (rebuilt.PersonFirstNameCount == 0 || rebuilt.MotherFirstNameCount == 0)
            throw new InvalidOperationException("A referência TF exige marginal de prenomes de pessoa e mãe.");

        return new FrozenIbgeNominalTfV9(
            prepared, referenceCode, publishedReferenceSha256.ToUpperInvariant(),
            blockingFrameFingerprint, bootstrap);
    }

    /// <summary>
    /// Returns null ONLY for explicit disabled TF (weight=0). An unknown name,
    /// mismatched first token or unsupported level with positive weight FAILS.
    /// Neither U nor the TF weight is guessed from the reference snapshot.
    /// </summary>
    public CompositeNominalTfPreviewV9? CreatePreview(
        CompositeNominalTfSideV9 side,
        CompositeNominalLevelV9 level,
        string? leftName,
        string? rightName,
        decimal unitReferenceU,
        decimal weight,
        decimal minimumFrequency,
        long minimumIndependentSupport)
    {
        if (!Enum.IsDefined(side))
            throw new InvalidOperationException("Lado nominal TF V9 desconhecido.");
        if (weight < 0m || weight > 1m)
            throw new InvalidOperationException("Peso TF V9 fora de [0,1].");
        if (weight == 0m)
            return null; // neutral Bayes factor 1 by explicit model choice
        if (level != CompositeNominalLevelV9.ExactFirst)
            throw new InvalidOperationException(
                "Snapshot IBGE de prenomes não estima TF integral, primeiro+último ou último sobrenome.");
        if (unitReferenceU <= 0m || unitReferenceU > 1m
            || minimumFrequency < 0m || minimumFrequency > 1m
            || minimumIndependentSupport <= 0)
            throw new InvalidOperationException("Parâmetros U/floor/suporte TF V9 inválidos.");
        var value = FirstNameV9(leftName);
        if (value is null || !string.Equals(value, FirstNameV9(rightName), StringComparison.Ordinal))
            throw new InvalidOperationException("TF V9 exige igualdade da unidade de primeiro nome.");
        var domain = side == CompositeNominalTfSideV9.Person ? personFirst : motherFirst;
        if (!domain.TryGetValue(value, out var frequency))
            throw new InvalidOperationException(
                "Prenome ausente/suprimido no snapshot IBGE fixado; não inferir frequência zero.");

        var reference = MakeReference(side, ReferenceCode,
            PublishedReferenceSha256, BlockingFrameFingerprint, Bootstrap);
        CompositeNominalTermFrequencyContractV9.RequireMatchingReference(
            level, reference, minimumIndependentSupport);
        return new CompositeNominalTfPreviewV9(
            side, reference, value, frequency, unitReferenceU,
            weight, minimumFrequency, minimumIndependentSupport);
    }

    private static NominalFrequencyReferenceContractV9 MakeReference(
        CompositeNominalTfSideV9 side, string code, string sha,
        string blockingFrameFingerprint, NominalSyntheticIbgeBootstrapV9 bootstrap) =>
        new(
            NominalFrequencyUnitV9.FirstName,
            NominalFrequencySourceV9.IbgePublishedMarginal,
            $"IBGE_PUBLISHED:{code}:{sha}:{(side == CompositeNominalTfSideV9.Person ? "SP_3550308_TODOS" : "BR_FEMININO")}",
            blockingFrameFingerprint, 0, false, bootstrap);

    private static bool IsSha256(string? sha) =>
        sha is { Length: 64 } && sha.All(static c =>
            (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));

    private static bool Same(
        IReadOnlyList<NominalTermFrequencyValue> a,
        IReadOnlyList<NominalTermFrequencyValue> b) =>
        a.Count == b.Count && a.Zip(b).All(x =>
            x.First.ValueNormalized == x.Second.ValueNormalized
            && x.First.Frequency == x.Second.Frequency);

    private static string ComputeMaterializedDigest(
        IReadOnlyList<NominalTermFrequencyEntry> entries)
    {
        var rows = entries.OrderBy(x => x.Attribute, StringComparer.Ordinal)
            .ThenBy(x => x.ValueNormalized, StringComparer.Ordinal)
            .Select(x => new
            {
                x.Attribute, x.ValueNormalized, x.Occurrences,
                x.PopulationReference, x.Frequency
            }).ToArray();
        var canonical = JsonSerializer.Serialize(rows);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string? FirstNameV9(string? full)
    {
        var name = BrazilianNameComponents.Project(full);
        if (name is null)
            return null;
        var parts = name.NormalizedFull.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var index = name.TitlePrefix is null ? 0 : 1;
        if (index >= parts.Length)
            return null;
        var value = parts[index].Trim('.', ',');
        return value.Length == 0 ? null : value;
    }
}
