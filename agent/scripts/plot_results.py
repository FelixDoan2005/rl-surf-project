"""Learning curves from the episodes.csv files that train.py writes.

    python agent/scripts/plot_results.py results/ppo_a results/sac_a --out docs/results/curves.png
    python agent/scripts/plot_results.py results/ppo_a results/ppo_b --x episodes --window 50

Each folder given is one curve (label = folder name): return per episode, smoothed with a moving average.
"""
import argparse
import csv
import os

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402
import numpy as np  # noqa: E402


def load(run_dir):
    with open(os.path.join(run_dir, "episodes.csv")) as f:
        rows = list(csv.DictReader(f))
    return (np.array([float(r["episode"]) for r in rows]), np.array([float(r["env_steps"]) for r in rows]),
            np.array([float(r["return"]) for r in rows]))


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("runs", nargs="+", help="result folders, each containing episodes.csv")
    ap.add_argument("--x", choices=["steps", "episodes"], default="steps")
    ap.add_argument("--window", type=int, default=20, help="moving-average window (episodes)")
    ap.add_argument("--out", default="results/curves.png")
    args = ap.parse_args()

    plt.figure(figsize=(8, 4.5))
    for run in args.runs:
        episodes, steps, returns = load(run)
        w = max(1, min(args.window, len(returns)))
        smooth = np.convolve(returns, np.ones(w) / w, mode="valid")
        x = (steps if args.x == "steps" else episodes)[w - 1:]
        plt.plot(x, smooth, label=os.path.basename(os.path.normpath(run)))
    plt.xlabel("environment steps" if args.x == "steps" else "episodes")
    plt.ylabel(f"return (moving average, {args.window} episodes)")
    plt.legend()
    plt.grid(alpha=0.3)
    plt.tight_layout()
    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    plt.savefig(args.out, dpi=150)
    print("saved", args.out)


if __name__ == "__main__":
    main()
