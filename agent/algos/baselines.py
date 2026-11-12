"""Baselines that don't learn. Handy for comparing against and for testing the scripts."""
import numpy as np

from .base import Agent


class RandomAgent(Agent):
    """Random actions: the random-agent baseline."""

    def __init__(self, env, config=None, seed=0):
        super().__init__(env, config, seed)
        env.action_space.seed(seed)

    def act(self, obs, deterministic=False):
        return self.env.action_space.sample()


class StraightAgent(Agent):
    """No input at all, it just rides straight down. A 'does nothing' baseline."""

    def act(self, obs, deterministic=False):
        space = self.env.action_space
        if hasattr(space, "n"):          # a discrete env (e.g. CartPole): always pick action 0
            return 0
        return np.zeros(space.shape, dtype=np.float32)
