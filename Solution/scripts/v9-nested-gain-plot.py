#!/usr/bin/env python3
"""Export observed batch gains/resolutions. A heuristic stopping record, not optimality proof."""
import argparse
import json
from pathlib import Path


def plot(source, destination):
    import matplotlib
    matplotlib.use("Agg")
    matplotlib.rcParams["svg.hashsalt"] = "JORNADA_V9_NESTED_GAIN_PLOT_V1"
    import matplotlib.pyplot as plt
    if destination.exists():
        raise FileExistsError(destination)
    if destination.suffix.lower() != ".svg":
        raise ValueError("Standalone SVG destination required")
    rows = json.loads(source.read_bytes())
    if not rows:
        raise ValueError("Batch history required")
    x = [r["Batch"] for r in rows]
    fig, axes = plt.subplots(3, 1, figsize=(9, 7), sharex=True, layout="constrained")
    for axis, gain, resolution, label in [(axes[0], "MaximumRecallGain", "RecallResolution", "Recall"),
            (axes[1], "MaximumFdrGain", "FdrResolution", "FDR")]:
        axis.plot(x, [100 * r[gain] for r in rows], "o-", label="Ganho observado")
        axis.plot(x, [None if r[resolution] is None else 100 * r[resolution] for r in rows], "s--", label="Maior resolução δ das comparações")
        axis.set_ylabel(label + " (p.p.)")
        axis.grid(alpha=.25)
        axis.legend(loc="upper right", fontsize=8)
    axes[2].plot(x, [r["MaximumGainInResolutionUnits"] for r in rows], "o-", label="Maior ganho / δ pareado")
    axes[2].axhline(1, linestyle="--", color="gray", label="Resolução de uma unidade")
    axes[2].set_ylabel("Ganho / δ")
    axes[2].set_xlabel("Lote")
    axes[2].set_xticks(x)
    axes[2].grid(alpha=.25)
    axes[2].legend(loc="upper right", fontsize=8)
    fig.suptitle("V9 — parada heurística no TRAIN-seleção\nNão comprova ótimo global ou independência estatística", fontsize=12)
    fig.savefig(destination, metadata={"Date": None})
    plt.close(fig)
    destination.write_bytes(("\n".join(line.rstrip() for line in destination.read_text(encoding="utf-8").splitlines()) + "\n").encode("utf-8"))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("gain_by_batch", type=Path)
    parser.add_argument("new_svg", type=Path)
    args = parser.parse_args()
    plot(args.gain_by_batch, args.new_svg)
