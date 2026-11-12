using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

// Python <-> Unity bridge. Lets a Python process reset the snowboard environment and step it with actions.
//
// HOW TO RUN
//   Editor:  press Play, then run the menu  Surf > Env Server > Start (Play mode)  (or add this component to the
//            SnowboarderAgent). Python connects to 127.0.0.1:<port>.
//   Build:   SnowboardEnv.exe -envserver -port 5555 [-batchmode -nographics]   (agent/envs/snowboard_env.py launches it for you)
//
// While this component is enabled it owns the physics: Physics.simulationMode is Script and nothing advances until
// Python sends a step. That makes training lock-step and reproducible. Everything is on the main thread (no extra
// threads): in fast mode it serves requests back to back and only returns to Unity to draw a frame when Python pauses.
//
// MODES (chosen by Python in the CONFIG message)
//   mode "high"  4 actions  [steer -1..1, tuck 0..1, brake 0..1, jump 0..1]  (> 0.5 = pressed); balance/steering assists on
//   mode "low"   11 actions the raw joint targets (see SnowboarderAgent), no assists
//   actionRepeat  physics steps per env step (action held)
//   realtime      false = as fast as possible (training); true = paced to the wall clock with smooth rendering (recording a demo)
//
// PROTOCOL v1: every message is [uint32 little-endian payload length][payload]; payload[0] is the message type.
//   Python -> Unity   'C' + utf8 json config        -> 'H' + utf8 json hello (obsSize, actionSize, dt, ...)
//                     'R' + int32 seed (-1 = keep)  -> 'O' observation
//                     'S' + float32[actionSize]     -> 'O' observation
//                     'Q'                           -> closes the connection
//   Unity -> Python   'O' + float32 reward + uint8 terminated + uint8 truncated + float32[obsSize] + utf8 json info
//                     'E' + utf8 text  (an error)
public class EnvServer : MonoBehaviour
{
    public const int ProtocolVersion = 1;

    [Header("Connection")]
    public int port = 5555;
    [Tooltip("Fast mode: how long to wait for Python's next request before letting Unity draw a frame.")]
    public int pollMicroseconds = 2000;

    [Serializable] public class EnvConfig
    {
        public string mode = "high";
        public int actionRepeat = 1;
        public bool realtime = false;
        public bool randomStart = true;
        public float maxEpisodeSeconds = 60f;
    }
    [Serializable] public class Hello
    {
        public int protocol; public int obsSize; public int actionSize; public string mode;
        public float dt; public int actionRepeat; public bool realtime; public string[] actionNames;
    }
    [Serializable] public class StepInfo
    {
        public string failReason; public bool finished; public float episodeTime; public float z; public float speed; public int jumps;
    }

    static readonly string[] HighNames = { "steer", "tuck", "brake", "jump" };
    static readonly string[] LowNames =
    {
        "front_hip_pitch", "front_hip_yaw", "front_hip_roll", "front_knee", "front_ankle",
        "back_hip_pitch", "back_hip_yaw", "back_hip_roll", "back_knee", "back_ankle", "brake"
    };

    SnowboarderAgent agent;
    ArticulationBody body;
    ArticulationInterpolation interp;
    TcpListener listener;
    TcpClient client;
    NetworkStream stream;
    Socket sock;
    EnvConfig cfg = new EnvConfig();
    bool configured, fromCommandLine, everConnected;
    float dt;

    // real-time stepping state
    bool stepInProgress;
    float acc;
    int substepsDone;
    float[] pendingAction;

    // settings changed while the server runs (restored when it stops)
    SimulationMode prevSimMode;
    bool prevManual, prevKeyboard, prevAutoReset, prevRunInBg;
    int prevVSync, prevFps;
    float prevOffset, prevMaxSeconds;

    int ActionSize => cfg.mode == "low" ? SnowboarderAgent.ActionSize : HighNames.Length;

    // ------------------------------------------------------------------ start / stop
    static bool HasArg(string name)
    {
        foreach (var a in Environment.GetCommandLineArgs()) if (a == name) return true;
        return false;
    }

