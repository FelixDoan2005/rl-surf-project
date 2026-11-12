# Environment Specification

_Status: v1, implemented in `Assets/Scripts/SnowboarderAgent.cs`. Keep in sync between Unity and Python._

## Overview
- One step = one physics step of **0.01 s (100 Hz)**; solver iterations 15/8 (`ProjectSettings/`). Add action repeat on the Python side if wanted.
- Jointed rider: torso, two legs (hip 3-DoF, knee, ankle) and a two-piece board. The rider faces **sideways**: torso local -X is downhill, so moving downhill is a *negative* x velocity in the observations.
- Two control modes (`manualControl`):
  - **Low-level** (`false`): 11 raw joint actions, no assists. The agent has to learn balance.
  - **High-level** (`true`): steer / tuck / brake / jump through `ApplyActions(...)`; balance and steering assists are on.
- Step loop (headless): `Physics.simulationMode = Script`, then per step: set action, `Tick()`, `Physics.Simulate(0.01)`, then read `CollectObservations()`, `ComputeReward()`, `Done`.

## Observations (41 floats)
| Index | What | Scaling |
|---|---|---|
| 0-2 | Torso linear velocity (torso frame) | / 20 |
| 3-5 | Torso angular velocity (torso frame) | / 5 |
| 6-8 | Direction of gravity (torso frame) | unit vector |
| 9 | Sideways position x | / track half-width (20 m) |
| 10-14 / 15-19 | Joint angles, front / back leg: hip pitch, hip yaw, hip roll, knee, ankle | [-1, 1] over the joint's limits |
| 20-24 / 25-29 | Joint angular velocities, same order | rad/s / 10, clamped to +-3 |
| 30-40 | Previous action (the 11 joint-level actions) | as sent |

No terrain or course information (slope, look-ahead, distance to finish) yet.

## Actions
**Low-level, 11 continuous values in [-1, 1]** (0 = middle of the joint range = neutral crouch), front leg first: `[hipPitch, hipYaw, hipRoll, knee, ankle]` x2, then `[10]` brake in [0, 1].
Ranges: hip pitch -60..20 deg, hip yaw +-30, hip roll +-25, knee 0..90, ankle -62..18.

**High-level:** `steering` in [-1, 1] (continuous), `isTucking`, `isBraking`, `isJumping` (booleans).
Tuck lowers drag (about 15 m/s tucked vs 13 m/s); brake slows and loosens sideways grip (drift when combined with steering); jump has a 0.6 s cooldown.

## Reward (placeholder, `ComputeReward`)
`progress (change in z / 10) + 0.002 * upright - 0.0005 * sum(action^2) - 0.001` per step; `+10` on finish, `-5` on a fall. To be tuned by the team.

## Termination
- **Fail:** torso less than 0.5 m above the ground, torso tilted (up.y < 0.4), or out of bounds (|x| > 25 m, y < -5).
- **Finish:** reaching the `FinishLine` trigger.
- **Truncation:** 60 s (`maxEpisodeSeconds`), not a failure.

## Reset
`ResetEpisode()`: rider placed at `StartPoint` with a random x offset of +-5 m, joints neutral, moving downhill at 6 m/s. Uses `UnityEngine.Random`; seeding is not exposed yet.
