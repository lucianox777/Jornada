namespace Jornada.Linkage.Parameters.Worker;

/// <summary>Bounded independent work; output order is input order, failures cancel peers.</summary>
public static class V9ParallelWork
{
    /// <summary>
    /// Optional per-host CPU quota. A declared maximum remains a hard cap;
    /// no quota field preserves exact legacy fixed-worker behavior. The .NET
    /// processor count is the logical/vCPU capacity visible to this process.
    /// Floor rounding leaves spare capacity and never schedules zero workers.
    /// Memory, SQL resources and the number of work items can constrain actual speed.
    /// </summary>
    public static int ResolveMaximumParallelism(int maximumParallelRuleSets,
        int? parallelCpuPercent = null, int? visibleLogicalProcessors = null)
    {
        if (maximumParallelRuleSets < 1 || parallelCpuPercent is < 1 or > 100
            || visibleLogicalProcessors is < 1)
            throw new InvalidDataException("Paralelismo CPU exige limite positivo, percentual de 1 a 100 e CPUs visíveis positivas.");
        if (parallelCpuPercent is null) return maximumParallelRuleSets;
        var logicalCpus = visibleLogicalProcessors ?? Environment.ProcessorCount;
        // long prevents overflow for unusual runtime-reported processor counts.
        var percentWorkers = Math.Max(1, (int)((long)logicalCpus * parallelCpuPercent.Value / 100));
        return Math.Min(maximumParallelRuleSets, percentWorkers);
    }

    public static async Task<IReadOnlyList<TResult>> MapAsync<TInput, TResult>(
        IReadOnlyList<TInput> inputs, int maximumParallelism,
        Func<TInput, CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumParallelism);
        var results = new TResult[inputs.Count];
        await Parallel.ForEachAsync(Enumerable.Range(0, inputs.Count),
            new ParallelOptions { MaxDegreeOfParallelism = maximumParallelism, CancellationToken = cancellationToken },
            async (index, ct) => results[index] = await operation(inputs[index], ct));
        return Array.AsReadOnly(results);
    }
}
