using UnityEngine;
using UnityEngine.InputSystem;

// Debug tool: drive the snowboarder's 11 raw joint actions by hand, to see what each one does.
//
// HOW TO USE: this component is on the SnowboarderAgent but switched OFF. Tick its checkbox in the Inspector,
// press Play, and drag the sliders that appear in the top-left of the Game view (or the "Actions" values in the
// Inspector while playing). R resets the rider. Untick it to go back to normal keyboard play.
//
// While it is on it takes over from the keyboard (Manual Control is switched off for you), which is exactly the
// setting a learning agent uses: there is no balance assist, so the rider has to be held up by these joints alone.
// 0 = the neutral crouch; each slider maps -1..1 onto that joint's full range.
[DefaultExecutionOrder(-50)]   // set the actions before SnowboarderAgent runs its physics step
[RequireComponent(typeof(SnowboarderAgent))]
public class JointTester : MonoBehaviour
{
    [Tooltip("0-4 front leg: hip pitch, hip yaw, hip roll, knee, ankle. 5-9 back leg: same order. 10 = brake (0..1).")]
    [Range(-1f, 1f)] public float[] actions = new float[SnowboarderAgent.ActionSize];
    public bool resetAfterFall = true;
    public float resetDelay = 1.5f;

    static readonly string[] Names =
    {
        "Front hip pitch (-60..20)", "Front hip yaw (+-30)", "Front hip roll (+-25)", "Front knee (0..90)", "Front ankle (-62..18)",
        "Back hip pitch (-60..20)",  "Back hip yaw (+-30)",  "Back hip roll (+-25)",  "Back knee (0..90)",  "Back ankle (-62..18)",
        "Brake (0..1)"
    };

    SnowboarderAgent agent;
    bool previousManual;
    float doneTimer;

    void OnEnable()
    {
        agent = GetComponent<SnowboarderAgent>();
        if (actions == null || actions.Length != SnowboarderAgent.ActionSize) actions = new float[SnowboarderAgent.ActionSize];
        previousManual = agent.manualControl;
        agent.manualControl = false;       // raw joint control: no keyboard, no balance assist
    }

    void OnDisable()
    {
        if (agent != null) agent.manualControl = previousManual;
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.rKey.wasPressedThisFrame) agent.ResetEpisode();
    }

    void FixedUpdate()
    {
        if (Physics.simulationMode == SimulationMode.Script) return;   // something else is stepping the physics
        if (agent.Done && resetAfterFall)
        {
            doneTimer += Time.fixedDeltaTime;
            if (doneTimer >= resetDelay) { doneTimer = 0f; agent.ResetEpisode(); }
        }
        else doneTimer = 0f;
        agent.SetAction(actions);
    }

    void OnGUI()
    {
        const float rowH = 24f, w = 380f;
        int n = SnowboarderAgent.ActionSize;
        GUI.Box(new Rect(10, 10, w, 56 + n * rowH), "Joint actions    (R = reset)");
        for (int i = 0; i < n; i++)
        {
            float y = 36 + i * rowH;
            GUI.Label(new Rect(18, y, 190, rowH), Names[i]);
            bool brake = i == n - 1;
            actions[i] = GUI.HorizontalSlider(new Rect(210, y + 6, 120, rowH), actions[i], brake ? 0f : -1f, 1f);
            GUI.Label(new Rect(338, y, 50, rowH), actions[i].ToString("F2"));
        }
        if (GUI.Button(new Rect(18, 40 + n * rowH, 120, 22), "Zero all")) System.Array.Clear(actions, 0, actions.Length);
        string state = agent.Done ? (agent.Finished ? "finished" : "fell: " + agent.FailReason) : "riding  " + agent.EpisodeTime.ToString("F1") + " s";
        GUI.Label(new Rect(150, 40 + n * rowH, 230, 22), state);
    }
}
