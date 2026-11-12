# Contributing

## Branching
- `main` is always working and openable in Unity. No direct pushes to `main`.
- Branch names: `unity/<topic>`, `agent/<topic>`, `docs/<topic>`. Merge via pull request, at least one review.
- Keep commits small; commit Unity changes together with their `.meta` files.

## Unity rules
- **Only one person edits a given Unity scene (or prefab) at a time.** Say so in the team chat before you start and when you finish. Scene/prefab merges are painful.
- Prefer prefabs over editing the scene directly, to keep conflicts small.
- `Assets/Terrain/SkiMountainData.asset` is a **binary** file (Git LFS) and cannot be merged: only the terrain owner edits it.
- Physics settings (`ProjectSettings/TimeManager.asset`, `DynamicsManager.asset`) are part of the environment. Changing them changes training results, so agree first and update `docs/env_spec.md`.
- Never commit `Library/`, `Temp/`, `UserSettings/`, `Logs/`, or builds.
- Never delete or regenerate `.meta` files by hand.
- Enable the merge driver once per clone:
  ```
  git config merge.unityyamlmerge.name "Unity SmartMerge"
  git config merge.unityyamlmerge.driver "'<UnityInstall>/Editor/Data/Tools/UnityYAMLMerge.exe' merge -p %O %B %A %A"
  ```

## Pre-merge checklist
- [ ] Project opens in Unity with no console errors
- [ ] Scene plays; environment reset works
- [ ] No `Library/`, `Temp/`, `.pt`, `.onnx` or build files in the diff
- [ ] `.meta` files included for every new/moved asset
- [ ] If the env spec changed (observations/actions/reward), `docs/env_spec.md` is updated and the Python side agrees
- [ ] Third-party code added? `README.md` "Third-party code" updated
- [ ] Python: `pip install -r agent/requirements.txt` still works

## Pinned versions (fill in exactly; everyone must match)
| Component | Version |
|---|---|
| Unity Editor | 6000.3.8f1 |
| com.unity.ml-agents | _TBD (not yet installed)_ |
| Python | _TBD_ |
| mlagents-envs | _TBD_ |
| torch | _TBD_ |
