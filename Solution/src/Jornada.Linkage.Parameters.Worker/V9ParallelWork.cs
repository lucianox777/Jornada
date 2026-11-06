namespace Jornada.Linkage.Parameters.Worker;

/// <summary>Bounded independent work; output order is input order, failures cancel peers.</summary>
public static class V9ParallelWork
{
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
