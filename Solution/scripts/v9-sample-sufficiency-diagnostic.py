#!/usr/bin/env python3
"""Conservative pre-registered sample-size diagnostics; not a certification."""
import math

def assess(m_eligible, m_only_baseline, m_only_variant, minimum_detectable_delta=0.02,
           confidence_z=1.96, minimum_independent_identities=100):
    if m_eligible < 0 or min(m_only_baseline, m_only_variant) < 0:
        raise ValueError("counts must be nonnegative")
    if m_only_baseline + m_only_variant > m_eligible:
        raise ValueError("discordant counts exceed eligible M")
    if not 0 < minimum_detectable_delta < 1:
        raise ValueError("delta must be between zero and one")
    if m_eligible == 0:
        return {"status": "NAO_AVALIAVEL", "reason": "zero eligible M"}
    difference = (m_only_variant - m_only_baseline) / m_eligible
    discordance = (m_only_variant + m_only_baseline) / m_eligible
    # Naive paired-pair variance is a LOWER-BAR diagnostic only; pairs sharing identity are dependent.
    variance = max(0, discordance - difference * difference) / m_eligible
    naive_half_width = confidence_z * math.sqrt(variance)
    return {"status": "INCONCLUSIVO", "paired_delta": difference,
            "discordant_fraction": discordance, "naive_pair_half_width": naive_half_width,
            "minimum_detectable_delta": minimum_detectable_delta,
            "minimum_independent_identities": minimum_independent_identities,
            "reason": "requires identity-clustered interval, stratum evidence and pre-registered power before certification"}
