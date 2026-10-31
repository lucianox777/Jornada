using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class FrozenIbgeNominalTfV9Tests
{
    private const string FullPublishedSha = "A1B2C3D4E5F60718293A4B5C6D7E8F90123456789ABCDEF00112233445566778";
    private static readonly NominalSyntheticIbgeBootstrapV9 Bootstrap = new(
        CompositeNominalTermFrequencyContractV9.SyntheticIbgeProvenance,
        30_000, "CORPUS_30000_SHA256", "GENERATOR_V1", 42UL, "HYPOTHESES_V1");

    private static PreparedNominalTermFrequency Prepared()
    {
        var rows = new[]
        {
            new NominalTermFrequencyEntry("NOME_PRENOME", "MARIA", 60, 100, .60m),
            new NominalTermFrequencyEntry("NOME_PRENOME", "ZULEICA", 5, 100, .05m),
            new NominalTermFrequencyEntry("NOME_MAE_PRENOME", "ANA", 30, 100, .30m),
            new NominalTermFrequencyEntry("NOME_MAE_PRENOME", "EVA", 10, 100, .10m),
            new NominalTermFrequencyEntry("NOME_ULTIMO_SOBRENOME", "SILVA", 40, 100, .40m),
            new NominalTermFrequencyEntry("NOME_MAE_ULTIMO_SOBRENOME", "SOUZA", 20, 100, .20m)
        };
        return new PreparedNominalTermFrequency(
            NominalTermFrequencySnapshot.Create(rows), rows, 9123);
    }

    private static FrozenIbgeNominalTfV9 Bound() =>
        FrozenIbgeNominalTfV9.BindPublished(
            Prepared(), "CENSO2022_NOMES_BRASIL_V1",
            FullPublishedSha, "SQL_BLOCKING_FRAME_PINNED", Bootstrap);

    private static readonly CompositeNominalLevelV9[] Levels =
        Enum.GetValues<CompositeNominalLevelV9>();

    private static FellegiSunterJointNominalCellV9[] Frame() =>
        Levels.SelectMany(p => Levels.Select(m =>
            new FellegiSunterJointNominalCellV9(p, m, 1m / 81m, 1m / 81m))).ToArray();

    private static IReadOnlyDictionary<string, decimal> Parameters() =>
        new Dictionary<string, decimal>
        {
            [LinkageParameterCatalog.PriorMatchProbability] = .01m,
            [LinkageParameterCatalog.DecisionEvidenceScoring] = 1m,
            [LinkageParameterCatalog.NeutralMissingEvidenceScoring] = 1m,
            [LinkageParameterCatalog.BirthSingleEvidenceScoring] = 1m,
            ["M_DATA_NASCIMENTO_EXACT"] = .80m,
            ["U_DATA_NASCIMENTO_EXACT"] = .20m,
            ["M_DATA_NASCIMENTO_DIFF"] = .20m,
            ["U_DATA_NASCIMENTO_DIFF"] = .80m
        };

    [Test]
    public void Frozen_reference_binds_actual_token_and_sql_reference_identity_not_caller_frequency()
    {
        var frozen = Bound();
        var common = frozen.CreatePreview(
            CompositeNominalTfSideV9.Person, CompositeNominalLevelV9.ExactFirst,
            "Maria Lima", "MARIA Costa", .10m, 1m, .00001m, 100)!;
        var rare = frozen.CreatePreview(
            CompositeNominalTfSideV9.Person, CompositeNominalLevelV9.ExactFirst,
            "Zuleica Lima", "ZULEICA Costa", .10m, 1m, .00001m, 100)!;
        Assert.Multiple((Action)(() =>
        {
            Assert.That(frozen.ReferenceVersionId, Is.EqualTo(9123));
            Assert.That(frozen.PublishedReferenceSha256, Is.EqualTo(FullPublishedSha));
            Assert.That(frozen.MaterializedFrequencySha256.Length, Is.EqualTo(64));
            Assert.That(frozen.MaterializedFrequencySha256, Is.Not.EqualTo(FullPublishedSha));
            Assert.That(common.TermFrequency, Is.EqualTo(.60m));
            Assert.That(rare.TermFrequency, Is.EqualTo(.05m));
            Assert.That(common.Reference.PopulationFrameFingerprint,
                Does.Contain("SP_3550308_TODOS"));
            Assert.That(common.Reference.PopulationFrameFingerprint,
                Does.Contain(FullPublishedSha));
            Assert.That(common.Reference.SyntheticBootstrap!.GeneratorSeed, Is.EqualTo(42UL));
        }));

        FellegiSunterScoreBreakdown Score(string left, string right, CompositeNominalTfPreviewV9? tf) =>
            FellegiSunterScoring.CalculateCompositeNominalV9PreviewWithBreakdown(
                Parameters(), left, right, null, null,
                Frame(), CompositeNominalComparisonV9.ContractVersion,
                nominalTf: tf);
        var noTf = Score("Maria Lima", "MARIA Costa", null);
        var withCommon = Score("Maria Lima", "MARIA Costa", common);
        var withRare = Score("Zuleica Lima", "ZULEICA Costa", rare);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(withCommon.Contributions.Count(x => x.Evidence == "NOMINAL_CONJUNTO_V9"),
                Is.EqualTo(1));
            Assert.That(withCommon.Contributions.Any(x => x.Evidence is "NOME" or "NOME_MAE"),
                Is.False);
            Assert.That(withCommon.Score.LogOdds - noTf.Score.LogOdds,
                Is.EqualTo((decimal)Math.Log(1d / 6d)).Within(.000000001m));
            Assert.That(withRare.Score.LogOdds - noTf.Score.LogOdds,
                Is.EqualTo((decimal)Math.Log(2d)).Within(.000000001m));
        }));
    }

    [Test]
    public void Mother_has_distinct_population_frame_and_frozen_frequency()
    {
        var frozen = Bound();
        var mother = frozen.CreatePreview(
            CompositeNominalTfSideV9.Mother, CompositeNominalLevelV9.ExactFirst,
            "Ana Souza", "ANA Costa", .11m, 1m, .00001m, 100)!;
        Assert.Multiple((Action)(() =>
        {
            Assert.That(mother.TermFrequency, Is.EqualTo(.30m));
            Assert.That(mother.Reference.PopulationFrameFingerprint, Does.Contain("BR_FEMININO"));
            Assert.That(mother.Reference.PopulationFrameFingerprint, Does.Not.Contain("SP_3550308_TODOS"));
            Assert.That(mother.Reference.Unit, Is.EqualTo(NominalFrequencyUnitV9.FirstName));
        }));
    }

    [Test]
    public void Frozen_surname_proxy_uses_last_content_surname_and_distinct_ibge_frames()
    {
        var frozen = Bound();
        var person = frozen.CreatePreview(
            CompositeNominalTfSideV9.Person, CompositeNominalLevelV9.ExactLast,
            "Maria da Silva", "Ana Silva", .10m, .5m, .00001m, 100)!;
        var mother = frozen.CreatePreview(
            CompositeNominalTfSideV9.Mother, CompositeNominalLevelV9.ExactLast,
            "Ana de Souza", "Eva Souza", .10m, 1m, .00001m, 100)!;
        Assert.Multiple((Action)(() =>
        {
            Assert.That(person.TermFrequency, Is.EqualTo(.40m));
            Assert.That(mother.TermFrequency, Is.EqualTo(.20m));
            Assert.That(person.Reference.Unit, Is.EqualTo(NominalFrequencyUnitV9.LastContentSurname));
            Assert.That(person.Reference.Source, Is.EqualTo(NominalFrequencySourceV9.IbgeSurnameAnyPositionProxy));
            Assert.That(mother.Reference.Source, Is.EqualTo(NominalFrequencySourceV9.IbgeSurnameAnyPositionProxy));
            Assert.That(person.Reference.PopulationFrameFingerprint, Does.Contain("SP_3550308_TODOS"));
            Assert.That(mother.Reference.PopulationFrameFingerprint, Does.Contain("BR_TODOS"));
        }));
        Assert.That(frozen.CreatePreview(
            CompositeNominalTfSideV9.Person, CompositeNominalLevelV9.ExactLast,
            "Nome ausente", "Nome ausente", .10m, 0m, .00001m, 100), Is.Null);
        Assert.That((Action)(() => frozen.CreatePreview(
            CompositeNominalTfSideV9.Person, CompositeNominalLevelV9.ExactLast,
            "Maria Lima", "Ana Lima", .10m, 1m, .00001m, 100)),
            Throws.InvalidOperationException);
    }

    [Test]
    public void Zero_weight_is_explicit_neutral_even_when_name_is_unknown_but_unknown_positive_weight_fails()
    {
        var frozen = Bound();
        Assert.That(frozen.CreatePreview(
            CompositeNominalTfSideV9.Person, CompositeNominalLevelV9.ExactFirst,
            "NAO PUBLICADO", "NAO PUBLICADO", .10m, 0m, .001m, 1), Is.Null);
        Assert.Multiple((Action)(() =>
        {
            Assert.That((Action)(() => frozen.CreatePreview(
                CompositeNominalTfSideV9.Person, CompositeNominalLevelV9.ExactFirst,
                "NAO PUBLICADO", "NAO PUBLICADO", .10m, 1m, .001m, 1)),
                Throws.InvalidOperationException);
            Assert.That((Action)(() => frozen.CreatePreview(
                CompositeNominalTfSideV9.Person, CompositeNominalLevelV9.ExactFirst,
                "Maria Lima", "Zuleica Costa", .10m, 1m, .001m, 1)),
                Throws.InvalidOperationException);
            Assert.That((Action)(() => frozen.CreatePreview(
                CompositeNominalTfSideV9.Person, CompositeNominalLevelV9.ExactFull,
                "Maria Silva", "MARIA SILVA", .10m, 1m, .001m, 1)),
                Throws.InvalidOperationException);
            Assert.That((Action)(() => frozen.CreatePreview(
                CompositeNominalTfSideV9.Person, CompositeNominalLevelV9.ExactFirst,
                "MARIA SILVA", "MARIA COSTA", 0m, 1m, .001m, 1)),
                Throws.InvalidOperationException);
            Assert.That((Action)(() => frozen.CreatePreview(
                CompositeNominalTfSideV9.Person, CompositeNominalLevelV9.ExactFirst,
                "MARIA SILVA", "MARIA COSTA", .10m, -1m, .001m, 1)),
                Throws.InvalidOperationException);
        }));
    }

    [Test]
    public void Rejects_corrupt_version_sha_bootstrap_and_mismatched_snapshot_entries()
    {
        var prepared = Prepared();
        FrozenIbgeNominalTfV9 Bind(PreparedNominalTermFrequency p,
            string hash = FullPublishedSha, NominalSyntheticIbgeBootstrapV9? bootstrap = null) =>
            FrozenIbgeNominalTfV9.BindPublished(
                p, "CENSO2022_NOMES_BRASIL_V1", hash,
                "BLOCKING_FRAME", bootstrap ?? Bootstrap);
        var tampered = prepared with
        {
            Snapshot = NominalTermFrequencySnapshot.Create(new[]
            {
                new NominalTermFrequencyEntry("NOME_PRENOME", "MARIA", 10, 100, .1m),
                new NominalTermFrequencyEntry("NOME_MAE_PRENOME", "ANA", 30, 100, .3m)
            })
        };
        Assert.Multiple((Action)(() =>
        {
            Assert.That((Action)(() => Bind(prepared, "F00")), Throws.InvalidOperationException);
            Assert.That((Action)(() => Bind(prepared with { ReferenceVersionId = 0 })),
                Throws.InvalidOperationException);
            Assert.That((Action)(() => Bind(prepared, bootstrap: Bootstrap with { People = 29_999 })),
                Throws.InvalidOperationException);
            Assert.That((Action)(() => Bind(tampered)), Throws.InvalidOperationException);
        }));
    }

    [Test]
    public void Frozen_frequency_digest_is_order_independent_but_changes_with_data()
    {
        var original = Prepared();
        var a = Bound();
        var reversed = original with { Entries = original.Entries.Reverse().ToArray() };
        var b = FrozenIbgeNominalTfV9.BindPublished(
            reversed, "CENSO2022_NOMES_BRASIL_V1",
            FullPublishedSha, "SQL_BLOCKING_FRAME_PINNED", Bootstrap);
        Assert.That(b.MaterializedFrequencySha256, Is.EqualTo(a.MaterializedFrequencySha256));
        Assert.That(b.ReferenceVersionId, Is.EqualTo(a.ReferenceVersionId));

        // Same published reference identity cannot hide a changed materialized
        // count/frequency. The snapshot must be rebuilt from the changed rows.
        var changedRows = original.Entries.Select(x =>
            x.Attribute == NominalTermFrequencySnapshot.PersonFirstNameAttribute
                && x.ValueNormalized == "MARIA"
                ? x with { Occurrences = 61, Frequency = .61m }
                : x).ToArray();
        var changed = FrozenIbgeNominalTfV9.BindPublished(
            new PreparedNominalTermFrequency(
                NominalTermFrequencySnapshot.Create(changedRows), changedRows,
                original.ReferenceVersionId),
            "CENSO2022_NOMES_BRASIL_V1", FullPublishedSha,
            "SQL_BLOCKING_FRAME_PINNED", Bootstrap);
        Assert.That(changed.MaterializedFrequencySha256,
            Is.Not.EqualTo(a.MaterializedFrequencySha256));

        // The digest must bind surname-proxy materialization too, not just first names.
        var surnameChangedRows = original.Entries.Select(x =>
            x.Attribute == NominalTermFrequencySnapshot.MotherLastSurnameAttribute
                && x.ValueNormalized == "SOUZA"
                ? x with { Occurrences = 21, Frequency = .21m }
                : x).ToArray();
        var surnameChanged = FrozenIbgeNominalTfV9.BindPublished(
            new PreparedNominalTermFrequency(
                NominalTermFrequencySnapshot.Create(surnameChangedRows),
                surnameChangedRows, original.ReferenceVersionId),
            "CENSO2022_NOMES_BRASIL_V1", FullPublishedSha,
            "SQL_BLOCKING_FRAME_PINNED", Bootstrap);
        Assert.That(surnameChanged.MaterializedFrequencySha256,
            Is.Not.EqualTo(a.MaterializedFrequencySha256));
    }
    [Test]
    public void Exported_frozen_marginals_keep_explicit_unit_and_surname_proxy_provenance()
    {
        var frozen = Bound();
        var personFirst = frozen.ExportForSnapshot(
            CompositeNominalTfSideV9.Person, CompositeNominalLevelV9.ExactFirst);
        var motherFirst = frozen.ExportForSnapshot(
            CompositeNominalTfSideV9.Mother, CompositeNominalLevelV9.ExactFirst);
        var personLast = frozen.ExportForSnapshot(
            CompositeNominalTfSideV9.Person, CompositeNominalLevelV9.ExactLast);
        var motherLast = frozen.ExportForSnapshot(
            CompositeNominalTfSideV9.Mother, CompositeNominalLevelV9.ExactLast);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(personFirst.Reference.Unit, Is.EqualTo(NominalFrequencyUnitV9.FirstName));
            Assert.That(motherFirst.Reference.PopulationFrameFingerprint, Does.Contain("BR_FEMININO"));
            Assert.That(personFirst.Frequencies["MARIA"], Is.EqualTo(.60m));
            Assert.That(motherFirst.Frequencies["ANA"], Is.EqualTo(.30m));
            Assert.That(personLast.Reference.Unit, Is.EqualTo(NominalFrequencyUnitV9.LastContentSurname));
            Assert.That(personLast.Reference.Source, Is.EqualTo(NominalFrequencySourceV9.IbgeSurnameAnyPositionProxy));
            Assert.That(motherLast.Reference.Source, Is.EqualTo(NominalFrequencySourceV9.IbgeSurnameAnyPositionProxy));
            Assert.That(personLast.Reference.PopulationFrameFingerprint, Does.Contain("SP_3550308_TODOS"));
            Assert.That(motherLast.Reference.PopulationFrameFingerprint, Does.Contain("BR_TODOS"));
            Assert.That(personLast.Frequencies["SILVA"], Is.EqualTo(.40m));
            Assert.That(motherLast.Frequencies["SOUZA"], Is.EqualTo(.20m));
            Assert.That(personLast.Frequencies.ContainsKey("MARIA"), Is.False);
            Assert.That(motherLast.Frequencies.ContainsKey("ANA"), Is.False);
        }));
        Assert.That((Action)(() => frozen.ExportForSnapshot(
            CompositeNominalTfSideV9.Person, CompositeNominalLevelV9.ExactFull)),
            Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => frozen.ExportForSnapshot(
            (CompositeNominalTfSideV9)99, CompositeNominalLevelV9.ExactFirst)),
            Throws.TypeOf<InvalidDataException>());
    }


    [Test]
    public void Surname_export_fails_closed_when_proxy_marginal_is_missing_without_disabling_first_name()
    {
        var prepared = Prepared();
        var firstNamesOnly = prepared.Entries.Where(x =>
            x.Attribute is NominalTermFrequencySnapshot.PersonFirstNameAttribute
                or NominalTermFrequencySnapshot.MotherFirstNameAttribute).ToArray();
        var frozen = FrozenIbgeNominalTfV9.BindPublished(
            new PreparedNominalTermFrequency(
                NominalTermFrequencySnapshot.Create(firstNamesOnly),
                firstNamesOnly, prepared.ReferenceVersionId),
            "CENSO2022_NOMES_BRASIL_V1", FullPublishedSha,
            "SQL_BLOCKING_FRAME_PINNED", Bootstrap);

        Assert.That(frozen.ExportForSnapshot(
            CompositeNominalTfSideV9.Person, CompositeNominalLevelV9.ExactFirst)
            .Frequencies.ContainsKey("MARIA"), Is.True);
        Assert.That((Action)(() => frozen.ExportForSnapshot(
            CompositeNominalTfSideV9.Person, CompositeNominalLevelV9.ExactLast)),
            Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => frozen.ExportForSnapshot(
            CompositeNominalTfSideV9.Mother, CompositeNominalLevelV9.ExactLast)),
            Throws.TypeOf<InvalidDataException>());
    }
}
