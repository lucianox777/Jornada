using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jornada.Linkage.Runner;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Local scientific lifecycle around the existing portable fit codec. No SQL publisher,
/// active-reference writer or DT-15 authority is exposed by this service.
/// A directory is one calibration lineage; independent TEST consumption is lineage-wide.
/// </summary>
public static class V9CalibrationFreezeService
{
    public const string EnvelopeSchemaVersion = "V9_CALIBRATION_ENVELOPE_V1";
    public const string BootstrapOrigin = "BOOTSTRAP_AUTOMATIC";
    public const string ManualOrigin = "MANUAL_SELECTION";
    public const string IndependentTestStatisticalMethod = "SOURCE_IDENTITY_ANY_ERROR_CP_RATIO_BONFERRONI_V1";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true
    };

    public static string FingerprintSelection(V9BootstrapPareto2D.Result selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return Sha(JsonSerializer.SerializeToUtf8Bytes(
            V9BootstrapPareto2D.Evaluate(selection.Table.Select(e => e.Option), selection.Policy), JsonOptions));
    }

    public static string FingerprintTestEvidence(V9IndependentTestEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(evidence.IdentityGroups);
        return Sha(JsonSerializer.SerializeToUtf8Bytes(evidence with
        {
            IdentityGroups = evidence.IdentityGroups.OrderBy(g => g.BaseIdentityFingerprint, StringComparer.Ordinal).ToArray()
        }, JsonOptions));
    }

    /// <summary>Can be reserved from the unlabeled cohort before opening TEST truth.</summary>
    public static string FingerprintTestIdentityGroups(IEnumerable<string> identityFingerprints)
    {
        ArgumentNullException.ThrowIfNull(identityFingerprints);
        var values = identityFingerprints.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        foreach (var value in values) RequireSha(value);
        if (values.Length < 2 || values.Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new InvalidDataException("Coorte TEST exige identidades únicas e pré-declaradas.");
        return Sha(JsonSerializer.SerializeToUtf8Bytes(values, JsonOptions));
    }

    /// <returns>No snapshot when VALIDATION has no eligible option.</returns>
    public static async Task<V9FrozenCalibration?> FreezeAsync(string directory,
        V9FittedSnapshot snapshot, V9CalibrationFreezeMetadata metadata,
        V9BootstrapPareto2D.Result selection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(selection);
        var actual = V9BootstrapPareto2D.Evaluate(selection.Table.Select(e => e.Option), selection.Policy);
        if (actual.InitialSuggestedOptionId is null) return null;
        metadata = NormalizeAndValidate(snapshot, metadata, actual);
        var envelope = new V9CalibrationSnapshotEnvelope(EnvelopeSchemaVersion, snapshot.ContentSha256, metadata, actual);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
        var frozen = new V9FrozenCalibration(envelope, Sha(bytes), snapshot);
        directory = PrepareDirectory(directory);
        await using var lease = await AcquireAsync(directory, cancellationToken);
        var path = Path.Combine(directory, metadata.Version + ".envelope.json");
        if (File.Exists(path))
        {
            if (!(await File.ReadAllBytesAsync(path, cancellationToken)).AsSpan().SequenceEqual(bytes))
                throw new InvalidDataException("Versão congelada divergente: criar nova versão, nunca sobrescrever.");
            await V9FittedSnapshotArtifact.ReadPinnedAsync(FitPath(directory, metadata.Version),
                snapshot.ContentSha256, cancellationToken);
            return frozen;
        }
        var previous = await ReadEnvelopesAsync(directory, cancellationToken);
        if (previous.Count == 0)
        {
            if (metadata.PredecessorEnvelopeSha256 is not null || metadata.Origin != BootstrapOrigin)
                throw new InvalidDataException("Primeiro congelamento exige bootstrap elegível, sem predecessor.");
        }
        else
        {
            var leaves = previous.Where(p => !previous.Any(c =>
                c.Envelope.Metadata.PredecessorEnvelopeSha256 == p.EnvelopeSha256)).ToArray();
            if (leaves.Length != 1 || metadata.Origin != ManualOrigin
                || metadata.PredecessorEnvelopeSha256 != leaves[0].EnvelopeSha256
                || metadata.CreatedUtc < leaves[0].Envelope.Metadata.CreatedUtc)
                throw new InvalidDataException("Calibração posterior exige seleção manual e predecessor atual fixado.");
        }
        var fitPath = FitPath(directory, metadata.Version);
        if (File.Exists(fitPath))
            await V9FittedSnapshotArtifact.ReadPinnedAsync(fitPath, snapshot.ContentSha256, cancellationToken);
        else
        {
            var temporary = fitPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await V9FittedSnapshotArtifact.WriteNewAsync(temporary, snapshot, cancellationToken);
                File.Move(temporary, fitPath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        await WriteNewAtomicAsync(path, bytes, cancellationToken);
        return frozen;
    }

    public static async Task<V9FrozenCalibration> ReadPinnedAsync(string directory,
        string version, string envelopeSha256, CancellationToken cancellationToken = default)
    {
        Identifier(version);
        RequireSha(envelopeSha256);
        var bytes = await File.ReadAllBytesAsync(Path.Combine(Path.GetFullPath(directory), version + ".envelope.json"), cancellationToken);
        if (Sha(bytes) != envelopeSha256) throw new InvalidDataException("Envelope diverge do SHA fixado.");
        var envelope = JsonSerializer.Deserialize<V9CalibrationSnapshotEnvelope>(bytes, JsonOptions)
            ?? throw new InvalidDataException("Envelope vazio.");
        if (envelope.SchemaVersion != EnvelopeSchemaVersion || envelope.Metadata.Version != version
            || !JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions).AsSpan().SequenceEqual(bytes))
            throw new InvalidDataException("Envelope incompatível ou não canônico.");
        var snapshot = await V9FittedSnapshotArtifact.ReadPinnedAsync(FitPath(directory, version),
            envelope.FittedSnapshotSha256, cancellationToken);
        var selection = V9BootstrapPareto2D.Evaluate(envelope.Selection.Table.Select(e => e.Option), envelope.Selection.Policy);
        var metadata = NormalizeAndValidate(snapshot, envelope.Metadata, selection);
        if (!JsonSerializer.SerializeToUtf8Bytes(envelope with { Metadata = metadata, Selection = selection }, JsonOptions)
                .AsSpan().SequenceEqual(bytes))
            throw new InvalidDataException("Tabela Pareto/catálogo do envelope diverge da reprodução canônica.");
        return new V9FrozenCalibration(envelope, envelopeSha256, snapshot);
    }

    public static async Task ReserveIndependentTestAsync(string directory,
        V9FrozenCalibration frozen, V9IndependentTestReservation reservation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frozen);
        ArgumentNullException.ThrowIfNull(reservation);
        Identifier(reservation.StudyId);
        Identifier(reservation.RunId);
        ArgumentNullException.ThrowIfNull(reservation.Protocol);
        if (!double.IsFinite(reservation.Protocol.Alpha) || reservation.Protocol.Alpha is <= 0 or >= 1
            || reservation.Protocol.PredeclaredMaximumAcceptedPerIdentity < 1)
            throw new InvalidDataException("Protocolo TEST exige alpha e cap pré-declarados antes da abertura de rótulos.");
        foreach (var attestation in new[] { reservation.Protocol.IndependentSamplingAttestationSha256,
                     reservation.Protocol.PhysicalMeasurementAttestationSha256 })
            if (attestation is not null) RequireSha(attestation);
        foreach (var hash in new[] { reservation.CorpusSha256, reservation.IdentityGroupsSha256,
                     reservation.ProtocolSha256, reservation.EnvelopeSha256, reservation.FittedSnapshotSha256 }) RequireSha(hash);
        directory = PrepareDirectory(directory);
        await using var lease = await AcquireAsync(directory, cancellationToken);
        frozen = await ReadPinnedAsync(directory, frozen.Envelope.Metadata.Version, frozen.EnvelopeSha256, cancellationToken);
        var lineage = await ReadEnvelopesAsync(directory, cancellationToken);
        if (reservation.Seed is 42 or 43 or 44 || reservation.Seed < 0
            || reservation.ReservedUtc.Offset != TimeSpan.Zero || reservation.ReservedUtc < frozen.Envelope.Metadata.CreatedUtc
            || reservation.EnvelopeSha256 != frozen.EnvelopeSha256
            || reservation.FittedSnapshotSha256 != frozen.FittedSnapshot.ContentSha256
            || lineage.Any(f => new[] { f.FittedSnapshot.Payload.TrainCorpusSha256, f.FittedSnapshot.Payload.ValidationCorpusSha256 }
                .Contains(reservation.CorpusSha256, StringComparer.OrdinalIgnoreCase)
                || new[] { f.FittedSnapshot.Payload.TrainIdentityGroupsSha256, f.FittedSnapshot.Payload.ValidationIdentityGroupsSha256 }
                .Contains(reservation.IdentityGroupsSha256, StringComparer.OrdinalIgnoreCase)))
            throw new InvalidDataException("TEST deve ser novo, independente e fixado após congelamento; sementes 42/43/44 já consumidas.");
        var tests = Path.Combine(directory, "independent-tests");
        Directory.CreateDirectory(tests);
        // Intentionally NOT idempotent: reservation authorizes exactly one execution.
        // Repeating a successful or failed reservation never grants another TEST run.
        foreach (var file in Directory.EnumerateFiles(tests, "*.reservation.json"))
        {
            var old = JsonSerializer.Deserialize<V9IndependentTestReservation>(await File.ReadAllBytesAsync(file, cancellationToken), JsonOptions)
                ?? throw new InvalidDataException("Reserva TEST inválida.");
            if (old.StudyId == reservation.StudyId || old.RunId == reservation.RunId || old.Seed == reservation.Seed
                || old.EnvelopeSha256 == reservation.EnvelopeSha256
                || old.FittedSnapshotSha256 == reservation.FittedSnapshotSha256
                || old.CorpusSha256.Equals(reservation.CorpusSha256, StringComparison.OrdinalIgnoreCase)
                || old.IdentityGroupsSha256.Equals(reservation.IdentityGroupsSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("TEST já reservado/consumido: proibido repetir ou escolher resultado favorável.");
        }
        await WriteNewAtomicAsync(Path.Combine(tests, reservation.StudyId + ".reservation.json"),
            JsonSerializer.SerializeToUtf8Bytes(reservation, JsonOptions), cancellationToken);
    }

    public static async Task CompleteIndependentTestAsync(string directory,
        V9IndependentTestReceipt receipt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(receipt.Reservation);
        Identifier(receipt.Reservation.StudyId);
        RequireSha(receipt.EvidenceSha256);
        if (receipt.CompletedUtc.Offset != TimeSpan.Zero || receipt.CompletedUtc < receipt.Reservation.ReservedUtc
            || string.IsNullOrWhiteSpace(receipt.Outcome)) throw new InvalidDataException("Recibo TEST incompleto.");
        directory = PrepareDirectory(directory);
        await using var lease = await AcquireAsync(directory, cancellationToken);
        var prefix = Path.Combine(directory, "independent-tests", receipt.Reservation.StudyId);
        var reserved = await File.ReadAllBytesAsync(prefix + ".reservation.json", cancellationToken);
        if (!reserved.AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(receipt.Reservation, JsonOptions)))
            throw new InvalidDataException("Recibo diverge da reserva TEST anterior à abertura de rótulos.");
        var frozen = (await ReadEnvelopesAsync(directory, cancellationToken))
            .SingleOrDefault(f => f.EnvelopeSha256 == receipt.Reservation.EnvelopeSha256)
            ?? throw new InvalidDataException("Envelope TEST não encontrado.");
        var testEvidence = receipt.Evidence ?? throw new InvalidDataException("Métricas TEST ausentes.");
        var evidence = testEvidence.Metrics ?? throw new InvalidDataException("Métricas TEST ausentes.");
        ValidateMetrics(evidence, allowNoAccepted: true);
        if (testEvidence.CorpusSha256 != receipt.Reservation.CorpusSha256
            || testEvidence.IdentityGroupsSha256 != receipt.Reservation.IdentityGroupsSha256
            || receipt.Reservation.FittedSnapshotSha256 != frozen.FittedSnapshot.ContentSha256
            || receipt.EvidenceSha256 != FingerprintTestEvidence(testEvidence))
            throw new InvalidDataException("Evidência TEST diverge do corpus/snapshot/reserva fixados.");
        var groups = testEvidence.IdentityGroups ?? throw new InvalidDataException("Coorte TEST ausente.");
        foreach (var group in groups) RequireSha(group.BaseIdentityFingerprint);
        if (FingerprintTestIdentityGroups(groups.Select(g => g.BaseIdentityFingerprint)) != receipt.Reservation.IdentityGroupsSha256)
            throw new InvalidDataException("Identidades TEST divergem da coorte reservada antes da abertura de rótulos.");
        var protocol = receipt.Reservation.Protocol;
        var bound = V9IdentityClusterFdr.UpperBoundForPopulationMeanRatioByAnyErrorCp(
            groups, protocol.PredeclaredMaximumAcceptedPerIdentity, comparedConfigurations: 1, protocol.Alpha);
        if (groups.Count != evidence.IndependentAcceptedIdentities
            || bound.TotalAccepted != evidence.TruePositive + evidence.FalsePositive
            || bound.FalseAccepted != evidence.FalsePositive
            || evidence.StatisticalMethod != IndependentTestStatisticalMethod
            || !Near(evidence.FdrUpperBound, bound.UpperFdrConditional ?? 1d))
            throw new InvalidDataException("Limite TEST deve ser recalculado da coorte inteira sob o protocolo pré-declarado.");
        var policy = frozen.Envelope.Metadata.Policy;
        var passed = protocol.IndependentSamplingAttestationSha256 is not null
            && protocol.PhysicalMeasurementAttestationSha256 is not null && bound.DenominatorSupported
            && evidence.FdrUpperBound <= policy.MaximumFdrUpperBound
            && evidence.RecallGlobal >= policy.MinimumGlobalRecall
            && evidence.IndependentAcceptedIdentities >= policy.MinimumIndependentAcceptedIdentities
            && evidence.PhysicalP95Milliseconds <= policy.MaximumP95Milliseconds;
        if (receipt.Passed != passed || evidence.ProtocolSha256 != receipt.Reservation.ProtocolSha256)
            throw new InvalidDataException("Resultado TEST contradiz política congelada ou protocolo reservado.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(receipt, JsonOptions);
        var path = prefix + ".receipt.json";
        if (File.Exists(path))
        {
            if (!(await File.ReadAllBytesAsync(path, cancellationToken)).AsSpan().SequenceEqual(bytes))
                throw new InvalidDataException("Resultado TEST imutável: reprovação não pode ser substituída.");
            return;
        }
        await WriteNewAtomicAsync(path, bytes, cancellationToken);
    }

    private static V9CalibrationFreezeMetadata NormalizeAndValidate(V9FittedSnapshot snapshot,
        V9CalibrationFreezeMetadata metadata, V9BootstrapPareto2D.Result selection)
    {
        Identifier(metadata.Version);
        if (metadata.PredecessorEnvelopeSha256 is not null) RequireSha(metadata.PredecessorEnvelopeSha256);
        RequireSha(metadata.SelectionReportSha256);
        var catalog = PersonResolutionAttributeCatalog.All.Select(a => new V9CalibrationCatalogEntry(
            a.CanonicalCode, a.Semantic.ToString(), a.EligibleForResolution, a.MultiValued, a.CompatibilityProfile))
            .OrderBy(a => a.Code, StringComparer.Ordinal).ToArray();
        if (metadata.Catalog is null || !metadata.Catalog.OrderBy(a => a.Code, StringComparer.Ordinal).SequenceEqual(catalog)
            || metadata.CatalogVersion != PersonResolutionAttributeCatalog.CatalogVersion)
            throw new InvalidDataException("Congelamento exige catálogo completo e contrato corrente.");
        var m = metadata.Validation ?? throw new InvalidDataException("Evidência VALIDATION ausente.");
        ValidateMetrics(m);
        var p = metadata.Policy ?? throw new InvalidDataException("Política versionada ausente.");
        if (p.MaximumFdrUpperBound is < 0m or > .01m)
            throw new InvalidDataException("Congelamento não pode relaxar o teto de FDR de 1% do seletor por fonte.");
        var grouping = metadata.Grouping ?? throw new InvalidDataException("Configuração de agrupamento ausente.");
        var fittedGrouping = snapshot.Payload.CandidateGrouping;
        if (fittedGrouping is null || fittedGrouping.CandidateGrouping != grouping.CandidateGrouping
            || fittedGrouping.MinimumPairPosterior != grouping.MinimumPairPosterior
            || fittedGrouping.MaximumObservationsPerIdentity != grouping.MaximumObservationsPerIdentity)
            throw new InvalidDataException("Agrupamento medido deve estar fixado no hash do snapshot.");
        var configurationId = V9ParetoSourceSelection.ConfigurationId(snapshot.Payload.OptionId,
            grouping.CandidateGrouping, snapshot.Payload.ConflictMargin);
        var option = selection.Table.SingleOrDefault(e => e.Option.OptionId == metadata.SelectedConfigurationId);
        if (option is null || !option.Eligible || !option.OnFrontier
            || metadata.SelectedConfigurationId != configurationId
            || grouping.CandidateGrouping != "BLIND_FS_COMPLETE_LINK_CANDIDATES"
            || grouping.MinimumPairPosterior is <= .5m or >= 1m || grouping.MaximumObservationsPerIdentity < 2
            || option.Option.ConfigurationFingerprintSha256 != snapshot.ContentSha256
            || metadata.Origin == BootstrapOrigin && selection.InitialSuggestedOptionId != metadata.SelectedConfigurationId
            || metadata.Origin is not (BootstrapOrigin or ManualOrigin)
            || metadata.SelectorVersion != selection.Version || metadata.SelectionReportSha256 != FingerprintSelection(selection)
            || metadata.CreatedUtc.Offset != TimeSpan.Zero || metadata.CreatedUtc == default
            || string.IsNullOrWhiteSpace(metadata.Actor) || string.IsNullOrWhiteSpace(p.Version)
            || string.IsNullOrWhiteSpace(p.AbstentionPolicy) || string.IsNullOrWhiteSpace(m.StatisticalMethod)
            || p.MaximumP95Milliseconds != (decimal)selection.Policy.MaximumP95Milliseconds
            || p.MaximumFdrUpperBound != (decimal)selection.Policy.MaximumAcceptedFdrUpper
            || p.MinimumIndependentAcceptedIdentities != selection.Policy.MinimumIndependentIdentityGroups
            || !Near(p.MinimumGlobalRecall, selection.Policy.MinimumGlobalRecall)
            || m.TruePositive != option.Option.TrueAccepted || m.FalsePositive != option.Option.FalseAccepted
            || m.TrueEligibleBeforeBlocking != option.Option.GlobalEligibleTruth
            || m.IndependentAcceptedIdentities != option.Option.IndependentValidationIdentityGroups
            || !Near(m.RecallGlobal, option.Option.GlobalRecall) || !Near(m.Fdr, option.Option.AcceptedFdr!.Value)
            || !Near(m.FdrUpperBound, option.Option.IdentityClusterFdrUpper!.Value)
            || !Near(m.PhysicalP95Milliseconds, option.Option.PhysicalP95Milliseconds!.Value)
            || m.Abstentions < 0 || m.RecallLowerBound < 0 || m.RecallUpperBound > 1
            || m.RecallLowerBound > m.RecallGlobal || m.RecallUpperBound < m.RecallGlobal)
            throw new InvalidDataException("Snapshot/seleção/política/evidência incompatível ou estatisticamente inelegível.");
        foreach (var hash in new[] { m.ProtocolSha256, m.EnvironmentSha256, m.DecisionsSha256 }) RequireSha(hash);
        return metadata with { Catalog = Array.AsReadOnly(catalog) };
    }

    private static void ValidateMetrics(V9CalibrationValidationEvidence evidence, bool allowNoAccepted = false)
    {
        foreach (var hash in new[] { evidence.ProtocolSha256, evidence.EnvironmentSha256, evidence.DecisionsSha256 }) RequireSha(hash);
        if (evidence.TruePositive < 0 || evidence.FalsePositive < 0 || evidence.TrueEligibleBeforeBlocking <= 0
            || evidence.TruePositive > evidence.TrueEligibleBeforeBlocking
            || evidence.TruePositive > long.MaxValue - evidence.FalsePositive
            || !allowNoAccepted && evidence.TruePositive + evidence.FalsePositive == 0 || evidence.Abstentions < 0
            || evidence.IndependentAcceptedIdentities < 2
            || evidence.PhysicalP95Milliseconds < 0 || string.IsNullOrWhiteSpace(evidence.StatisticalMethod)
            || evidence.FdrUpperBound < evidence.Fdr || evidence.FdrUpperBound > 1m
            || evidence.RecallLowerBound < 0m || evidence.RecallUpperBound > 1m
            || evidence.RecallLowerBound > evidence.RecallGlobal || evidence.RecallUpperBound < evidence.RecallGlobal
            || Math.Abs(evidence.RecallGlobal - (decimal)evidence.TruePositive / evidence.TrueEligibleBeforeBlocking) > .0000000001m
            || Math.Abs(evidence.Fdr - (evidence.TruePositive + evidence.FalsePositive == 0 ? 0m
                : (decimal)evidence.FalsePositive / (evidence.TruePositive + evidence.FalsePositive))) > .0000000001m)
            throw new InvalidDataException("Métricas agregadas/bounds de identidades inválidos.");
    }

    private static bool Near(decimal value, double other) => Math.Abs((double)value - other) <= 1e-10;
    private static string FitPath(string directory, string version) => Path.Combine(directory, version + ".fitted.json");
    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static void RequireSha(string hash)
    {
        if (hash is null || hash.Length != 64 || hash.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new InvalidDataException("SHA-256 canônico ausente.");
    }
    private static void Identifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 100 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            throw new InvalidDataException("Identificador de versão/estudo inválido.");
    }
    private static string PrepareDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);
        return directory;
    }
    private static async Task<FileStream> AcquireAsync(string directory, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return new FileStream(Path.Combine(directory, ".lifecycle.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 200) { await Task.Delay(25, cancellationToken); }
        }
    }
    private static async Task WriteNewAtomicAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
            File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static async Task<List<V9FrozenCalibration>> ReadEnvelopesAsync(string directory, CancellationToken cancellationToken)
    {
        var result = new List<V9FrozenCalibration>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.envelope.json"))
        {
            var version = Path.GetFileName(path)[..^".envelope.json".Length];
            var hash = Sha(await File.ReadAllBytesAsync(path, cancellationToken));
            result.Add(await ReadPinnedAsync(directory, version, hash, cancellationToken));
        }
        return result;
    }
}
