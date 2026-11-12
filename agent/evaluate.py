"""Evaluate an agent on fixed seeds, so every agent gets the same start positions and the numbers are comparable.

    python agent/evaluate.py --algo straight --exe Builds/SnowboardEnv/SnowboardEnv.exe --episodes 20
    python agent/evaluate.py --algo ppo --checkpoint results/ppo_x/final.pt --exe Builds/SnowboardEnv/SnowboardEnv.exe

Uses act(obs, deterministic=True) and never calls observe(), so nothing is learned here. Run it from the repo root.
"""
import argparse
import json

import numpy as np

from algos import make_agent
from common import add_env_args, ending_of, make_env


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    add_env_args(ap)
    ap.add_argument("--algo", default="random")
    ap.add_argument("--config", help="json file with hyperparameters (same one you trained with)")
    ap.add_argument("--checkpoint", help="weights file written by the agent's save()")
    ap.add_argument("--episodes", type=int, default=20)
    ap.add_argument("--out", help="also write the summary to this json file")
    args = ap.parse_args()

    config = {}
    if args.config:
        with open(args.config) as f:
            config = json.load(f)
    env = make_env(args)
    agent = make_agent(args.algo, env, config, args.seed)
    if args.checkpoint:
        agent.load(args.checkpoint)

    returns, seconds, endings = [], [], {}
    for ep in range(args.episodes):
        obs, info = env.reset(seed=args.seed + ep)          # same seeds for every agent
        ret, done = 0.0, False
        while not done:
            obs, reward, terminated, truncated, info = env.step(agent.act(obs, deterministic=True))
            ret += reward
            done = terminated or truncated
        returns.append(ret)
        seconds.append(info["episodeTime"])
        e = ending_of(info, truncated)
        endings[e] = endings.get(e, 0) + 1
    env.close()

    summary = {"algo": args.algo, "episodes": args.episodes, "seed": args.seed, "mode": args.mode, "repeat": args.repeat,
               "return_mean": float(np.mean(returns)), "return_std": float(np.std(returns)),
               "sim_seconds_mean": float(np.mean(seconds)), "finish_rate": endings.get("finished", 0) / args.episodes,
               "endings": endings}
    print(json.dumps(summary, indent=2))
    if args.out:
        with open(args.out, "w") as f:
            json.dump(summary, f, indent=2)


if __name__ == "__main__":
    main()
