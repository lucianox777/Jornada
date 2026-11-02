using System.Text.Json;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9GoldHarnessSafetyTests
{
    [TestCase("Server=(localdb)\\MSSQLLocalDB;Database=JornadaLocal;Integrated Security=true")]
    [TestCase("Server=(localdb)\\JornadaV9Gold20261019_0000abcd;Database=JornadaLocal;Integrated Security=true")]
    [TestCase("Server=localhost;Database=JornadaHarness;Integrated Security=true")]
    [TestCase("Server=(localdb)\\JornadaV9Gold20261019_0000abcd;Database=JornadaHarness;User Id=fixture")]
    [TestCase("Server=(localdb)\\JornadaV9GoldAnything;Database=JornadaHarness;Integrated Security=true")]
    public void Rejects_original_remote_and_non_owned_database_before_connecting(string connection) =>
        Assert.Throws<InvalidOperationException>((Action)(() => V9GoldHarnessCommand.ValidateDisposableConnection(connection)));

    [TestCase("Server=(localdb)\\JornadaV9Gold20261019_0000abcd;Database=JornadaHarness;\nIntegrated Security=true")]
    [TestCase("Server=\"(localdb)\\JornadaV9Gold20261019_0000abcd\n\";Database=JornadaHarness;Integrated Security=true")]
    public void Rejects_control_characters_in_disposable_connection_before_sql(string connection) =>
        Assert.Throws<InvalidOperationException>((Action)(() =>
            V9GoldHarnessCommand.ValidateDisposableConnection(connection)));

    [TestCase("UPPER_TRAIN=0")]
    [TestCase("UPPER_TRAIN=0.0")]
    [TestCase("UPPER_TRAIN=0,7")]
    [TestCase("UPPER_TRAIN=invalid")]
    [TestCase("UPPER_TRAIN=")]
    [TestCase("OTHER=0.8")]
    public void Joint_upper_parser_fails_closed_for_invalid_manual_threshold(string argument) =>
        Assert.Throws<ArgumentException>((Action)(() =>
            Jornada.Linkage.Parameters.Worker.V9LowerNonMatchTrainCalibrator.ParseJointUpperArgument(argument)));

    [Test]
    public void Joint_upper_parser_accepts_invariant_decimal_candidate()
    {
        Assert.That(Jornada.Linkage.Parameters.Worker.V9LowerNonMatchTrainCalibrator
            .ParseJointUpperArgument("UPPER_TRAIN=0.85"), Is.EqualTo(0.85m));
        Assert.That(Jornada.Linkage.Parameters.Worker.V9LowerNonMatchTrainCalibrator
            .ParseJointUpperArgument("UPPER_TRAIN=1"), Is.EqualTo(1m));
        Assert.That(Jornada.Linkage.Parameters.Worker.V9LowerNonMatchTrainCalibrator
            .ParseJointUpperArgument("UPPER_TRAIN=1.0"), Is.EqualTo(1m));
    }

    private static string[] JointTrial(string order, string lower, string upper) =>
        [V9GoldHarnessCommand.JointTrainTrialFlag, "selection", "validation", "fit",
            "train.csv", "new-output", order, lower, upper,
            new string('a', 64) + V9GoldJointTrainMatrixCommand.PlanSuffix];

    [Test]
    public void Joint_train_accepts_only_explicit_three_order_physical_pair()
    {
        foreach (var order in Jornada.Linkage.Parameters.Worker.V9LowerNonMatchTrainCalibrator.JointOrders)
            Assert.DoesNotThrow((Action)(() => V9GoldHarnessCommand.ValidateJointTrainTrialArguments(
                JointTrial(order, "LOWER_NONMATCH=0.1", "UPPER_TRAIN=1"))));
    }

    [TestCase("G0_G3", "LOWER_NONMATCH=0.1", "UPPER_TRAIN=0.9")]
    [TestCase("BASELINE_G0_G1_G2_G3", "LOWER_NONMATCH=0.9", "UPPER_TRAIN=0.9")]
    [TestCase("BASELINE_G0_G1_G2_G3", "LOWER_NONMATCH=0.91", "UPPER_TRAIN=0.9")]
    [TestCase("BASELINE_G0_G1_G2_G3", "LOWER_NONMATCH=0", "UPPER_TRAIN=0.9")]
    [TestCase("BASELINE_G0_G1_G2_G3", "LOWER_NONMATCH=0.1", "UPPER_TRAIN=1.01")]
    [TestCase("BASELINE_G0_G1_G2_G3", "LOWER_NONMATCH=0,1", "UPPER_TRAIN=0.9")]
    public void Joint_train_rejects_invalid_or_unregistered_cells(string order, string lower, string upper) =>
        Assert.Throws<ArgumentException>((Action)(() =>
            V9GoldHarnessCommand.ValidateJointTrainTrialArguments(JointTrial(order, lower, upper))));

    [Test]
    public void Joint_train_requires_preregistered_plan_and_rejects_extra_options_before_sql()
    {
        var valid = JointTrial("BASELINE_G0_G1_G2_G3", "LOWER_NONMATCH=0.1", "UPPER_TRAIN=0.9");
        Assert.DoesNotThrow((Action)(() =>
            V9GoldHarnessCommand.ValidateJointTrainTrialArguments(valid)));
        Assert.Throws<ArgumentException>((Action)(() =>
            V9GoldHarnessCommand.ValidateJointTrainTrialArguments(valid[..^1])),
            "No joint TRAIN physical cell may execute without a frozen preregistered plan.");
        var badPlan = (string[])valid.Clone();
        badPlan[9] = "unregistered.json";
        Assert.Throws<ArgumentException>((Action)(() =>
            V9GoldHarnessCommand.ValidateJointTrainTrialArguments(badPlan)));
    }

    [Test]
    public void Joint_train_rejects_missing_or_duplicate_trial_options_before_sql()
    {
        var valid = JointTrial("BASELINE_G0_G1_G2_G3", "LOWER_NONMATCH=0.1", "UPPER_TRAIN=0.9");
        Assert.Throws<ArgumentException>((Action)(() =>
            V9GoldHarnessCommand.ValidateJointTrainTrialArguments(valid[..^1])));
        var duplicate = (string[])valid.Clone();
        duplicate[8] = duplicate[7];
        Assert.Throws<ArgumentException>((Action)(() =>
            V9GoldHarnessCommand.ValidateJointTrainTrialArguments(duplicate)));
    }


    [Test]
    public void Joint_train_recall_denominator_is_frozen_before_threshold_dependent_publication()
    {
        IReadOnlyDictionary<string, IReadOnlyList<string>> waves =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["G0"] = ["A", "B", "A"],
                ["G1"] = ["A", "C"],
                ["G2"] = ["C", "A", "D"],
                ["G3"] = ["B", "D", "E"]
            };
        Assert.Multiple((Action)(() =>
        {
            Assert.That(V9GoldHarnessCommand.CountPreregisteredTrueLinkOpportunities(
                waves, ["G0", "G1", "G2", "G3"]), Is.EqualTo(5));
            Assert.That(V9GoldHarnessCommand.CountPreregisteredTrueLinkOpportunities(
                waves, ["G3", "G2", "G1", "G0"]), Is.EqualTo(6));
            // Duplicate A inside first G0 wave is not an earlier-wave link.
            Assert.That(V9GoldHarnessCommand.CountPreregisteredTrueLinkOpportunities(
                new Dictionary<string, IReadOnlyList<string>> { ["G0"] = ["A", "A"] },
                ["G0"]), Is.Zero);
        }));
    }

    [Test]
    public void Joint_train_denominator_rejects_missing_duplicate_and_corrupt_waves()
    {
        IReadOnlyDictionary<string, IReadOnlyList<string>> waves =
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["G0"] = ["A"], ["G1"] = ["A"]
            };
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9GoldHarnessCommand.CountPreregisteredTrueLinkOpportunities(waves, ["G0"])));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9GoldHarnessCommand.CountPreregisteredTrueLinkOpportunities(waves, ["G0", "G0"])));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9GoldHarnessCommand.CountPreregisteredTrueLinkOpportunities(
                new Dictionary<string, IReadOnlyList<string>>
                {
                    ["G0"] = ["A"], ["G1"] = [""]
                }, ["G0", "G1"])));
    }

    [Test]
    public void Accepts_only_explicit_disposable_instance_contract_before_resident_marker_and_empty_checks() =>
        Assert.DoesNotThrow((Action)(() => V9GoldHarnessCommand.ValidateDisposableConnection(
            "Server=(localdb)\\JornadaV9Gold20261019_0000abcd;Database=JornadaHarness;Integrated Security=true")));

    [TestCase("selection")]
    [TestCase("validation")]
    [TestCase("gold")]
    [TestCase("csv")]
    public void Joint_train_rejects_unpinned_frozen_input_before_SQL(string changedInput)
    {
        // A direct Runner invocation must not bypass the PowerShell orchestrator's
        // SHA checks. All files are deliberately syntactically incomplete: the
        // expected failure must be the preregistered digest, BEFORE JSON/SQL.
        var root = Path.Combine(Path.GetTempPath(),
            "JornadaV9JointPreflight_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var selectionPath = Path.Combine(root, "selection.json");
            var validationPath = Path.Combine(root, "validation.json");
            var goldPath = Path.Combine(root, "gold.fitted.json");
            var csvPath = Path.Combine(root, "train.csv");
            File.WriteAllText(selectionPath, "{}");
            File.WriteAllText(validationPath, "{}");
            File.WriteAllText(goldPath, "{}");
            File.WriteAllText(csvPath, "synthetic,not,a,real,corpus");
            string Digest(string path) => V9GoldJointTrainMatrixCommand.Sha(File.ReadAllBytes(path));
            string Pin(string name, string path) => changedInput == name
                ? new string('f', 64) : Digest(path);
            var uppers = new[] { .8m, .9m };
            var lowers = new[] { .1m, .2m };
            var plan = new V9GoldJointTrainMatrixCommand.Plan(
                V9GoldJointTrainMatrixCommand.Version, new string('a', 64),
                Pin("gold", goldPath), Pin("csv", csvPath), Pin("selection", selectionPath),
                Pin("validation", validationPath),
                uppers, lowers, 2, ManualCalibrationRequested: true, TestUsed: false,
                Jornada.Linkage.Parameters.Worker.V9LowerNonMatchTrainCalibrator
                    .JointGridFingerprint(uppers, lowers));
            var bytes = JsonSerializer.SerializeToUtf8Bytes(plan);
            var planPath = Path.Combine(root,
                V9GoldJointTrainMatrixCommand.Sha(bytes) +
                V9GoldJointTrainMatrixCommand.PlanSuffix);
            File.WriteAllBytes(planPath, bytes);
            var output = Path.Combine(root, "must-not-exist");
            var args = new[] { V9GoldHarnessCommand.JointTrainTrialFlag,
                selectionPath, validationPath, goldPath, csvPath, output,
                "BASELINE_G0_G1_G2_G3", "LOWER_NONMATCH=0.1", "UPPER_TRAIN=0.8",
                planPath };
            var exception = Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(async () =>
            {
                _ = await V9GoldHarnessCommand.RunJointTrainTrialAsync(args,
                    "Server=(localdb)\\JornadaV9Gold20261031_00000001;Database=JornadaHarness;Integrated Security=true");
            }));
            Assert.That(exception!.Message, Does.Contain("frozen input provenance"));
            Assert.That(Directory.Exists(output), Is.False,
                "Rejected TRAIN evidence must not create an output directory.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }


    [Test]
    public void Frozen_gold_train_csv_rejects_invalid_utf8_before_sql()
    {
        // TextFieldParser's default UTF-8 decoding can replace malformed bytes
        // with U+FFFD. Frozen identity evidence must not be silently changed.
        var directory = Path.Combine(Path.GetTempPath(), "v9-gold-utf8-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "observacoes.csv");
            var header = System.Text.Encoding.UTF8.GetBytes(
                "observacao_id,base_person_id,particao,gestor,nome,nome_mae,data_nascimento,cpf\\n" +
                "O1,B1,TRAIN,G0,");
            var suffix = System.Text.Encoding.UTF8.GetBytes(",Maria,1990-01-01,\\n");
            // C3 28 is not a valid UTF-8 sequence; the old parser accepted it.
            File.WriteAllBytes(path, [.. header, 0xC3, 0x28, .. suffix]);
            var reader = typeof(V9GoldHarnessCommand).GetMethod("ReadSources",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.That(reader, Is.Not.Null);
            var error = Assert.Throws<System.Reflection.TargetInvocationException>((Action)(() =>
                _ = reader!.Invoke(null, new object?[] { path, null, null })));
            Assert.That(error!.InnerException is System.Text.DecoderFallbackException or InvalidDataException,
                Is.True, "Invalid UTF-8 must fail closed before physical SQL replay.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestCase("extra_column")]
    [TestCase("missing_column")]
    [TestCase("unknown_partition")]
    [TestCase("duplicate_header")]
    public void Frozen_gold_train_csv_rejects_noncanonical_row_shape_or_partition(string mutation)
    {
        // Exercise the actual Runner CSV reader without SQL or a model.
        // The old reader silently ignored surplus fields and unknown partitions.
        var directory = Path.Combine(Path.GetTempPath(), "v9-gold-csv-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "observacoes.csv");
            const string header = "observacao_id,base_person_id,particao,gestor,nome,nome_mae,data_nascimento,cpf";
            const string good = "O1,B1,TRAIN,G0,Ana,Maria,1990-01-01,";
            var rows = mutation switch
            {
                "extra_column" => new[] { header, good + ",unexpected" },
                "missing_column" => new[] { header, "O1,B1,TRAIN,G0,Ana,Maria,1990-01-01" },
                "unknown_partition" => new[] { header, good, "O2,B2,UNKNOWN,G1,Beto,Lucia,1991-01-01," },
                "duplicate_header" => new[] { header + ",cpf", good + "," },
                _ => throw new ArgumentOutOfRangeException(nameof(mutation))
            };
            File.WriteAllLines(path, rows);
            var reader = typeof(V9GoldHarnessCommand).GetMethod("ReadSources",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.That(reader, Is.Not.Null);
            var exception = Assert.Throws<System.Reflection.TargetInvocationException>((Action)(() =>
            {
                _ = reader!.Invoke(null, new object?[] { path, null, null });
            }));
            Assert.That(exception!.InnerException, Is.TypeOf<InvalidDataException>(),
                "Malformed frozen CSV must fail as invalid evidence, not be silently truncated or crash.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
