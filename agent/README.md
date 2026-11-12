# agent/ — Python side

The game is the environment, this folder is the brain + the scripts around it. Run everything from the repo root.

## What's where
| Path | What it is |
|---|---|
| `envs/snowboard_env.py` | The Gymnasium environment (talks to the Unity game). Shouldn't need touching. |
| `algos/` | One file per algorithm (PPO, SAC, DQN...). `base.py` is the shape an agent has, `baselines.py` has a random agent and a "ride straight" agent. |
| `configs/` | Hyperparameters, one json per experiment. |
| `train.py` | Runs an agent in the env and logs every episode to `results/<run>/episodes.csv`. |
| `evaluate.py` | Runs an agent on fixed seeds (same start positions for everyone) and prints the numbers. |
| `play.py` | Watch an agent at normal speed with the game window, for the demo video. |
| `plot_results.py` | Learning curves from the `episodes.csv` files. |
| `speed_test.py` | How fast the env is + random / straight baseline numbers. |
| `results/`, `runs/` | Outputs. Git-ignored (so are `*.pt` / `*.onnx`). |

## Adding your algorithm
1. New file `algos/<name>.py` with a class that extends `Agent` (`act`, `observe`, `save`, `load`). Your learning goes in there.
2. Register it in `algos/__init__.py`.
3. Train it: `python agent/train.py --algo <name> --config agent/configs/<yours>.json --exe Builds/SnowboardEnv/SnowboardEnv.exe`

Try the random agent first to check everything works: `python agent/train.py --algo random --episodes 5 --exe Builds/SnowboardEnv/SnowboardEnv.exe`

## Keeping results comparable
- Use `evaluate.py` for the final numbers: same seeds for every agent.
- Run each experiment with a few different `--seed`s.
- Use the same `--mode` / `--repeat` when comparing, and note which game release you used (env-v1 etc.).
- Checkpoints aren't committed: share them over drive. Final csv / plots for the report can go in `docs/results/`.

## Who's doing what
| Who | What |
|---|---|
| _name_ | _algorithm / task_ |
