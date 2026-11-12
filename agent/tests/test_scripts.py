"""train.py / evaluate.py / plot_results.py end to end on CartPole (no Unity needed), including the
flow a teammate follows: write an Agent, register it, train it, save it, evaluate it from the checkpoint."""
import csv
import json
import os
import sys

import evaluate
import plot_results
import train
from algos import AGENTS, Agent


def run(module, argv, monkeypatch):
    monkeypatch.setattr(sys, "argv", [module.__name__ + ".py"] + argv)
    module.main()


def read_episodes(path):
    with open(path) as f:
        return list(csv.DictReader(f))


def test_train_logs_every_episode(tmp_path, monkeypatch):
    monkeypatch.setattr(train, "RESULTS_DIR", str(tmp_path))
    run(train, ["--env", "CartPole-v1", "--algo", "random", "--episodes", "3", "--run-name", "r"], monkeypatch)
    rows = read_episodes(tmp_path / "r" / "episodes.csv")
    assert len(rows) == 3
    assert {"episode", "env_steps", "return", "length", "ending"} <= set(rows[0])
    assert int(rows[-1]["env_steps"]) == sum(int(r["length"]) for r in rows)      # the running step count adds up
    with open(tmp_path / "r" / "config.json") as f:
        assert json.load(f)["args"]["algo"] == "random"


def test_a_teammates_algorithm_runs_through_train_and_evaluate(tmp_path, monkeypatch):
    seen = {"observe": 0, "loaded": None}

    class MyAgent(Agent):
        def act(self, obs, deterministic=False):
            return 0
        def observe(self, obs, action, reward, next_obs, terminated, truncated):
            seen["observe"] += 1
        def save(self, path):
            with open(path, "w") as f:
                f.write("weights")
        def load(self, path):
            with open(path) as f:
                seen["loaded"] = f.read()

    monkeypatch.setitem(AGENTS, "mine", MyAgent)                   # the one line a teammate adds in algos/__init__.py
    monkeypatch.setattr(train, "RESULTS_DIR", str(tmp_path))
    run(train, ["--env", "CartPole-v1", "--algo", "mine", "--episodes", "2", "--run-name", "m", "--save-every", "1"], monkeypatch)
    rows = read_episodes(tmp_path / "m" / "episodes.csv")
    assert seen["observe"] == sum(int(r["length"]) for r in rows)  # observe() was called on every single step
    assert (tmp_path / "m" / "final.pt").exists() and (tmp_path / "m" / "checkpoint_1.pt").exists()

    out = tmp_path / "eval.json"
    run(evaluate, ["--env", "CartPole-v1", "--algo", "mine", "--checkpoint", str(tmp_path / "m" / "final.pt"),
                   "--episodes", "2", "--out", str(out)], monkeypatch)
    assert seen["loaded"] == "weights"                              # the checkpoint was loaded back
    with open(out) as f:
        summary = json.load(f)
    assert summary["episodes"] == 2 and summary["env"] == "CartPole-v1" and "return_mean" in summary


def test_evaluate_uses_the_same_seeds_every_time(tmp_path, monkeypatch):
    results = []
    for i in range(2):
        out = tmp_path / f"e{i}.json"
        run(evaluate, ["--env", "CartPole-v1", "--algo", "straight", "--episodes", "3", "--seed", "5", "--out", str(out)], monkeypatch)
        with open(out) as f:
            results.append(json.load(f)["return_mean"])
    assert results[0] == results[1]


def test_plot_results_writes_a_png(tmp_path, monkeypatch):
    monkeypatch.setattr(train, "RESULTS_DIR", str(tmp_path))
    for name in ("a", "b"):
        run(train, ["--env", "CartPole-v1", "--algo", "random", "--episodes", "4", "--run-name", name], monkeypatch)
    png = tmp_path / "curves.png"
    run(plot_results, [str(tmp_path / "a"), str(tmp_path / "b"), "--window", "2", "--out", str(png)], monkeypatch)
    assert png.exists() and os.path.getsize(png) > 1000
