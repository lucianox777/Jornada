using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9GoldLowerNonMatchExperimentTests
{
    private static void Multiple(Action a) => Assert.Multiple(a);
    private static void Reject<T>(Action a) where T : Exception => Assert.Throws<T>(a);
    private static readonly Guid Model = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Candidate = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly string Fit = new('a', 64);
    private static V9LowerNonMatchPreview.VerifiedRetrieval Frame(
        bool index = true, bool complete = true, bool passes = true) =>
        new(Fit, new string('b', 64), 1, index, complete, passes);
    private static ProbabilisticLinkageDecision Rejected(decimal p) =>
        new(ResolutionStatus.NAO_RESOLVIDO, null, Candidate, p, null, null, null,
            Model, "ABAIXO_T_LINKAGE");

    [Test]
    public void Invariant_threshold_parser_fails_closed_on_unsafe_or_ambiguous_arguments()
    {
        Assert.That(V9GoldLowerNonMatchExperiment.ParseThreshold("LOWER_NONMATCH=0.2", .9m),
            Is.EqualTo(.2m));
        foreach (var bad in new[] {
            "0.2", "LOWER_NONMATCH=0,2", "LOWER_NONMATCH=0", "LOWER_NONMATCH=0.9",
            "LOWER_NONMATCH=1", "LOWER_NONMATCH=-0.1", "LOWER_NONMATCH=NaN",
            "LOWER_NONMATCH=0.2extra"
        })
            Reject<ArgumentException>(() => V9GoldLowerNonMatchExperiment.ParseThreshold(bad, .9m));
    }

    [Test]
    public void Lower_only_changes_reason_not_score_candidate_or_status()
    {
        var original = Rejected(.1m);
        var changed = V9GoldLowerNonMatchExperiment.Apply(original, Frame(), Fit, .2m, .9m);
        Multiple(() =>
        {
            Assert.That(changed.Status, Is.EqualTo(ResolutionStatus.NAO_RESOLVIDO));
            Assert.That(changed.MelhorScore, Is.EqualTo(.1m));
            Assert.That(changed.MelhorCandidatoUuid, Is.EqualTo(Candidate));
            Assert.That(changed.PessoaUuidResolvido, Is.Null);
            Assert.That(changed.Motivo, Is.EqualTo(V9GoldLowerNonMatchExperiment.PublicationReason));
            Assert.That(changed.Motivo, Does.Not.StartWith("SEM_CANDIDATO_"));
            Assert.That(original.Motivo, Is.EqualTo("ABAIXO_T_LINKAGE"));
        });
    }

    [Test]
    public void Border_middle_incomplete_conflict_and_existing_link_are_unchanged()
    {
        var low = Rejected(.1m);
        var unchanged = new[] {
            V9GoldLowerNonMatchExperiment.Apply(Rejected(.2m), Frame(), Fit, .2m, .9m),
            V9GoldLowerNonMatchExperiment.Apply(Rejected(.3m), Frame(), Fit, .2m, .9m),
            V9GoldLowerNonMatchExperiment.Apply(low, Frame(index: false), Fit, .2m, .9m),
            V9GoldLowerNonMatchExperiment.Apply(low, Frame(complete: false), Fit, .2m, .9m),
            V9GoldLowerNonMatchExperiment.Apply(low, Frame(passes: false), Fit, .2m, .9m),
            V9GoldLowerNonMatchExperiment.Apply(low with { Status = ResolutionStatus.CONFLITO },
                Frame(), Fit, .2m, .9m),
            V9GoldLowerNonMatchExperiment.Apply(low with {
                Status = ResolutionStatus.RESOLVIDO, PessoaUuidResolvido = Candidate
            }, Frame(), Fit, .2m, .9m)
        };
        Assert.That(unchanged.All(x => x.Motivo == "ABAIXO_T_LINKAGE"), Is.True);
    }

    [Test]
    public void Disposable_dt10_patch_is_explicit_localdb_only_and_fails_on_schema_drift()
    {
        var canonical =
            "CREATE OR ALTER PROCEDURE identidade.sp_publicar_resolucao_progressiva_linkage_lote AS BEGIN " +
            "IF l.raw_motivo NOT LIKE N'SEM_CANDIDATO_%' THROW 51810,'blocked',1; END";
        var patched = V9GoldDisposableLowerProcedurePatch.Apply(canonical);
        // SQL Server 17 LocalDB OBJECT_DEFINITION normalization was measured
        // physically in run #40132062400: ' CREATE   PROCEDURE ...'.
        // Accept that exact known header form, not an unknown or altered guard.
        var normalized = " CREATE   PROCEDURE identidade.sp_publicar_resolucao_progressiva_linkage_lote"
            + canonical["CREATE OR ALTER PROCEDURE identidade.sp_publicar_resolucao_progressiva_linkage_lote".Length..];
        var patchedNormalized = V9GoldDisposableLowerProcedurePatch.Apply(normalized);
        Multiple(() =>
        {
            Assert.That(patchedNormalized, Does.Contain("SERVERPROPERTY(N'IsLocalDB')"));
            Assert.That(patchedNormalized, Does.Contain(V9GoldLowerNonMatchExperiment.PublicationReason));
            Assert.That(patched, Does.Contain("SERVERPROPERTY(N'IsLocalDB')"));
            Assert.That(patched, Does.Contain("Jornada.V9.ExperimentalHarness"));
            Assert.That(patched, Does.Contain(V9GoldLowerNonMatchExperiment.PublicationReason));
            Assert.That(patched, Does.Contain("l.raw_motivo NOT LIKE N'SEM_CANDIDATO_%'"));
        });
        Reject<InvalidDataException>(() => V9GoldDisposableLowerProcedurePatch.Apply(patched));
        Reject<InvalidDataException>(() => V9GoldDisposableLowerProcedurePatch.Apply(
            canonical.Replace("l.raw_motivo NOT LIKE", "l.raw_motivo LIKE", StringComparison.Ordinal)));
        Reject<InvalidDataException>(() => V9GoldDisposableLowerProcedurePatch.Apply(
            normalized.Replace("THROW 51810", "PRINT 51810", StringComparison.Ordinal)));
        Reject<InvalidDataException>(() => V9GoldDisposableLowerProcedurePatch.Apply(
            normalized.Replace("identidade.sp_publicar_resolucao_progressiva_linkage_lote",
                "identidade.sp_other_unknown_procedure", StringComparison.Ordinal)));
        Reject<InvalidDataException>(() => V9GoldDisposableLowerProcedurePatch.Apply(
            canonical.Replace("THROW", "OR l.raw_motivo NOT LIKE N'SEM_CANDIDATO_%' THROW",
                StringComparison.Ordinal)));
    }

    [Test]
    public void Sql_publication_requires_private_database_marker_and_distinct_experimental_reason()
    {
        var sql = ProbabilisticLinkageBatchRunner.ProgressivePublicationSql();
        Multiple(() =>
        {
            Assert.That(sql, Does.Contain("DB_NAME()=N'JornadaHarness'"));
            Assert.That(sql, Does.Contain("Jornada.V9.ExperimentalHarness"));
            Assert.That(sql, Does.Contain("PINNED_FIT_PREVIEW_NO_ACTIVATION"));
            Assert.That(sql, Does.Contain("THROW 51822"));
            Assert.That(sql, Does.Contain(V9GoldLowerNonMatchExperiment.PublicationReason));
            Assert.That(sql, Does.Contain("N'NOVA_IDENTIDADE_LIMIAR_INFERIOR_TRAIN'"));
            Assert.That(sql, Does.Contain("c.raw_motivo LIKE N'SEM_CANDIDATO_%'"));
            Assert.That(sql, Does.Contain("c.ultimo_destino_externo_uuid IS NULL"));
        });
    }
}
