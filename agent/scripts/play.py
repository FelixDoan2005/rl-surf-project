"""Watch an agent at normal speed (for the demo video). Opens the game window and paces it in real time.

    python agent/scripts/play.py --algo straight --exe Builds/SnowboardEnv/SnowboardEnv.exe
    python agent/scripts/play.py --algo ppo --checkpoint results/ppo_x/final.pt --exe Builds/SnowboardEnv/SnowboardEnv.exe

Leave out --exe to use a game that's already running (Unity editor: press Play, then menu Surf > Env Server > Start).
Record the window with OBS or the Windows Game Bar. For the before/after video run it once with the untrained
agent (or --algo random) and once with the trained checkpoint. Run it from the repo root.
"""
import argparse
import json

from common import add_env_args, ending_of, make_env  # common also sets up the import path
from algos import make_agent  # noqa: E402


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    add_env_args(ap)
    ap.add_argument("--algo", default="straight")
    ap.add_argument("--config", help="json file with hyperparameters")
    ap.add_argument("--checkpoint", help="weights file written by the agent's save()")
    ap.add_argument("--episodes", type=int, default=5, help="0 = keep going until you stop it")
    args = ap.parse_args()

    config = {}
    if args.config:
        with open(args.config) as f:
            config = json.load(f)
    env = make_env(args, realtime=True, graphics=True)
    agent = make_agent(args.algo, env, config, args.seed)
    if args.checkpoint:
        agent.load(args.checkpoint)

    ep = 0
    try:
        while args.episodes == 0 or ep < args.episodes:
            obs, info = env.reset(seed=args.seed + ep)
            ret, done = 0.0, False
            while not done:
                obs, reward, terminated, truncated, info = env.step(agent.act(obs, deterministic=True))
                ret += reward
                done = terminated or truncated
            ep += 1
            print(f"episode {ep}: return {ret:.2f}  {ending_of(info, terminated, truncated)}")
    except KeyboardInterrupt:
        pass
    env.close()


if __name__ == "__main__":
    main()
