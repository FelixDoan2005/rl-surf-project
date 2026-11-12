"""Speed test and baseline for the Unity snowboard environment.

Runs a policy that needs no learning and reports how fast the environment is (steps per second) and how it scores:

    python agent/speed_test.py --exe Builds/SnowboardEnv/SnowboardEnv.exe --episodes 20          # random actions
    python agent/speed_test.py --exe Builds/SnowboardEnv/SnowboardEnv.exe --policy straight       # just ride straight
    python agent/speed_test.py --port 5555                                                        # game already running (editor)

--policy random is the random-agent baseline the coursework asks for; --policy straight (no input at all) is a
useful "does nothing" baseline. Use --seed so the start positions are identical between runs.
"""
import argparse
import collections
import os
import sys
import time

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from envs.snowboard_env import SnowboardEnv  # noqa: E402


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--exe", help="path to SnowboardEnv.exe (omit to connect to a game that is already running)")
    ap.add_argument("--port", type=int, default=5555)
    ap.add_argument("--mode", choices=["high", "low"], default="high")
    ap.add_argument("--repeat", type=int, default=1, help="physics steps (0.01 s) per env step")
    ap.add_argument("--episodes", type=int, default=20)
    ap.add_argument("--policy", choices=["random", "straight"], default="random")
    ap.add_argument("--seed", type=int, default=0)
    ap.add_argument("--realtime", action="store_true", help="pace to the wall clock (for watching); use with --graphics")
    ap.add_argument("--graphics", action="store_true", help="open a window instead of running headless")
    args = ap.parse_args()

    env = SnowboardEnv(exe=args.exe, port=args.port, mode=args.mode, action_repeat=args.repeat,
                       realtime=args.realtime, graphics=args.graphics)
    rng = np.random.default_rng(args.seed)
    env.action_space.seed(args.seed)
    print(f"connected: mode={env.mode} obs={env.obs_size} actions={env.action_names} dt={env.dt}s repeat={env.action_repeat}")

    returns, lengths, seconds, finished = [], [], [], 0
    reasons = collections.Counter()
    total_steps = 0
    t0 = time.perf_counter()
    for ep in range(args.episodes):
        obs, info = env.reset(seed=args.seed + ep)
        ret, steps, done = 0.0, 0, False
        while not done:
            if args.policy == "straight":
                action = np.zeros(env.action_size, dtype=np.float32)
            else:
                action = env.action_space.sample()
            obs, reward, terminated, truncated, info = env.step(action)
            ret += reward
            steps += 1
            done = terminated or truncated
        total_steps += steps
        returns.append(ret)
        lengths.append(steps)
        seconds.append(info["episodeTime"])
        if info["finished"]:
            finished += 1
            reasons["finished"] += 1
        else:
            reasons[("timeout" if truncated else (info["failReason"].split(" (")[0] or "failed"))] += 1
        print(f"  episode {ep + 1:3d}: return {ret:8.2f}  steps {steps:5d}  time {info['episodeTime']:5.1f}s  z {info['z']:6.1f}"
              f"  {'FINISHED' if info['finished'] else info['failReason']}")
    wall = time.perf_counter() - t0
    env.close()

    physics_steps = total_steps * args.repeat
    print("\n=== summary ===")
    print(f"policy {args.policy}, mode {args.mode}, repeat {args.repeat}, {args.episodes} episodes, seed {args.seed}")
    print(f"env steps/s        {total_steps / wall:10.0f}")
    print(f"physics steps/s    {physics_steps / wall:10.0f}   (= {physics_steps * env.dt / wall:.1f}x real time)")
    print(f"wall time          {wall:10.1f} s for {total_steps} env steps")
    print(f"return             {np.mean(returns):10.2f} +- {np.std(returns):.2f}")
    print(f"episode length     {np.mean(seconds):10.1f} s simulated  ({np.mean(lengths):.0f} env steps)")
    print(f"finish rate        {finished / args.episodes:10.0%}")
    print("endings            " + ", ".join(f"{k}: {v}" for k, v in reasons.most_common()))


if __name__ == "__main__":
    main()
