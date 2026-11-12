# RL Surf — CM32032 Reinforcement Learning, CW1

A snowboarding/surfing game built in Unity 6, with agents trained from Python (PyTorch) using our own PPO/SAC.

## Layout
- repo root — Unity project (`Assets/`, `Packages/`, `ProjectSettings/`)
- `agent/` — Python RL code, training scripts, plots
- `docs/` — environment spec, experiment plan, notes

## Setup
1. Install **Git** and **Git LFS**, then `git lfs install`.
2. Clone, then open the repo root in Unity Hub with the exact editor version in `ProjectSettings/ProjectVersion.txt` (see `CONTRIBUTING.md` for pinned versions).
3. Python (3.10 recommended):
   ```
   python -m venv agent/.venv
   agent/.venv/Scripts/activate      # Windows; use source agent/.venv/bin/activate on macOS/Linux
   pip install -r agent/requirements.txt
   ```

## Run
- **Play manually:** open `Assets/Scenes/SampleScene.unity` and press Play. **A/D** steer, **W** tuck (faster), **S** brake (with A/D: drift), **Space** jump, **R** reset. It restarts by itself after a fall.
- **Raw joint control:** tick the *Joint Tester* component on `SnowboarderAgent`, press Play, and use the sliders in the Game view.
- **Environment spec** (observations, actions, reward): `docs/env_spec.md`.
- **Train:** _TBD — will be `python agent/train.py ...` once the agent code exists._
- **Training builds / results** go in `Build(s)/`, `results/`, `runs/` (all git-ignored).

## Third-party code
Every external source must be listed here (required by the coursework). One row per source.

| Source (name + URL) | Licence | How it was used | What we modified |
|---|---|---|---|
| Unity URP template / Unity packages (see `Packages/manifest.json`) | Unity licence | Project template and built-in packages | Nothing yet |
| _add rows_ | | | |