    static string GetArg(string name)
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
        return null;
    }

    // A built player started with -envserver gets the server automatically (no scene setup needed).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoStart()
    {
        if (!HasArg("-envserver")) return;
        var existing = FindFirstObjectByType<EnvServer>(FindObjectsInactive.Include);
        if (existing != null) { existing.enabled = true; return; }
        var a = FindFirstObjectByType<SnowboarderAgent>();
        if (a == null) { Debug.LogError("[EnvServer] no SnowboarderAgent in the scene"); return; }
        a.gameObject.AddComponent<EnvServer>();
    }

    void OnEnable()
    {
        agent = GetComponent<SnowboarderAgent>();
        body = GetComponent<ArticulationBody>();
        interp = GetComponent<ArticulationInterpolation>();
        fromCommandLine = HasArg("-envserver");
        if (fromCommandLine && int.TryParse(GetArg("-port"), out int p)) port = p;
        dt = Time.fixedDeltaTime;

        prevSimMode = Physics.simulationMode;
        prevManual = agent.manualControl; prevKeyboard = agent.readKeyboard; prevAutoReset = agent.autoResetInManualPlay;
        prevOffset = agent.startOffsetRange; prevMaxSeconds = agent.maxEpisodeSeconds;
        prevRunInBg = Application.runInBackground; prevVSync = QualitySettings.vSyncCount; prevFps = Application.targetFrameRate;

        try
        {
            listener = new TcpListener(IPAddress.Loopback, port);   // this computer only
            listener.Start();
        }
        catch (Exception e)
        {
            Debug.LogError("[EnvServer] cannot listen on port " + port + ": " + e.Message);
            listener = null;
            enabled = false;
            return;
        }

        Physics.simulationMode = SimulationMode.Script;   // from now on this script decides when the physics advances
        agent.readKeyboard = false;
        Application.runInBackground = true;
        cfg = new EnvConfig();
        ApplyConfig(cfg);
        Debug.Log("[EnvServer] listening on 127.0.0.1:" + port);
    }

    void OnDisable()
    {
        Disconnect();
        if (listener != null) { listener.Stop(); listener = null; }
        if (agent == null) return;
        Physics.simulationMode = prevSimMode;
        agent.manualControl = prevManual; agent.readKeyboard = prevKeyboard; agent.autoResetInManualPlay = prevAutoReset;
        agent.startOffsetRange = prevOffset; agent.maxEpisodeSeconds = prevMaxSeconds;
        Application.runInBackground = prevRunInBg; QualitySettings.vSyncCount = prevVSync; Application.targetFrameRate = prevFps;
        if (interp != null) { interp.externalClock = false; interp.enabled = true; }
    }

    void ApplyConfig(EnvConfig c)
    {
        c.mode = c.mode == "low" ? "low" : "high";
        c.actionRepeat = Mathf.Max(1, c.actionRepeat);
        agent.manualControl = c.mode == "high";       // high: keys path with assists; low: raw joint actions
        agent.autoResetInManualPlay = false;           // Python decides when to reset
        agent.startOffsetRange = c.randomStart ? prevOffset : 0f;
        agent.maxEpisodeSeconds = c.maxEpisodeSeconds;
        if (interp != null)
        {
            interp.externalClock = true;
            interp.enabled = c.realtime && !Application.isBatchMode;   // smooth drawing only matters when someone is watching
        }
        QualitySettings.vSyncCount = c.realtime ? prevVSync : 0;
        Application.targetFrameRate = c.realtime && prevVSync == 0 ? 60 : -1;
        stepInProgress = false;
        acc = 0f;
        configured = true;
    }

    // ------------------------------------------------------------------ main loop
    void Update()
    {
        if (listener == null) return;
        if (client == null)
        {
            if (everConnected && fromCommandLine) { Application.Quit(); return; }   // launched by Python: exit with it
            if (!listener.Pending()) return;
            Accept();
            if (client == null) return;
        }
        if (cfg.realtime) UpdateRealtime(); else UpdateFast();
    }

    // Fast mode: answer requests back to back for up to 50 ms, then let Unity draw a frame and come back.
    void UpdateFast()
    {
        double t0 = Time.realtimeSinceStartupAsDouble;
        while (client != null && Time.realtimeSinceStartupAsDouble - t0 < 0.05)
        {
            if (!TryReadFrame(pollMicroseconds, out byte[] payload)) break;
            Handle(payload);
        }
    }

    // Real-time mode: the physics advances at the wall-clock rate (0.01 s per step), one env step = actionRepeat steps.
    void UpdateRealtime()
    {
        acc = Mathf.Min(acc + Time.unscaledDeltaTime, 0.05f);   // time owed to the physics; never catch up more than 50 ms
        for (int guard = 0; client != null && guard < 32; guard++)
        {
            if (!stepInProgress)
            {
                // Waiting for Python's next action. If time is owed, give Python a moment to answer so the next step
                // starts in this same frame (otherwise every env step would cost a whole frame).
                if (!TryReadFrame(acc >= dt ? pollMicroseconds : 0, out byte[] payload)) break;
                Handle(payload);        // a step request starts a step; reset/config replies right away
                continue;
            }
            if (acc < dt) break;        // the next physics step isn't due yet
            acc -= dt;
            PhysicsSubStep(pendingAction, substepsDone == 0);
            substepsDone++;
            if (substepsDone >= cfg.actionRepeat || agent.Done)
            {
                stepInProgress = false;
                SendObs(agent.ComputeReward());
            }
        }
        if (interp != null) interp.externalAlpha = Mathf.Clamp01(acc / dt);   // how far through the next step this frame is
    }

    void PhysicsSubStep(float[] a, bool first)
    {
        if (cfg.mode == "low") agent.SetAction(a);
        else agent.ApplyActions(a[0], a[1] > 0.5f, a[2] > 0.5f, first && a[3] > 0.5f);   // a jump is a press, not a hold
        agent.Tick();
        Physics.Simulate(dt);
        if (interp != null) interp.RecordStep();
    }

    // ------------------------------------------------------------------ messages
    void Handle(byte[] p)
    {
        switch ((char)p[0])
        {
            case 'C': HandleConfig(p); break;
            case 'R': HandleReset(p); break;
            case 'S': HandleStep(p); break;
            case 'Q': Debug.Log("[EnvServer] Python said goodbye"); Disconnect(); break;
            default: SendError("unknown message type '" + (char)p[0] + "'"); break;
        }
    }

    void HandleConfig(byte[] p)
    {
        cfg = new EnvConfig();
        string json = Encoding.UTF8.GetString(p, 1, p.Length - 1);
        if (json.Length > 0) JsonUtility.FromJsonOverwrite(json, cfg);
        ApplyConfig(cfg);
        var h = new Hello
        {
            protocol = ProtocolVersion, obsSize = SnowboarderAgent.ObservationSize, actionSize = ActionSize, mode = cfg.mode,
            dt = dt, actionRepeat = cfg.actionRepeat, realtime = cfg.realtime,
            actionNames = cfg.mode == "low" ? LowNames : HighNames
        };
        SendText('H', JsonUtility.ToJson(h));
        Debug.Log("[EnvServer] configured: " + json);
    }

    void HandleReset(byte[] p)
    {
        if (!configured) ApplyConfig(cfg);
        int seed = p.Length >= 5 ? BitConverter.ToInt32(p, 1) : -1;
        if (seed >= 0) UnityEngine.Random.InitState(seed);
        stepInProgress = false;
        acc = 0f;
        agent.ResetEpisode();
        Physics.Simulate(dt);   // a teleport only takes effect on the next physics step; this makes the first observation real
        if (interp != null) interp.RecordStep();
        SendObs(0f);
    }

    void HandleStep(byte[] p)
    {
        if (!configured) ApplyConfig(cfg);
        int n = ActionSize;
        if (p.Length != 1 + 4 * n) { SendError("step needs " + n + " float32 actions, got " + (p.Length - 1) / 4); return; }
        var a = new float[n];
        Buffer.BlockCopy(p, 1, a, 0, 4 * n);
        for (int i = 0; i < n; i++) if (float.IsNaN(a[i]) || float.IsInfinity(a[i])) a[i] = 0f;

        if (cfg.realtime) { pendingAction = a; substepsDone = 0; stepInProgress = true; return; }   // the loop above runs it

        for (int i = 0; i < cfg.actionRepeat; i++)
        {
            PhysicsSubStep(a, i == 0);
            if (agent.Done) break;
        }
        SendObs(agent.ComputeReward());
    }

    void SendObs(float reward)
    {
        float[] obs = agent.CollectObservations();
        bool terminated = agent.Failed || agent.Finished;
        bool truncated = agent.TimedOut && !terminated;
        var info = new StepInfo
        {
            failReason = agent.FailReason, finished = agent.Finished, episodeTime = agent.EpisodeTime,
            z = agent.transform.position.z, speed = body.linearVelocity.magnitude, jumps = agent.Jumps
        };
        using (var ms = new MemoryStream(8 + 4 * obs.Length + 160))
        using (var w = new BinaryWriter(ms))
        {
            w.Write((byte)'O');
            w.Write(reward);
            w.Write((byte)(terminated ? 1 : 0));
            w.Write((byte)(truncated ? 1 : 0));
            for (int i = 0; i < obs.Length; i++) w.Write(obs[i]);
            w.Write(Encoding.UTF8.GetBytes(JsonUtility.ToJson(info)));
            w.Flush();
            Send(ms.ToArray());
        }
    }

    void SendText(char type, string text)
    {
        byte[] t = Encoding.UTF8.GetBytes(text);
        var payload = new byte[1 + t.Length];
        payload[0] = (byte)type;
        Buffer.BlockCopy(t, 0, payload, 1, t.Length);
        Send(payload);
    }

    void SendError(string message)
    {
        Debug.LogWarning("[EnvServer] " + message);
        SendText('E', message);
    }

    // ------------------------------------------------------------------ socket plumbing
    void Accept()
    {
        try
        {
            client = listener.AcceptTcpClient();
            client.NoDelay = true;
            client.ReceiveTimeout = 5000;
            client.SendTimeout = 5000;
            stream = client.GetStream();
            sock = client.Client;
            everConnected = true;
            configured = false;
            Debug.Log("[EnvServer] Python connected");
        }
        catch (Exception e)
        {
            Debug.LogWarning("[EnvServer] accept failed: " + e.Message);
            Disconnect();
        }
    }

    void Disconnect()
    {
        stepInProgress = false;
        try { if (stream != null) stream.Close(); if (client != null) client.Close(); } catch (Exception) { }
        stream = null; client = null; sock = null;
    }

    // Waits up to waitMicros for a complete request. Returns false if none arrived (or the connection closed).
    bool TryReadFrame(int waitMicros, out byte[] payload)
    {
        payload = null;
        if (client == null) return false;
        try
        {
            if (sock.Available < 4)
            {
                if (!sock.Poll(Mathf.Max(waitMicros, 0), SelectMode.SelectRead)) return false;
                if (sock.Available == 0) { Debug.Log("[EnvServer] Python disconnected"); Disconnect(); return false; }
            }
            byte[] header = ReadExact(4);
            int len = BitConverter.ToInt32(header, 0);
            if (len <= 0 || len > (1 << 20)) throw new IOException("bad message length " + len);
            payload = ReadExact(len);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[EnvServer] read failed: " + e.Message);
            Disconnect();
            return false;
        }
    }

    byte[] ReadExact(int n)
    {
        var buf = new byte[n];
        int off = 0;
        while (off < n)
        {
            int r = stream.Read(buf, off, n - off);
            if (r <= 0) throw new IOException("connection closed");
            off += r;
        }
        return buf;
    }

    void Send(byte[] payload)
    {
        if (client == null) return;
        try
        {
            var frame = new byte[4 + payload.Length];
            BitConverter.GetBytes(payload.Length).CopyTo(frame, 0);
            Buffer.BlockCopy(payload, 0, frame, 4, payload.Length);
            stream.Write(frame, 0, frame.Length);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[EnvServer] send failed: " + e.Message);
            Disconnect();
        }
    }

    void OnGUI()
    {
        if (client == null && listener != null)
            GUI.Label(new Rect(10, 10, 700, 24), "EnvServer: waiting for Python on port " + port + "  (physics paused)");
    }
}
