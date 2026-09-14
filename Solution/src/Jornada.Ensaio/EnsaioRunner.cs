namespace Jornada.Ensaio;

public sealed class EnsaioRunner(
    EnsaioRuntimeOptions options,
    CheckpointCollector checkpointCollector,
    CalibrationProcessRunner calibrationRunner,
    IngestionStageRunner ingestionRunner,
    ICollection<string> log)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.OutputDirectory);
        var checkpoints = new List<Checkpoint>();
        Checkpoint? previous = null;

        foreach (var stage in EnsaioPlan.Padrao)
        {
            if (options.SingleStage is not null && stage.Codigo != options.SingleStage)
                continue;

            Console.WriteLine($"── {stage.Codigo}  {stage.Descricao}");
            try
            {
                await ExecuteStageAsync(stage, cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   [falha] {ex.Message}");
                log.Add($"{stage.Codigo}: FALHA — {ex.Message}");
            }

            var checkpoint = await checkpointCollector.ColetarAsync(
                stage,
                options.BaselineSha,
                cancellationToken);
            await CheckpointCollector.GravarAsync(
                checkpoint,
                options.OutputDirectory,
                cancellationToken);

            if (previous is not null)
            {
                var diffPath = Path.Combine(
                    options.OutputDirectory,
                    $"diff-{previous.Etapa}--{checkpoint.Etapa}.md");
                await File.WriteAllTextAsync(
                    diffPath,
                    CheckpointDiff.Gerar(previous, checkpoint),
                    cancellationToken);
            }

            checkpoints.Add(checkpoint);
            previous = checkpoint;
        }

        await WriteFinalArtifactsAsync(checkpoints, cancellationToken);
        return 0;
    }

    private Task ExecuteStageAsync(EnsaioEtapa stage, CancellationToken cancellationToken) =>
        stage.Tipo switch
        {
            EnsaioEtapaTipo.Calibrador => calibrationRunner.RunAsync(stage, cancellationToken),
            EnsaioEtapaTipo.Ingestao => ingestionRunner.RunAsync(stage, cancellationToken),
            _ => Task.CompletedTask
        };

    private async Task WriteFinalArtifactsAsync(
        IReadOnlyList<Checkpoint> checkpoints,
        CancellationToken cancellationToken)
    {
        var logPath = Path.Combine(options.OutputDirectory, "ensaio.log");
        await File.WriteAllLinesAsync(logPath, log, cancellationToken);

        var reportPath = Path.Combine(options.OutputDirectory, "RELATORIO_ENSAIO.md");
        await File.WriteAllTextAsync(
            reportPath,
            EnsaioReport.Gerar(checkpoints, log.ToArray()),
            cancellationToken);

        Console.WriteLine($"Relatório interpretativo: {reportPath}");
    }
}
