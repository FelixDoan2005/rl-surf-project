"""Training entry point. Runs any registered agent in the env and logs every episode.

    python agent/scripts/train.py --algo random --exe Builds/SnowboardEnv/SnowboardEnv.exe --episodes 20
    python agent/scripts/train.py --algo ppo --config agent/configs/ppo.json --exe Builds/SnowboardEnv/SnowboardEnv.exe --repeat 4

Run it from the repo root. Each run writes results/<run>/ with config.json and episodes.csv (plot_results.py draws
learning curves from those), plus final.pt if the agent's save() writes one. The learning itself is the agent's job
(algos/<yours>.py): this script just calls act() and observe().
"""
import argparse
import csv
import json
import os
import time

from common import RESULTS_DIR, add_env_args, ending_of, make_env  # common also sets up the import path
from algos import make_agent  # noqa: E402


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    add_env_args(ap)
    ap.add_argument("--algo", default="random", help="name registered in algos/__init__.py")
    ap.add_argument("--config", help="json file with hyperparameters (see configs/)")
    ap.add_argument("--episodes", type=int, default=100)
    ap.add_argument("--run-name", help="folder under results/ (default: <algo>_<timestamp>)")
    ap.add_argument("--save-every", type=int, default=0, help="also save a checkpoint every N episodes (0 = only at the end)")
    args = ap.parse_args()

    config = {}
    if args.config:
        with open(args.config) as f:
            config = json.load(f)
    run = args.run_name or f"{args.algo}_{time.strftime('%Y%m%d_%H%M%S')}"
    out = os.path.join(RESULTS_DIR, run)

    env = make_env(args)
    agent = make_agent(args.algo, env, config, args.seed)
    os.makedirs(out, exist_ok=True)
    with open(os.path.join(out, "config.json"), "w") as f:
        json.dump({"args": vars(args), "config": config, "obs_size": env.obs_size,
                   "action_names": env.action_names, "dt": env.dt}, f, indent=2)

    fields = ["episode", "env_steps", "return", "length", "sim_seconds", "finished", "ending", "wall_seconds"]
    t0 = time.time()
    total_steps = 0
    with open(os.path.join(out, "episodes.csv"), "w", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=fields)
        writer.writeheader()
        for ep in range(args.episodes):
            obs, info = env.reset(seed=args.seed if ep == 0 else None)   # seed once, then let it run on
            ret, steps, done = 0.0, 0, False
            while not done:
                action = agent.act(obs)
                next_obs, reward, terminated, truncated, info = env.step(action)
                agent.observe(obs, action, reward, next_obs, terminated, truncated)
                obs = next_obs
                ret += reward
                steps += 1
                done = terminated or truncated
            total_steps += steps
            ending = ending_of(info, truncated)
            writer.writerow({"episode": ep + 1, "env_steps": total_steps, "return": round(ret, 4), "length": steps,
                             "sim_seconds": round(info["episodeTime"], 2), "finished": int(info["finished"]),
                             "ending": ending, "wall_seconds": round(time.time() - t0, 1)})
            f.flush()
            print(f"episode {ep + 1:4d}  return {ret:8.2f}  steps {steps:5d}  {ending}")
            if args.save_every and (ep + 1) % args.save_every == 0:
                agent.save(os.path.join(out, f"checkpoint_{ep + 1}.pt"))
    agent.save(os.path.join(out, "final.pt"))
    env.close()
    print(f"done: {total_steps} env steps in {time.time() - t0:.0f}s -> {out}")


if __name__ == "__main__":
    main()
