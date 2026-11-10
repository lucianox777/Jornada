namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Finite, fixed provenance tags only. No exception messages, SQL, personal
/// data or stack traces may cross from the physical worker into CI artifacts.
/// </summary>
public static class V9CalibradorSafeFailureCategory
{
    private const string DataKey = "JornadaV9SafeFailureCategory";
    public const string PhysicalSqlFrame = "PHYSICAL_SQL_FRAME";
    public const string PhysicalSqlProbe = "PHYSICAL_SQL_PROBE";
    public const string PhysicalFrozenHoldout = "PHYSICAL_FROZEN_HOLDOUT";
    public const string PhysicalReplayMismatch = "PHYSICAL_REPLAY_MISMATCH";
    public const string PhysicalFrameOrder = "PHYSICAL_FRAME_ORDER";
    public const string FrozenTfReference = "FROZEN_TF_REFERENCE";
    public const string Unclassified = "UNCLASSIFIED";
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        PhysicalSqlFrame, PhysicalSqlProbe, PhysicalFrozenHoldout, PhysicalReplayMismatch,
        PhysicalFrameOrder, FrozenTfReference
    };

    public static void Mark(Exception error, string category)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (!Allowed.Contains(category))
            throw new ArgumentException("Unknown safe category.", nameof(category));
        // Preserve the innermost, most precise known phase when outer
        // physical-frame boundaries catch and rethrow the same exception.
        if (error.Data[DataKey] is string previous && Allowed.Contains(previous))
            return;
        error.Data[DataKey] = category;
    }

    public static string Classify(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (error.Data[DataKey] is string category && Allowed.Contains(category))
            return category;
        // Exact in-source constant matches only; never print e.Message.
        if (error is InvalidDataException)
            return error.Message switch
            {
                "V9_RESOURCE_SQL_POOL_INVALID" or
                "V9_RESOURCE_SQL_CONNECTION_CAPACITY" => "RESOURCE_SQL_POOL",
                "V9_RESOURCE_RUNTIME_DISK_BELOW_RESERVE" => "RESOURCE_RUNTIME_DISK",
                "V9_RESOURCE_RUNTIME_RAM_BELOW_ONE_WORKER" or
                "V9_RESOURCE_RUNTIME_RAM_NOT_ADMISSIBLE" or
                "V9_RESOURCE_EXPLICIT_RAM_UNMEASURED" or
                "V9_RESOURCE_EXPLICIT_WORKERS_EXCEED_MEASURED_RAM"
                    => "RESOURCE_RUNTIME_RAM",
                "V9_SQL_COST_ORDER_EMPTY_FRAME" or
                "V9_SQL_COST_ORDER_INVALID_PASS" or
                "V9_SQL_COST_ORDER_MISSING_STABLE_KEY" or
                "V9_SQL_COST_ORDER_DUPLICATE_STABLE_KEY" => PhysicalFrameOrder,
                _ => Unclassified
            };
        return Unclassified;
    }
}
