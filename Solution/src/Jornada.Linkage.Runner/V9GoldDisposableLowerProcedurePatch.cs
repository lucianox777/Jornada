using System.Text.RegularExpressions;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Private SQL surgery ONLY for an already guarded, empty, disposable Gold
/// TRAIN LocalDB. Leaves the canonical migration and every operational
/// database unchanged. Refuses any unrecognized procedure definition.
/// </summary>
public static class V9GoldDisposableLowerProcedurePatch
{
    public const string ProcedureName = "identidade.sp_publicar_resolucao_progressiva_linkage_lote";
    private const string OldGuard = "l.raw_motivo NOT LIKE N'SEM_CANDIDATO_%'";
    // SqlLocalDB/SQL Server 17 normalizes CREATE OR ALTER PROCEDURE into
    // ' CREATE   PROCEDURE ...' in OBJECT_DEFINITION. The actual disposable
    // schema run #40132062400 proved the exact guard occurs once and THROW
    // 51810 remains present; the previous literal header check was too strict.
    // Anchor the known procedure name and accept ONLY whitespace variation
    // or SQL Server's omission of OR ALTER, without relaxing the DT-10 guard.
    private static readonly Regex ExpectedDeclaration = new(
        @"\A\s*CREATE\s+(?:OR\s+ALTER\s+)?PROCEDURE\s+identidade\.sp_publicar_resolucao_progressiva_linkage_lote(?=\s)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    public static string Apply(string canonicalDefinition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalDefinition);
        if (!ExpectedDeclaration.IsMatch(canonicalDefinition)
            || canonicalDefinition.Split(OldGuard, StringSplitOptions.None).Length != 2
            || !canonicalDefinition.Contains("THROW 51810", StringComparison.OrdinalIgnoreCase)
            || canonicalDefinition.Contains(V9GoldLowerNonMatchExperiment.PublicationReason, StringComparison.Ordinal))
            throw new InvalidDataException("Procedure DT-10 desconhecida ou já alterada: experimento recusado.");

        // Do not modify the existing no-candidate case or any source data. The
        // new reason is accepted only under the same isolated DB ownership marker
        // that the C# harness checks before publication.
        const string alternative = """
            (l.raw_motivo NOT LIKE N'SEM_CANDIDATO_%'
                AND NOT (l.raw_motivo=N'LIMIAR_INFERIOR_GOLD_TRAIN_EXPERIMENTAL'
                    AND DB_NAME()=N'JornadaHarness'
                    AND CONVERT(int,SERVERPROPERTY(N'IsLocalDB'))=1
                    AND EXISTS (SELECT 1 FROM sys.extended_properties
                        WHERE class=0 AND name=N'Jornada.EnvironmentProfile'
                          AND CONVERT(nvarchar(200),value)=N'Development')
                    AND EXISTS (SELECT 1 FROM sys.extended_properties
                        WHERE class=0 AND name=N'Jornada.V9.ExperimentalHarness'
                          AND CONVERT(nvarchar(200),value)=N'PINNED_FIT_PREVIEW_NO_ACTIVATION')))
            """;
        return canonicalDefinition.Replace(OldGuard, alternative, StringComparison.Ordinal);
    }
}
