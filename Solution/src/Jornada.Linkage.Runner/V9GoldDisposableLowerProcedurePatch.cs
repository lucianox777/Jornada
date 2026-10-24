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
    private const string ExpectedDeclaration =
        "CREATE OR ALTER PROCEDURE identidade.sp_publicar_resolucao_progressiva_linkage_lote";

    public static string Apply(string canonicalDefinition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalDefinition);
        var validDeclaration = canonicalDefinition.Contains(ExpectedDeclaration, StringComparison.OrdinalIgnoreCase);
        var literalGuardCount = canonicalDefinition.Split(OldGuard, StringSplitOptions.None).Length - 1;
        var hasExperimentalReason = canonicalDefinition.Contains(
            V9GoldLowerNonMatchExperiment.PublicationReason, StringComparison.OrdinalIgnoreCase);
        if (!validDeclaration || literalGuardCount != 1 || hasExperimentalReason)
        {
            // Diagnostic metadata ONLY; never print procedure text or patch
            // an unrecognized definition. A physically provisioned LocalDB
            // smoke distinguishes SQL metadata formatting from schema drift.
            throw new InvalidDataException(
                "Procedure DT-10 desconhecida ou já alterada: experimento recusado. "
                + $"declaration={validDeclaration}; guardCount={literalGuardCount}; "
                + $"experimentalMarker={hasExperimentalReason}.");
        }

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
