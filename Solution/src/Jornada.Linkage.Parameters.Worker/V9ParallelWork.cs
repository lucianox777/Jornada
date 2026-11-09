namespace Jornada.Linkage.Parameters.Worker;

/// <summary>Bounded independent work; output order is input order, failures cancel peers.</summary>
public static class V9ParallelWork
{
    /// <summary>
    /// Informational capacity comparison for physical statistical reports.
    /// No CPU limits, defaults or statistical choices are changed by this object.
    /// </summary>
    public sealed record CpuCapacityDiagnostic(
        int ConfiguredMaximumParallelRuleSets,
        int? DotNetVisibleLogicalProcessors,
        int EffectiveMaximumParallelRuleSets,
        bool ExplicitWorkerCountBelowDetectedProcessors,
        string? Notice);

    public static CpuCapacityDiagnostic InspectCpuCapacity(
        int configuredWorkers, int? visibleLogicalProcessors = null)
    {
        var effective = ResolveMaximumParallelism(configuredWorkers, visibleLogicalProcessors);
        var visible = visibleLogicalProcessors ?? Environment.ProcessorCount;
        if (visible <= 0)
        {
            // Explicit overrides do not require successful auto-detection.
            // Do not invent a detected CPU count for the report.
            return new(configuredWorkers, null, effective, false,
                "CPUS_NAO_DETECTADAS_CONFIGURACAO_MANUAL");
        }
        var explicitlyBelow = configuredWorkers > 0 && effective < visible;
        return new(configuredWorkers, visible, effective, explicitlyBelow,
            explicitlyBelow ? "PARALELISMO_CONFIGURADO_INFERIOR_AS_CPUS_VISIVEIS_DOTNET" : null);
    }

    /// <summary>
    /// The existing worker count accepts 0 to use all logical processors
    /// available to this .NET process (including host/container CPU quotas).
    /// Explicit positive values remain possible for reproducible experiments.
    /// The OS/.NET scheduler allocates CPU time; this only controls the count
    /// of independent SQL/calibration tasks.
    /// </summary>
    public static int ResolveMaximumParallelism(int configuredWorkers, int? visibleLogicalProcessors = null)
    {
        if (configuredWorkers < 0)
            throw new InvalidDataException("Paralelismo exige 0 para automático ou limite explícito positivo.");
        // Explicit legacy values 1, 2, ... never need CPU autodetection.
        if (configuredWorkers > 0)
            return configuredWorkers;
        var available = visibleLogicalProcessors ?? Environment.ProcessorCount;
        if (available <= 0)
            throw new InvalidDataException(
                "Detecção automática de CPUs indisponível: Environment.ProcessorCount não retornou um número positivo. "
                + "Verifique a máquina/contêiner e configure MaximumParallelRuleSets explicitamente.");
        return available;
    }

    /// <summary>
    /// Only the physical calibration entry points supply a measured governor.
    /// All library/historical callers retain unchanged MapAsync semantics.
    /// </summary>
    public static async Task<IReadOnlyList<TResult>> MapCalibrationAsync<TInput, TResult>(
        IReadOnlyList<TInput> inputs, int configuredParallelism,
        Func<TInput, CancellationToken, Task<TResult>> operation,
        V9CalibrationRuntimeScheduler? resourceScheduler,
        CancellationToken cancellationToken)
    {
        if (resourceScheduler is null)
            return await MapAsync(inputs, configuredParallelism, operation, cancellationToken);
        var (values, _) = await resourceScheduler.MapAsync(inputs, operation, cancellationToken);
        return values;
    }

    public static async Task<IReadOnlyList<TResult>> MapAsync<TInput, TResult>(
        IReadOnlyList<TInput> inputs, int maximumParallelism,
        Func<TInput, CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(operation);
        maximumParallelism = ResolveMaximumParallelism(maximumParallelism);
        var results = new TResult[inputs.Count];
        await Parallel.ForEachAsync(Enumerable.Range(0, inputs.Count),
            new ParallelOptions { MaxDegreeOfParallelism = maximumParallelism, CancellationToken = cancellationToken },
            async (index, ct) => results[index] = await operation(inputs[index], ct));
        return Array.AsReadOnly(results);
    }
}
