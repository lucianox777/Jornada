"""Diagnostic guard for V9 published surname TF coverage."""

def assess_coverage(snapshot_manifest):
    required = ("projection_version", "frequency_scope", "frozen")
    if any(key not in snapshot_manifest for key in required):
        raise ValueError("incomplete snapshot manifest")
    return {
        "projection_version": snapshot_manifest["projection_version"],
        "surname_published": False,
        "status": "NOT_VERIFIED",
    }
