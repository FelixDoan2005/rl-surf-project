"""The shape every agent has, so train.py / evaluate.py / play.py work with any algorithm.

This file is only the interface, there's no learning in here. Put your PPO / SAC / DQN etc. in its own file
in this folder, make it extend Agent, and register it in algos/__init__.py.
"""


class Agent:
    def __init__(self, env, config=None, seed=0):
        self.env = env              # the SnowboardEnv (has observation_space / action_space)
        self.config = config or {}  # hyperparameters from the json file in configs/
        self.seed = seed

    def act(self, obs, deterministic=False):
        """Return an action for env.action_space. deterministic=True is used when evaluating (no exploration noise)."""
        raise NotImplementedError

    def observe(self, obs, action, reward, next_obs, terminated, truncated):
        """Called after every env step. Store the transition and/or learn from it here, however your algorithm works."""

    def save(self, path):
        """Write weights / a checkpoint to `path`. (*.pt and *.onnx are git-ignored, share them another way.)"""

    def load(self, path):
        """Load what save() wrote."""
