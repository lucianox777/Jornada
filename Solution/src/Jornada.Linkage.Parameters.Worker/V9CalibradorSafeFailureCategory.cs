namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Finite, fixed provenance tags only. No exception messages, SQL, personal
/// data or stack traces may cross from the physical worker into CI artifacts.
/// </summary>
public static class V9CalibradorSafeFailureCategory
{
    private const string DataKey = "JornadaV9SafeFailureCategory";
    private const string ProbeCheckpointDataKey = "JornadaV9SafeProbeCheckpoint";
    private static readonly HashSet<string> ProbeCheckpoints = new(StringComparer.Ordinal)
    {
        "PROBE_ADMISSION", "PROBE_SIGNATURES", "PROBE_SQL_PREPARE", "PROBE_SQL_MATERIALIZE",
        "PROBE_SQL_READ", "PROBE_SQL_DIGEST", "PROBE_PAIR_SUPPORT", "PROBE_FS_FRAME",
        "PROBE_FS_FIT", "FS_REQUEST_VALIDATE", "FS_TRAIN_FIT", "FS_FIT_VALIDATE",
        "FS_VALIDATION_EVALUATE", "FS_METRICS_VALIDATE", "PROBE_SOURCE_RESOLVE", "PROBE_SNAPSHOT_EXPORT"
    };
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

    /// <summary>Mark a fixed, allowlisted checkpoint, never an arbitrary error message.</summary>
    public static void MarkProbeCheckpoint(Exception error, string checkpoint)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (!ProbeCheckpoints.Contains(checkpoint))
            throw new ArgumentException("Unknown physical probe checkpoint.", nameof(checkpoint));
        if (Classify(error) == PhysicalSqlProbe)
            error.Data[ProbeCheckpointDataKey] = checkpoint;
    }

    /// <summary>
    /// Bounded, non-secret reason for an already-marked physical SQL probe failure.
    /// Only exact, source-controlled literals are recognized. Never return the
    /// exception text, SQL command, arbitrary Data entries or stack trace.
    /// </summary>
    public static string PhysicalSqlProbeReason(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (Classify(error) != PhysicalSqlProbe || error is not InvalidDataException)
            return "NOT_PHYSICAL_SQL_PROBE";
        return error.Message switch
        {
            "Source Rank pareado exige coorte e resultados por identidade." => "SOURCE_RANK_INPUT",
            "Probe físico aceita exclusivamente TRAIN/VALIDATION; TEST ou linha inválida é proibido." => "FRAME_PARTITION",
            "ObservationId duplicado no frame físico." => "DUPLICATE_OBSERVATION",
            "TRAIN e VALIDATION são obrigatórios." => "MISSING_PARTITION",
            "Vazamento de identidade entre TRAIN e VALIDATION." => "IDENTITY_SPLIT_OVERLAP",
            "Ruleset contém feature fora do catálogo do Calibrador." => "UNSUPPORTED_BLOCKING_FEATURE",
            "TF positiva exige referência IBGE publicada/fixada no mesmo frame SQL." => "TF_FRAME_REFERENCE",
            "Contagem da união SQL diverge da leitura do frame." => "SQL_PAIR_COUNT_MISMATCH",
            "M recuperado no SQL excede universo elegível pré-blocking: denominador inconsistente." => "RECOVERED_M_UNIVERSE",
            "Política de suporte da opção diverge da política de execução." => "SUPPORT_POLICY_MISMATCH",
            "Cobertura de identidades SQL não reconcilia." => "SQL_IDENTITY_COVERAGE",
            "União física contaminou identidades entre splits." => "SQL_SPLIT_OVERLAP",
            "Arestas SQL de VALIDATION divergem do censo original." => "VALIDATION_EDGE_MISMATCH",
            "Replay ponta a ponta divergiu do censo ou Resolve congelado." => "END_TO_END_REPLAY_MISMATCH",
            "Universo pré-blocking ausente, duplicado ou inválido." => "PREBLOCKING_UNIVERSE",
            "Candidato SQL fora da partição/universo de identidade-base." => "SQL_CANDIDATE_UNIVERSE",
            "Partição SQL inesperada no digest de candidatos." => "SQL_DIGEST_PARTITION",
            "Aresta física sem orientação canônica." => "SQL_EDGE_ORIENTATION",
            "Digest físico sem arestas de candidatos." => "SQL_EMPTY_EDGE_DIGEST",
            "Fanout físico fora do domínio do adaptador." => "SQL_FANOUT_DOMAIN",
            "TP/FP/abstenção FS não reconciliam com o frame SQL físico." => "SQL_FS_COUNTS",
            _ => error.Data[ProbeCheckpointDataKey] is string checkpoint && ProbeCheckpoints.Contains(checkpoint)
                ? "UNCLASSIFIED_PHYSICAL_SQL_PROBE_AT_" + checkpoint
                : "UNCLASSIFIED_PHYSICAL_SQL_PROBE"
        };
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
