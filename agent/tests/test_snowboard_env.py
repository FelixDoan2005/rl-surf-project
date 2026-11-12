"""The Python side of the Unity bridge, checked against a fake Unity server that speaks the same socket protocol
(see Assets/Scripts/EnvServer.cs). No Unity needed. If someone edits snowboard_env.py and breaks the protocol, this fails."""
import json
import socket
import struct
import threading

import numpy as np
import pytest

from envs.snowboard_env import SnowboardEnv, UnityEnvError

OBS_SIZE = 41


def frame(payload):
    return struct.pack("<I", len(payload)) + payload


def recv_exact(conn, n):
    buf = b""
    while len(buf) < n:
        chunk = conn.recv(n - len(buf))
        if not chunk:
            raise ConnectionError
        buf += chunk
    return buf


def obs_message(reward, terminated):
    info = {"failReason": "torso tilted (up.y 0.30)" if terminated else "", "finished": False,
            "episodeTime": 1.5, "z": 3.0, "speed": 2.0, "jumps": 0}
    return b"O" + struct.pack("<fBB", reward, int(terminated), 0) + np.arange(OBS_SIZE, dtype="<f4").tobytes() + json.dumps(info).encode()


class FakeUnity(threading.Thread):
    """Answers like EnvServer.cs: config -> hello, reset -> observation, step -> observation (episode ends on the 3rd step)."""

    def __init__(self):
        super().__init__(daemon=True)
        self.server = socket.socket()
        self.server.bind(("127.0.0.1", 0))
        self.server.listen(1)
        self.port = self.server.getsockname()[1]
        self.last_seed = None
        self.last_action = None
        self.config = None

    def run(self):
        conn, _ = self.server.accept()
        steps = 0
        while True:
            try:
                payload = recv_exact(conn, struct.unpack("<I", recv_exact(conn, 4))[0])
            except ConnectionError:
                return
            kind = payload[:1]
            if kind == b"C":
                self.config = json.loads(payload[1:].decode())
                hello = {"protocol": 1, "obsSize": OBS_SIZE, "actionSize": 4, "mode": "high", "dt": 0.01,
                         "actionRepeat": self.config["actionRepeat"], "realtime": False,
                         "actionNames": ["steer", "tuck", "brake", "jump"]}
                conn.sendall(frame(b"H" + json.dumps(hello).encode()))
            elif kind == b"R":
                steps = 0
                self.last_seed = struct.unpack("<i", payload[1:5])[0]
                conn.sendall(frame(obs_message(0.0, False)))
            elif kind == b"S":
                steps += 1
                self.last_action = np.frombuffer(payload[1:], dtype="<f4")
                conn.sendall(frame(obs_message(1.0, steps >= 3)))
            elif kind == b"Q":
                conn.close()
                return


@pytest.fixture
def env_and_server():
    fake = FakeUnity()
    fake.start()
    env = SnowboardEnv(port=fake.port, action_repeat=4, connect_timeout=5)
    yield env, fake
    env.close()


def test_handshake_sets_up_the_spaces(env_and_server):
    env, fake = env_and_server
    assert fake.config["actionRepeat"] == 4 and fake.config["mode"] == "high"
    assert env.observation_space.shape == (OBS_SIZE,)
    assert env.action_space.shape == (4,)
    assert list(env.action_space.low) == [-1, 0, 0, 0] and list(env.action_space.high) == [1, 1, 1, 1]
    assert env.action_names == ["steer", "tuck", "brake", "jump"]


def test_reset_passes_the_seed_and_returns_the_observation(env_and_server):
    env, fake = env_and_server
    obs, info = env.reset(seed=7)
    assert fake.last_seed == 7
    assert obs.dtype == np.float32 and obs.shape == (OBS_SIZE,) and obs[10] == 10.0
    env.reset()
    assert fake.last_seed == -1                         # no seed = "keep going"


def test_step_clips_actions_and_reports_the_end_of_an_episode(env_and_server):
    env, fake = env_and_server
    env.reset(seed=0)
    obs, reward, terminated, truncated, info = env.step([5.0, -3.0, 0.5, 9.0])
    assert list(fake.last_action) == [1.0, 0.0, 0.5, 1.0]      # clipped into the action space
    assert reward == 1.0 and not terminated and not truncated
    assert info["episodeTime"] == 1.5
    env.step([0, 0, 0, 0])
    _, _, terminated, _, info = env.step([0, 0, 0, 0])
    assert terminated and info["failReason"].startswith("torso tilted")


def test_step_after_the_episode_ended_needs_a_reset(env_and_server):
    env, _ = env_and_server
    env.reset(seed=0)
    for _ in range(3):
        env.step([0, 0, 0, 0])
    with pytest.raises(UnityEnvError, match="reset"):
        env.step([0, 0, 0, 0])
    env.reset()
    env.step([0, 0, 0, 0])                              # fine again after a reset


def test_wrong_number_of_actions_is_rejected(env_and_server):
    env, _ = env_and_server
    env.reset(seed=0)
    with pytest.raises(ValueError, match="expected 4"):
        env.step([0.0, 0.0])
