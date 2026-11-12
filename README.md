# what we got so far

## Layout
- repo root — Unity project (`Assets/`, `Packages/`, `ProjectSettings/`)
- `agent/` — Python RL code, training scripts, plots
- `docs/` — environment spec, experiment plan, notes

## Setup
1. Install **Git** and **Git LFS** (u can skip if ur only doing python but might as well its fast), then `git lfs install`.
2. Clone the repo.

**Python side**

3. Python (3.11 recommended):
   ```
   python -m venv agent/.venv
   agent/.venv/Scripts/activate      # Windows; use source agent/.venv/bin/activate on macOS/Linux
   pip install -r agent/requirements.txt
   ```

**Unity side**

4. Open the repo root in Unity Hub with the exact editor version in `ProjectSettings/ProjectVersion.txt` (see `CONTRIBUTING.md` for pinned versions).

## Run

### Python side
Right now the snowboard game is a Unity project and Python code can't run Unity so for people working on just the python side (to be decided), I will package the game into a single program (havent done this yet) - "SnowboardEnv.exe" so u don't need Unity or the project to use it.
- **Get the game:** download the zip from the Releases page (Windows only) and unzip it into `Builds/SnowboardEnv/`.
- **Check it works:** `python agent/scripts/speed_test.py --exe Builds/SnowboardEnv/SnowboardEnv.exe` checks the connection, speed and random/straight baselines. Use `from envs.snowboard_env import SnowboardEnv` in your own code (run from `agent/`).
- **Environment spec** (observations, actions, reward, Python interface): `docs/env_spec.md`.
- **Train:** `python agent/scripts/train.py --algo random --episodes 5 --exe Builds/SnowboardEnv/SnowboardEnv.exe` (random agent, just to check it works), then `--algo <yours>` once u've added it.
- **Agent scripts are already set up:** `agent/scripts/` has train, evaluate, play (for the demo video) and plot scripts, plus a random agent and a straight-line agent to compare against. we just need to add chosen algs and stuff to `agent/algos/`, see `agent/README.md`
- **Training builds / results** go in `Build(s)/`, `results/`, `runs/` (all git-ignored).


### Unity side (Abdul pls tweak it if you think smt isnt quite right) 
- **Play manually:** open `Assets/Scenes/SampleScene.unity` and press Play. **A/D** steer, **W** tuck (faster), **S** brake (with A/D: drift), **Space** jump, **R** reset. It restarts by itself after a fall.
- **Raw joint control:** tick the *Joint Tester* component on `SnowboarderAgent`, press Play, and use the sliders in the Game view (very hard to do lol).
- **Build the player:** *Surf > Env Server > Build Windows Player* (output goes to `Builds/`, not in git).

## Claude made this Third-party code thing from the spec
Every external source must be listed here (required by the coursework). One row per source.

| Source (name + URL) | Licence | How it was used | What we modified |
|---|---|---|---|
| Unity URP template / Unity packages (see `Packages/manifest.json`) | Unity licence | Project template and built-in packages | Nothing yet |
| Gymnasium (https://gymnasium.farama.org) | MIT | Standard environment interface (`reset`/`step`, spaces) in `agent/envs/snowboard_env.py` | Nothing |
| NumPy (https://numpy.org) | BSD-3 | Arrays and maths | Nothing |
| PyTorch (https://pytorch.org) | BSD-style | Neural networks and autograd only; the RL algorithms are our own | Nothing |
| _add rows_ | | | |
