"""The agent interface and the baselines, on standard Gymnasium envs (no Unity needed)."""
import gymnasium as gym
import pytest

from algos import AGENTS, Agent, make_agent


def test_baselines_are_registered():
    assert {"random", "straight"} <= set(AGENTS)


def test_unknown_algo_is_a_clear_error():
    with pytest.raises(KeyError, match="unknown algo"):
        make_agent("nope", gym.make("CartPole-v1"))


def test_agent_interface_has_all_four_methods():
    for name in ("act", "observe", "save", "load"):
        assert callable(getattr(Agent, name))


@pytest.mark.parametrize("env_id", ["CartPole-v1", "Pendulum-v1"])      # one discrete, one continuous
@pytest.mark.parametrize("algo", ["random", "straight"])
def test_baselines_give_valid_actions(algo, env_id):
    env = gym.make(env_id)
    agent = make_agent(algo, env, {}, 0)
    obs, _ = env.reset(seed=0)
    for _ in range(5):
        action = agent.act(obs)
        assert env.action_space.contains(action)
        obs, _, terminated, truncated, _ = env.step(action)
        if terminated or truncated:
            obs, _ = env.reset()
