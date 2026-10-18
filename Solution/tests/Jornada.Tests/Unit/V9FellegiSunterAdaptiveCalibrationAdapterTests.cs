using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9FellegiSunterAdaptiveCalibrationAdapterTests
{
    private static readonly string TrainSha = new('a', 64);
    private static readonly string ValidationSha = new('b', 64);
    private static readonly string TrainGroupsSha = new('c', 64);
    private static readonly string ValidationGroupsSha = new('d', 64);
    private static readonly string StudySha = new('e', 64);
    private static readonly DateOnly Birth = new(1980, 5, 6);

    [Test]
    public void Real_V9_adapter_fits_train_and_scores_validation_with_existing_Csharp_FS()
    {
        var option = new V9FsAdaptiveOptionConfiguration("T050", .5m, .50m);
        var adapter = new V9FellegiSunterAdaptiveCalibrationAdapter(Frame(), [option]);

        var request = new AdaptiveCalibrationStudyRequest(
            StudySha,
            Slice("TRAIN", TrainSha, TrainGroupsSha, 6),
            Slice("VALIDATION", ValidationSha, ValidationGroupsSha, 6),
            [new AdaptiveCalibrationModelOption(
                V9FellegiSunterAdaptiveCalibrationAdapter.Family,
                V9FellegiSunterAdaptiveCalibrationAdapter.Version,
                option.OptionId,
                option.FingerprintSha256)],
            ManuallyRequestedCalibration: true,
            new AdaptiveCalibrationPreviewBudget(
                AdaptiveCalibrationPreviewBudget.CurrentVersion,
                StudySha,
                MinimumGlobalAcceptedRecall: .95,
                MaximumObservedAcceptedFdrPoint: .01,
                MaximumBlockingFalseDegreeP99: 10,
                MinimumValidationIdentityGroups: 4));

        var report = AdaptiveCalibrationModelSearch.Evaluate(request, [adapter]);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(report.Options, Has.Count.EqualTo(1));
            Assert.That(report.Options[0].Validation.TruePairsWithoutCpf, Is.EqualTo(2));
            Assert.That(report.Options[0].Validation.TrueAcceptedLinks, Is.EqualTo(2));
            Assert.That(report.Options[0].Validation.FalseAcceptedLinks, Is.EqualTo(0));
            Assert.That(report.Options[0].Validation.Abstentions, Is.EqualTo(0));
            Assert.That(report.Options[0].ObservedAcceptedFdrPoint, Is.EqualTo(0d));
            Assert.That(report.Options[0].GlobalAcceptedRecall, Is.EqualTo(1d));
            Assert.That(report.Options[0].NominalPairwiseFdrUpper95, Is.GreaterThan(.57d));
            Assert.That(report.Options[0].MeetsNecessaryNominalFdrScreen, Is.False);
            Assert.That(report.SuggestedModelFamily, Is.Null);
            Assert.That(report.Status, Is.EqualTo("NO_MODEL_MEETS_PRELIMINARY_FDR_SCREEN"));
            Assert.That(report.TestUsed, Is.False);
            Assert.That(report.OperationalModelCreated, Is.False);
            Assert.That(report.AutomaticRecalibrationOrActivationAllowed, Is.False);
        }));
    }

    [Test]
    public void Configuration_fingerprint_is_bound_to_threshold_and_fit_is_train_only()
    {
        var option = new V9FsAdaptiveOptionConfiguration("T090", .5m, .90m);
        var adapter = new V9FellegiSunterAdaptiveCalibrationAdapter(Frame(), [option]);
        var model = new AdaptiveCalibrationModelOption(
            V9FellegiSunterAdaptiveCalibrationAdapter.Family,
            V9FellegiSunterAdaptiveCalibrationAdapter.Version,
            option.OptionId,
            option.FingerprintSha256);

        var fit = adapter.FitTrain(model, Slice("TRAIN", TrainSha, TrainGroupsSha, 6));

        Assert.That(fit.TrainOnly, Is.True);
        Assert.That(fit.TrainingCorpusFingerprintSha256, Is.EqualTo(TrainSha));
        Assert.That(fit.FittedParametersFingerprintSha256, Has.Length.EqualTo(64));

        var forged = model with { ConfigurationFingerprintSha256 = new string('f', 64) };
        Assert.That(
            (Action)(() => { _ = adapter.FitTrain(
                forged, Slice("TRAIN", TrainSha, TrainGroupsSha, 6)); }),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Preblocking_cohort_counts_zero_candidate_groups_in_validation_support()
    {
        var baseline = Frame();
        var trainFull = Enumerable.Range(1, 6)
            .Select(i => $"BASE-{i:00}").Append("BASE-TRAIN-ZERO").ToArray();
        var validationFull = Enumerable.Range(7, 6)
            .Select(i => $"BASE-{i:00}").Append("BASE-VAL-ZERO").ToArray();
        var expanded = baseline with
        {
            TrainPreBlockingBaseIdentityIds = trainFull,
            ValidationPreBlockingBaseIdentityIds = validationFull
        };
        var config = new V9FsAdaptiveOptionConfiguration("ALL_BASES", .5m, .5m);
        var adapter = new V9FellegiSunterAdaptiveCalibrationAdapter(expanded, [config]);
        var model = new AdaptiveCalibrationModelOption(
            V9FellegiSunterAdaptiveCalibrationAdapter.Family,
            V9FellegiSunterAdaptiveCalibrationAdapter.Version,
            config.OptionId, config.FingerprintSha256);
        // The SQL-only frame still has six groups per split, but the
        // attestable TRAIN/VALIDATION cohort contains seven each.
        var fit = adapter.FitTrain(model, Slice("TRAIN", TrainSha, TrainGroupsSha, 7));
        var metrics = adapter.EvaluateValidation(
            fit, Slice("VALIDATION", ValidationSha, ValidationGroupsSha, 7));
        Assert.That(metrics.IndependentlyLabeledIdentityGroups, Is.EqualTo(7));
        Assert.That((Action)(() => adapter.EvaluateValidation(
            fit, Slice("VALIDATION", ValidationSha, ValidationGroupsSha, 6))),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Preblocking_cohort_is_frozen_and_rejects_duplicates_leakage_and_missing_sql_group()
    {
        var source = Frame();
        var train = Enumerable.Range(1, 6).Select(i => $"BASE-{i:00}").ToList();
        var validation = Enumerable.Range(7, 6).Select(i => $"BASE-{i:00}").ToList();
        var option = new V9FsAdaptiveOptionConfiguration("COHORT", .5m, .5m);
        var frozen = new V9FellegiSunterAdaptiveCalibrationAdapter(source with
        {
            TrainPreBlockingBaseIdentityIds = train,
            ValidationPreBlockingBaseIdentityIds = validation
        }, [option]);
        // A mutable input collection must not change the snapshot.
        train.Clear();
        validation.Clear();
        var model = new AdaptiveCalibrationModelOption(
            V9FellegiSunterAdaptiveCalibrationAdapter.Family,
            V9FellegiSunterAdaptiveCalibrationAdapter.Version,
            option.OptionId, option.FingerprintSha256);
        Assert.That((Action)(() => { _ = frozen.FitTrain(
            model, Slice("TRAIN", TrainSha, TrainGroupsSha, 6)); }), Throws.Nothing);

        void Reject(IEnumerable<string> tr, IEnumerable<string> val) =>
            Assert.That((Action)(() => { _ = new V9FellegiSunterAdaptiveCalibrationAdapter(
                source with
                {
                    TrainPreBlockingBaseIdentityIds = tr.ToArray(),
                    ValidationPreBlockingBaseIdentityIds = val.ToArray()
                }, [option]); }), Throws.TypeOf<InvalidDataException>());

        var baseTrain = Enumerable.Range(1, 6).Select(i => $"BASE-{i:00}").ToArray();
        var baseVal = Enumerable.Range(7, 6).Select(i => $"BASE-{i:00}").ToArray();
        Reject(baseTrain.Append(baseTrain[0]), baseVal); // duplicate
        Reject(baseTrain, baseVal.Append(baseTrain[0])); // cross-split leakage
        Reject(baseTrain.Where(x => x != "BASE-01"), baseVal); // SQL group not in cohort
        Assert.That((Action)(() => { _ = new V9FellegiSunterAdaptiveCalibrationAdapter(
            source with { TrainPreBlockingBaseIdentityIds = baseTrain }, [option]); }),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Frame_rejects_test_partition_and_identity_leakage()
    {
        var pairs = Frame().Pairs.ToList();
        pairs.Add(new V9FsAdaptiveCandidatePair(
            "TEST", "TEST-A", "TEST-A", true,
            "MARIA SILVA", "MARIA SILVA", "ANA COSTA", "ANA COSTA",
            Birth, Birth, 1));

        Assert.That(
            (Action)(() => { _ = new V9FellegiSunterAdaptiveCalibrationAdapter(
                Frame() with { Pairs = pairs },
                [new V9FsAdaptiveOptionConfiguration("DEFAULT", .5m, .5m)]); }),
            Throws.TypeOf<InvalidDataException>());

        var leaked = Frame();
        var same = leaked.Pairs[0].LeftIdentityGroupId;
        var changed = leaked.Pairs.Select((x, i) =>
            i == 4 ? x with { LeftIdentityGroupId = same, RightIdentityGroupId = same } : x).ToArray();
        Assert.That(
            (Action)(() => { _ = new V9FellegiSunterAdaptiveCalibrationAdapter(
                leaked with { Pairs = changed },
                [new V9FsAdaptiveOptionConfiguration("DEFAULT", .5m, .5m)]); }),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Candidate_frame_is_snapshotted_before_train_and_validation()
    {
        var source = Frame();
        var mutablePairs = source.Pairs.ToList();
        var option = new V9FsAdaptiveOptionConfiguration("SNAPSHOT", .5m, .50m);
        var adapter = new V9FellegiSunterAdaptiveCalibrationAdapter(
            source with { Pairs = mutablePairs }, [option]);
        var reference = new V9FellegiSunterAdaptiveCalibrationAdapter(Frame(), [option]);
        var model = new AdaptiveCalibrationModelOption(
            V9FellegiSunterAdaptiveCalibrationAdapter.Family,
            V9FellegiSunterAdaptiveCalibrationAdapter.Version,
            option.OptionId, option.FingerprintSha256);

        // Mutar TRAIN e VALIDATION depois da construção não pode alterar o resultado.
        mutablePairs[0] = mutablePairs[0] with { LeftPersonName = "ALTERADO" };
        mutablePairs[4] = mutablePairs[4] with { RightMotherName = "ALTERADA" };
        mutablePairs.Clear();

        var train = Slice("TRAIN", TrainSha, TrainGroupsSha, 6);
        var validation = Slice("VALIDATION", ValidationSha, ValidationGroupsSha, 6);
        var actualFit = adapter.FitTrain(model, train);
        var referenceFit = reference.FitTrain(model, train);
        Assert.That(actualFit.FittedParametersFingerprintSha256,
            Is.EqualTo(referenceFit.FittedParametersFingerprintSha256));
        Assert.That(adapter.EvaluateValidation(actualFit, validation),
            Is.EqualTo(reference.EvaluateValidation(referenceFit, validation)));
    }

    [Test]
    public void Validation_rejects_forged_fit_configuration_or_training_provenance()
    {
        var option = new V9FsAdaptiveOptionConfiguration("PROVENANCE", .5m, .50m);
        var adapter = new V9FellegiSunterAdaptiveCalibrationAdapter(Frame(), [option]);
        var model = new AdaptiveCalibrationModelOption(
            V9FellegiSunterAdaptiveCalibrationAdapter.Family,
            V9FellegiSunterAdaptiveCalibrationAdapter.Version,
            option.OptionId, option.FingerprintSha256);
        var fit = adapter.FitTrain(model, Slice("TRAIN", TrainSha, TrainGroupsSha, 6));
        var validation = Slice("VALIDATION", ValidationSha, ValidationGroupsSha, 6);

        var forgedFits = new[]
        {
            fit with { ConfigurationFingerprintSha256 = new string('f', 64) },
            fit with { TrainingCorpusFingerprintSha256 = new string('f', 64) },
            fit with { OptionId = "OUTRA_OPCAO" },
            fit with { TrainOnly = false }
        };
        foreach (var forged in forgedFits)
            Assert.That(
                (Action)(() => { _ = adapter.EvaluateValidation(forged, validation); }),
                Throws.TypeOf<InvalidDataException>());
    }


    [Test]
    public void Source_recall_uses_eligible_observations_and_oracle_gold_collapses_duplicate_identity()
    {
        // A occurs in THREE managers -> three true unordered SQL edges but
        // only THREE eligible source observations. C occurs in two managers
        // and has ZERO SQL edges -> both still belong in the denominator.
        var birth = Birth;
        var observations = new[]
        {
            new V9PhysicalCalibrationObservation(100, "A", "VALIDATION", "G1",
                "MARIA SILVA", "ANA COSTA", birth),
            new V9PhysicalCalibrationObservation(101, "A", "VALIDATION", "G2",
                "MARIA SILVA", "ANA COSTA", birth),
            new V9PhysicalCalibrationObservation(102, "A", "VALIDATION", "G3",
                "MARIA SILVA", "ANA COSTA", birth),
            new V9PhysicalCalibrationObservation(103, "B", "VALIDATION", "G2",
                "CARLOS LIMA", "ROSA LIMA", birth.AddYears(4)),
            new V9PhysicalCalibrationObservation(104, "C", "VALIDATION", "G1",
                "JOAO PEREIRA", "LUCIA PEREIRA", birth),
            new V9PhysicalCalibrationObservation(105, "C", "VALIDATION", "G2",
                "JOAO PEREIRA", "LUCIA PEREIRA", birth)
        };
        var sqlEdges = new (int A, int B)[]
        {
            (100, 101), (100, 102), (101, 102), (100, 103)
        };
        var original = Frame();
        var valPairs = sqlEdges.Select(edge =>
        {
            var a = observations.Single(x => x.ObservationId == edge.A);
            var b = observations.Single(x => x.ObservationId == edge.B);
            return new V9FsAdaptiveCandidatePair("VALIDATION", a.BasePersonId,
                b.BasePersonId, a.BasePersonId == b.BasePersonId,
                a.PersonName, b.PersonName, a.MotherName, b.MotherName,
                a.BirthDate, b.BirthDate, 3);
        });
        var frame = original with
        {
            Pairs = original.Pairs.Where(x => x.Partition == "TRAIN")
                .Concat(valPairs).ToArray(),
            ValidationPreBlockingBaseIdentityIds = ["A", "B", "C"]
        };
        var option = new V9FsAdaptiveOptionConfiguration("SOURCE", .5m, .5m);
        var adapter = new V9FellegiSunterAdaptiveCalibrationAdapter(frame, [option]);
        var model = new AdaptiveCalibrationModelOption(
            V9FellegiSunterAdaptiveCalibrationAdapter.Family,
            V9FellegiSunterAdaptiveCalibrationAdapter.Version,
            option.OptionId, option.FingerprintSha256);
        _ = adapter.FitTrain(model, Slice("TRAIN", TrainSha, TrainGroupsSha, 6));
        var result = adapter.EvaluateSourceUnitResolveValidation(
            observations, sqlEdges, [.5m], 16);
        Assert.That(result, Has.Count.EqualTo(3));
        var raw = result.Single(x => x.CandidateGrouping == "RAW_SQL_OBSERVATION_CANDIDATES");
        var blind = result.Single(x => x.CandidateGrouping == "BLIND_EXACT_PERSON_MOTHER_DOB_GROUPING");
        var oracle = result.Single(x => x.SyntheticGoldOracleGrouping);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(raw.EligibleSourceObservationsBeforeBlocking, Is.EqualTo(5));
            Assert.That(oracle.EligibleSourceObservationsBeforeBlocking, Is.EqualTo(5));
            Assert.That(raw.EligibleSourceObservationsWithTrueSqlCandidate, Is.EqualTo(3));
            Assert.That(oracle.BlockingSourceRecall, Is.EqualTo(.6d));
            Assert.That(raw.GlobalSourceRecall,
                Is.EqualTo((double)raw.TrueAcceptedSourceObservations / 5d));
            Assert.That(oracle.GlobalSourceRecall,
                Is.EqualTo((double)oracle.TrueAcceptedSourceObservations / 5d));
            Assert.That(raw.Conflicts, Is.GreaterThan(oracle.Conflicts));
            Assert.That(oracle.TrueAcceptedSourceObservations,
                Is.GreaterThan(raw.TrueAcceptedSourceObservations));
            Assert.That(oracle.TrueAcceptedSourceObservations, Is.EqualTo(3));
            Assert.That(blind.TrueAcceptedSourceObservations, Is.EqualTo(3));
            Assert.That(blind.Conflicts, Is.Zero);
            Assert.That(blind.ImpureBlindGroups, Is.Zero);
            Assert.That(oracle.GlobalSourceRecall, Is.EqualTo(.6d));
            Assert.That(raw.TrueAcceptedSourceObservations
                + raw.FalseAcceptedSourceObservations
                + raw.Conflicts + raw.Unresolved, Is.EqualTo(6));
            Assert.That(oracle.OperationallyCertified, Is.False);
        }));
    }

    [Test]
    public void Source_resolve_rejects_test_and_duplicate_sql_edges()
    {
        var option = new V9FsAdaptiveOptionConfiguration("SOURCE_SAFE", .5m, .5m);
        var adapter = new V9FellegiSunterAdaptiveCalibrationAdapter(Frame(), [option]);
        var observations = new[]
        {
            new V9PhysicalCalibrationObservation(1, "A", "VALIDATION", "G1",
                "MARIA SILVA", "ANA COSTA", Birth),
            new V9PhysicalCalibrationObservation(2, "A", "VALIDATION", "G2",
                "MARIA SILVA", "ANA COSTA", Birth),
            new V9PhysicalCalibrationObservation(3, "T", "TEST", "G3",
                "MARIA SILVA", "ANA COSTA", Birth)
        };
        Assert.That((Action)(() => adapter.EvaluateSourceUnitResolveValidation(
            observations, [(1, 2)], [.5m], 16)),
            Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => adapter.EvaluateSourceUnitResolveValidation(
            observations.Take(2).ToArray(), [(1, 2), (1, 2)], [.5m], 16)),
            Throws.TypeOf<InvalidDataException>());
    }

    private static AdaptiveCalibrationSlice Slice(
        string partition, string corpus, string groups, int count) =>
        new(partition, corpus, groups, count, true, false);

    private static V9FsAdaptiveCandidateFrame Frame()
    {
        var g = Enumerable.Range(1, 12)
            .Select(i => $"BASE-{i:00}")
            .ToArray();
        return new V9FsAdaptiveCandidateFrame(
            TrainSha, ValidationSha, TrainGroupsSha, ValidationGroupsSha,
            [
                Pair("TRAIN", g[0], g[0], true, "MARIA SILVA", "MARIA SILVA", "ANA COSTA", "ANA COSTA", Birth, Birth, 2),
                Pair("TRAIN", g[1], g[1], true, "JOAO SOUZA", "JOAO SOUZA", "LUCIA SOUZA", "LUCIA SOUZA", Birth, Birth, 2),
                Pair("TRAIN", g[2], g[3], false, "MARIA SILVA", "CARLOS LIMA", "ANA COSTA", "ROSA LIMA", Birth, Birth.AddDays(20), 5),
                Pair("TRAIN", g[4], g[5], false, "JOAO SOUZA", "PEDRO ALVES", "LUCIA SOUZA", "TERESA ALVES", Birth, Birth.AddYears(3), 6),

                Pair("VALIDATION", g[6], g[6], true, "MARIA SILVA", "MARIA SILVA", "ANA COSTA", "ANA COSTA", Birth, Birth, 2),
                Pair("VALIDATION", g[7], g[7], true, "JOAO SOUZA", "JOAO SOUZA", "LUCIA SOUZA", "LUCIA SOUZA", Birth, Birth, 3),
                Pair("VALIDATION", g[8], g[9], false, "MARIA SILVA", "ROBERTO MENDES", "ANA COSTA", "HELENA MENDES", Birth, Birth.AddYears(5), 7),
                Pair("VALIDATION", g[10], g[11], false, "JOAO SOUZA", "PAULO NUNES", "LUCIA SOUZA", "MARTA NUNES", Birth, Birth.AddDays(10), 8)
            ]);
    }

    private static V9FsAdaptiveCandidatePair Pair(
        string partition, string leftGroup, string rightGroup, bool isTrue,
        string leftName, string rightName, string leftMother, string rightMother,
        DateOnly leftBirth, DateOnly rightBirth, int degree) =>
        new(partition, leftGroup, rightGroup, isTrue,
            leftName, rightName, leftMother, rightMother,
            leftBirth, rightBirth, degree);
}
