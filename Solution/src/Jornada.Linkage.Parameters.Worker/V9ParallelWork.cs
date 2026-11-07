namespace Jornada.Linkage.Parameters.Worker;

/// <summary>Bounded independent work; output order is input order, failures cancel peers.</summary>
public static class V9ParallelWork
{
    /// <summary>
    /// The existing worker count accepts 0 to use all logical processors
    /// available to this .NET process (including host/container CPU quotas).
    /// Explicit positive values remain possible for reproducible experiments.
    /// The OS/.NET scheduler allocates CPU time; this only controls the count
    /// of independent SQL/calibration tasks.
    /// </summary>
    public static int ResolveMaximumParallelism(int configuredWorkers, int? visibleLogicalProcessors = null)
    {
        if (configuredWorkers < 0 || visibleLogicalProcessors is < 1)
            throw new InvalidDataException("Paralelismo exige 0 para automático ou limite explícito positivo.");
        var available = visibleLogicalProcessors ?? Environment.ProcessorCount;
        return configuredWorkers == 0 ? Math.Max(1, available) : configuredWorkers;
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
