# V9 — exploratory append (incremental, no activation)

This first increment implements **append-only materialization**, not the search engine.
The existing V3 calibrator still performs physical TRAIN exploration and produces
candidate evidence. The command below creates a **new** derived plan, leaving the
approved input byte-for-byte unchanged:

```bash
python3 scripts/v9-exploratory-append.py plan.json candidates.json plan.exploratory.json
```

Candidate input format:

```json
{
  "Candidates": [{
    "ModelOption": {
      "OptionId": "EXP_001",
      "SmoothingAlpha": 0.5,
      "DecisionThreshold": 0.95,
      "TfWeight": 0,
      "EligibleForSelection": false,
      "SharedSurnameMode": 0,
      "UnpublishedTermPolicy": 1
    },
    "Evidence": {
      "Status": "VALIDATED_TRAIN",
      "Recall": 0.98,
      "FdrUpper": 0.002,
      "BaselineRecall": 0.97,
      "BaselineFdrUpper": 0.003,
      "StudyFingerprintSha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
    }
  }]
}
```

Numbers above are **illustrative**, not measured. The tool checks that the
submitted metrics strictly Pareto-dominate the submitted baseline, but **does not
independently authenticate the evidence**. Do not treat this as statistical
certification. It refuses duplicate IDs/parameter sets, active TF without
abstention, and output paths equal to the source or existing files. It retains
the old ModelOptions and appends only `EligibleForSelection=false` options.
It updates the study fingerprint and records source/candidate SHA-256 provenance.

**Not yet implemented:** `--exploratory` search inside the C# worker,
physical comparisons, evidence authenticity verification, multiple-comparison
adjustment, automatic generation of `candidates.json`, or activation.
M/U method and blocking modifications are *not* representable by adding
ModelOptions alone; those require separate versioned study configurations.
