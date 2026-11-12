"""Agents live here, one file per algorithm. Register yours in AGENTS so `--algo <name>` finds it."""
from .base import Agent
from .baselines import RandomAgent, StraightAgent

AGENTS = {
    "random": RandomAgent,
    "straight": StraightAgent,
    # TODO(each person): add yours, e.g.
    #   from .ppo import PPOAgent
    #   "ppo": PPOAgent,
}


def make_agent(name, env, config=None, seed=0):
    if name not in AGENTS:
        raise KeyError(f"unknown algo '{name}', known: {sorted(AGENTS)}")
    return AGENTS[name](env, config or {}, seed)
