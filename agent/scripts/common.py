"""Bits shared by train.py / evaluate.py / play.py: the env options and how to build the env."""
import os
import sys

AGENT_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))   # agent/ (has envs/ and algos/)
sys.path.insert(0, AGENT_DIR)
import gymnasium as gym  # noqa: E402
from envs.snowboard_env import SnowboardEnv  # noqa: E402

ROOT = os.path.dirname(AGENT_DIR)   # repo root
RESULTS_DIR = os.path.join(ROOT, "results")                          # git-ignored


def add_env_args(ap):
    ap.add_argument("--env", default="snowboard",
                    help="'snowboard' (our Unity game) or any Gymnasium id such as CartPole-v1, to try an algorithm on a standard env first. "
                         "The options below (exe, port, mode, repeat) only apply to snowboard.")
    ap.add_argument("--exe", help="path to SnowboardEnv.exe (leave out to connect to the Unity editor / a game that is already running)")
    ap.add_argument("--port", type=int, default=5555)
    ap.add_argument("--mode", choices=["high", "low"], default="high", help="high = steer/tuck/brake/jump, low = raw joints")
    ap.add_argument("--repeat", type=int, default=1, help="physics steps (0.01 s each) per env step")
    ap.add_argument("--seed", type=int, default=0)


def make_env(args, **overrides):
    if args.env != "snowboard":
        return gym.make(args.env)
    kw = dict(exe=args.exe, port=args.port, mode=args.mode, action_repeat=args.repeat)
    kw.update(overrides)
    return SnowboardEnv(**kw)


def ending_of(info, terminated, truncated):
    """Short label for how an episode ended."""
    if info.get("finished"):
        return "finished"
    if truncated:
        return "timeout"
    if terminated:
        return (info.get("failReason") or "").split(" (")[0] or "terminated"
    return "running"
