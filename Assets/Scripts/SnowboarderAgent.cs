using UnityEngine;
using UnityEngine.InputSystem;

// Jointed snowboarder: torso + 2x (thigh, shin, board half) built from primitives with ArticulationBody joints.
// The rider faces sideways (torso local -X points downhill = the board's nose). Body built by
// Assets/Editor/SnowboardBuilder.cs; this script drives it.
//
// Index 0 = FRONT foot, index 1 = BACK foot.
//
// HOW AN RL AGENT CONTROLS THE RIDER - two interfaces, pick one per experiment:
//
//  1) Low-level joint control (the main research setting; the agent must learn to balance itself):
//       manualControl = false;
//       each step:  agent.SetAction(float[11]);  agent.Tick();  Physics.Simulate(dt);
//                   obs = agent.CollectObservations();  r = agent.ComputeReward();  done = agent.Done;
//     Actions (11, each in [-1,1]): per leg [hipPitch, hipYaw, hipRoll, knee, ankle(=board edge)],
//     front leg first then back leg, then [10] = brake in [0,1]. Action 0 = neutral crouch.
//
//  2) High-level commands (easier; the built-in balance assist keeps the rider up):
//       manualControl = true;  readKeyboard = false;
//       each step:  agent.ApplyActions(steering, isTucking, isBraking);  agent.Tick();  Physics.Simulate(dt);
//     This is exactly what the keyboard does, so a human and the agent share one control path.
//     Comparing (1) against (2) is a good experiment: how much does handing the agent balance help?
//
// For human testing, leave manualControl and readKeyboard on: A/D steer, W tuck, S brake/drift, R reset.
//
// Articulations can't form loops, so the board is two halves (one per foot) held together by a stiff
// spring/damper coupling (CoupleBoard). Treat it as one rigid board.
[RequireComponent(typeof(ArticulationBody))]
public class SnowboarderAgent : MonoBehaviour
{
    public const int ActionSize = 11;        // 10 joint targets + brake
    public const int ObservationSize = 41;   // 30 body/joint values + previous action (11)
    const int BrakeIndex = 10;
    public const float StartYaw = 90f;   // rider faces sideways

    [Header("Setup")]
    public Camera followCamera;              // drag the Main Camera here; it will chase this rider
    public Transform startPoint;
    public float trackHalfWidth = 20f;
    public float maxEpisodeSeconds = 60f;
    public float startOffsetRange = 5f;
    public float startSpeed = 6f;            // m/s downhill at reset: the rider drops in already moving

    [Header("Joint drives (tune these)")]
    public float stiffness = 2000f;
    public float damping = 300f;          // 200 made the ankles buzz (board/screen vibration); 300 halves it without
                                          // slowing the balance response (350+ was smoother but fell more in slaloms)
    public float forceLimit = 1000f;

    [Header("Board")]
    public float edgeGrip = 6f;          // resists sideways sliding of the board
    public float maxLateralAccel = 8f;   // m/s^2 (~0.8 g): snow can only grip so hard. Beyond this the board slides
                                         // (a drift) instead of an impossible grip force tripping it over its edge.
    [Range(0f, 1f)] public float carveSpeedKeep = 1f;   // 1 = turning never costs speed (grip force perpendicular to travel); 0 = old behaviour
    [Header("Air drag and braking (real units)")]
    public float airDensity = 1.2f;            // kg/m^3
    public float dragAreaStanding = 0.45f;     // Cd*A in m^2 in a riding stance (typical 0.4-0.7)
    public float tuckAreaFactor = 0.45f;       // Cd*A multiplier when fully crouched (a tuck is much more aerodynamic)
    public float referenceMass = 80f;          // the sim rider is light, so drag is scaled as if it were an 80 kg person
    public float brakeDeceleration = 5f;       // m/s^2 at full brake (brake action = 1): a hard "hockey stop"
    public float driftBrakeFraction = 0.5f;    // manual keys: S while steering uses this fraction of the brake, so you drift instead of stopping
    public float brakeGripFactor = 0.1f;       // sideways grip at full brake: low = the board slides (drift)
    public float brakeSteerBoost = 0.4f;       // manual keys: steering is (1 + boost)x stronger while braking, so you can throw the tail out
    public float stanceSeparation = 0.5f;
    public float couplePos = 800f, couplePosDamp = 40f;   // keep the two halves together
    public float coupleRot = 20f, coupleRotDamp = 2f;     // keep the two halves aligned

    [Header("Control")]
    public bool manualControl = true;    // true = high-level steer/tuck/brake path (keyboard or ApplyActions); false = raw joint actions
    public bool readKeyboard = true;     // turn off when an agent drives the high-level path through ApplyActions
    public bool autoResetInManualPlay = true;    // when playing by hand, restart at the top after a fall (R still resets any time)
    public float autoResetDelay = 1.5f;          // seconds to lie there before restarting, so you can see what happened
    float doneTimer;

    public float[] Action { get; private set; } = new float[ActionSize];
    public bool Finished { get; private set; }
    public bool Failed { get; private set; }
    public bool TimedOut { get; private set; }
    public bool Done => Finished || Failed || TimedOut;
    public string FailReason { get; private set; } = "";   // why the last episode failed (debugging aid)
    public float EpisodeTime => episodeTime;

    // Direction the board's nose points, flattened onto the ground (used by the camera, heading hold and carve limit).
    // Taken from the halves' orientation (local -X = nose). The line between the two halves was used before, but
    // it wobbles as the spring-coupled halves move against each other.
    public Vector3 Heading
    {
        get
        {
            if (board[0] == null || board[1] == null) return Vector3.forward;
            Vector3 d = -(board[0].transform.right + board[1].transform.right);
            d.y = 0f;
            return d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.forward;
        }
    }

    [Header("Rendering")]
    // Draw the rider blended between physics steps (ArticulationInterpolation). Unity can't interpolate
    // ArticulationBodies itself, so without it the rider is drawn in 10-15 cm jumps and looks like it's shaking.
    public bool smoothRendering = true;
    ArticulationInterpolation drawn;

    // debug readouts (handy for tuning; not used by the agent)
    public float DebugYawTorque { get; private set; }
    public float DebugYawRate { get; private set; }

    ArticulationBody torso;
    ArticulationBody[] thigh = new ArticulationBody[2];
    ArticulationBody[] shin = new ArticulationBody[2];
    ArticulationBody[] board = new ArticulationBody[2];
    ArticulationBody[] allBodies;
    float[] prevAction = new float[ActionSize];
    float episodeTime, lastZ;

void Awake()
{
    torso = GetComponent<ArticulationBody>();
    string[] side = { "F", "B" };
    for (int i = 0; i < 2; i++)
    {
        thigh[i] = transform.Find("Thigh" + side[i]).GetComponent<ArticulationBody>();
        shin[i] = thigh[i].transform.Find("Shin" + side[i]).GetComponent<ArticulationBody>();
        board[i] = shin[i].transform.Find("Board" + side[i]).GetComponent<ArticulationBody>();
        Tune(thigh[i], true, true, true);
        Tune(shin[i], true, false, false);
        Tune(board[i], true, false, false);
    }

    allBodies = new ArticulationBody[7];
    allBodies[0] = torso;
    for (int i = 0; i < 2; i++)
    {
        allBodies[1 + i * 3] = thigh[i];
        allBodies[2 + i * 3] = shin[i];
        allBodies[3 + i * 3] = board[i];
    }

    // 1. Ignore self-collisions across all limbs and board halves to stop contact popping
    var allColliders = GetComponentsInChildren<Collider>();
    for (int i = 0; i < allColliders.Length; i++)
    {
        for (int j = i + 1; j < allColliders.Length; j++)
        {
            Physics.IgnoreCollision(allColliders[i], allColliders[j], true);
        }
    }

    // 2. Set moderate solver iterations and use Discrete collision detection to stop phantom terrain jumps
    foreach (var b in allBodies) 
    {
        b.solverIterations = 12;
        b.solverVelocityIterations = 4;
        b.collisionDetectionMode = CollisionDetectionMode.Discrete;
    }

    if (startPoint == null)
    {
        var sp = GameObject.Find("StartPoint");
        if (sp != null) startPoint = sp.transform;
    }

    if (followCamera != null)
    {
        var fc = followCamera.GetComponent<FollowCamera>();
        if (fc == null) fc = followCamera.gameObject.AddComponent<FollowCamera>();
        fc.target = transform;
    }

    // 3. Smooth rendering (visual only): draw the rider between physics steps instead of in jumps
    if (smoothRendering)
    {
        drawn = GetComponent<ArticulationInterpolation>();
        if (drawn == null) drawn = gameObject.AddComponent<ArticulationInterpolation>();
        if (drawn.rigidGroup == null || drawn.rigidGroup.Length == 0)
            drawn.rigidGroup = new[] { board[0].transform, board[1].transform };   // draw the two halves as one solid board
    }
}

    void Start() => ResetEpisode();

    void Update()
    {
        if (manualControl && readKeyboard) ReadKeyboard();
    }

    void FixedUpdate()
    {
        // When something else steps the physics by hand (Physics.simulationMode == Script, e.g. a headless
        // training loop or a test), it calls Tick() itself, so don't also tick here.
        if (Physics.simulationMode == SimulationMode.Script) return;

        // after a fall (or timeout) restart at the top; reaching the finish stays put until you press R
        if (Done && !Finished && manualControl && autoResetInManualPlay)
        {
            doneTimer += Time.fixedDeltaTime;
            if (doneTimer >= autoResetDelay) ResetEpisode();
        }
        Tick();
    }

    // One control step: apply the current Action and update the episode state. Call once per physics step.
    public void Tick()
    {
        if (Done) { CoupleBoard(); return; }   // after a fall or the finish, still keep the board in one piece

        if (manualControl) ManualStep();   // turn the keys into actions (manual play only)

        ApplyAction(Action);
        CoupleBoard();
        BoardForces();
        episodeTime += Time.fixedDeltaTime;

        // The teleport in ResetEpisode only takes effect at the next physics step, so on the first Tick after a
        // reset the transforms still show the OLD (maybe fallen) pose. Skip the checks once or it fails instantly.
        if (justReset) { justReset = false; System.Array.Copy(Action, prevAction, ActionSize); return; }

        // ---- TODO (you): termination rules ----
        Vector3 p = transform.position;
        float height = HeightAboveGround();
        if (height < 0.5f) { Failed = true; FailReason = "torso near ground (" + height.ToString("F2") + " m)"; }
        else if (transform.up.y < 0.4f) { Failed = true; FailReason = "torso tilted (up.y " + transform.up.y.ToString("F2") + ")"; }
        else if (p.y < -5f || Mathf.Abs(p.x) > trackHalfWidth + 5f) { Failed = true; FailReason = "out of bounds " + p.ToString("F1"); }
        if (episodeTime > maxEpisodeSeconds) TimedOut = true;   // truncation, not failure

        System.Array.Copy(Action, prevAction, ActionSize);
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.name == "FinishLine") Finished = true;
    }

    // ------------------------------------------------------------------ actions
    public void SetAction(float[] a) => System.Array.Copy(a, Action, Mathf.Min(a.Length, ActionSize));

    void ApplyAction(float[] a)
    {
        for (int l = 0; l < 2; l++)
        {
            int o = l * 5;
            SetTarget(thigh[l], 0, a[o]);       // hip pitch
            SetTarget(thigh[l], 1, a[o + 1]);   // hip yaw (turns the foot/board)
            SetTarget(thigh[l], 2, a[o + 2]);   // hip roll
            SetTarget(shin[l], 0, a[o + 3]);    // knee
            SetTarget(board[l], 0, a[o + 4]);   // ankle: rolls the board onto its heel/toe edge
        }
    }

    // Spring/damper that makes the two board halves behave like one rigid board.
    void CoupleBoard()
    {
        var f = board[0];
        var b = board[1];

        Vector3 axis = -f.transform.right;   // board nose direction
        Vector3 posErr = (f.transform.position - b.transform.position) - axis * stanceSeparation;
        Vector3 force = -couplePos * posErr - couplePosDamp * (f.linearVelocity - b.linearVelocity);
        f.AddForce(force);
        b.AddForce(-force);

        Quaternion q = f.transform.rotation * Quaternion.Inverse(b.transform.rotation);
        q.ToAngleAxis(out float angle, out Vector3 rotAxis);
        if (angle > 180f) angle -= 360f;
        Vector3 rotErr = rotAxis * (angle * Mathf.Deg2Rad);
        Vector3 torque = coupleRot * rotErr + coupleRotDamp * (f.angularVelocity - b.angularVelocity);
        f.AddTorque(-torque);
        b.AddTorque(torque);
    }

    // The board glides along its length but resists sliding sideways, and drag limits the top speed.
    // Steering is done by the agent moving its feet (front/back hip roll in opposite directions), not here.
    void BoardForces()
    {
        float brake = Mathf.Clamp01(Action[BrakeIndex]);
        // braking loosens the grip so the tail can slide; the full loosening is already reached at half brake,
        // so a drift (half brake) slides as freely as a hard stop
        float grip = edgeGrip * Mathf.Lerp(1f, brakeGripFactor, Mathf.Clamp01(brake * 2f));

        // Carving keeps its speed: the grip force is aimed perpendicular to the direction of travel, so it turns the
        // momentum without slowing it. (Straight across the board, as before, also bled off speed whenever the board
        // slipped, so every turn cost speed.) Braking blends back to across-the-board, so drifts and stops still scrub speed.
        float keep = carveSpeedKeep * (1f - Mathf.Clamp01(brake * 2f));
        for (int i = 0; i < 2; i++)
        {
            var s = board[i];
            Vector3 across = s.transform.forward;                      // local Z = across the board
            Vector3 nose = -s.transform.right;                          // local -X = along the board
            Vector3 vel = s.linearVelocity;
            float lateral = Vector3.Dot(vel, across);
            float along = Vector3.Dot(vel, nose);
            float accel = Mathf.Min(Mathf.Abs(lateral) * grip, maxLateralAccel);   // friction-limited
            Vector3 dir = -across * (lateral >= 0f ? 1f : -1f);                    // across-the-board: opposes the sliding
            float mag = Mathf.Sqrt(lateral * lateral + along * along);
            if (keep > 0f && mag > 0.05f)
            {
                // perpendicular to the velocity (within the board's plane), on the side that rotates it toward the board's axis
                float m = -(lateral >= 0f ? 1f : -1f) * (along >= 0f ? 1f : -1f);
                Vector3 perp = m * (-lateral * nose + along * across) / mag;
                dir = Vector3.Lerp(dir, perp, keep).normalized;
            }
            s.AddForce(dir * accel * s.mass);
        }

        // Air drag: a = 0.5 * rho * CdA * v^2 / m, as a deceleration against the direction of travel.
        // CdA falls with crouch depth (a tuck is faster). Brake adds a plain deceleration.
        // The acceleration is applied to every body in proportion to its mass, so the whole rider slows evenly.
        Vector3 v = torso.linearVelocity;
        Vector3 flat = new Vector3(v.x, 0f, v.z);
        float speed = flat.magnitude;
        if (speed < 0.01f) return;

        float cda = dragAreaStanding * Mathf.Lerp(1f, tuckAreaFactor, Crouch());
        float decel = 0.5f * airDensity * cda * speed * speed / referenceMass + brake * brakeDeceleration;
        Vector3 force = -(flat / speed) * decel;   // per kg
        foreach (var b in allBodies) b.AddForce(force * b.mass);
    }

    // how deep the crouch is, 0 = neutral stance, 1 = the W crouch (average knee angle 45 -> 70 degrees)
    public float Crouch()
    {
        float deg = (shin[0].jointPosition[0] + shin[1].jointPosition[0]) * 0.5f * Mathf.Rad2Deg;
        return Mathf.Clamp01((deg - 45f) / 25f);
    }

    // ------------------------------------------------------------------ observations (40 numbers)
    public float[] CollectObservations()
    {
        var obs = new float[ObservationSize];
        int k = 0;

        Vector3 v = transform.InverseTransformDirection(torso.linearVelocity);
        Vector3 w = transform.InverseTransformDirection(torso.angularVelocity);
        Vector3 g = transform.InverseTransformDirection(Vector3.down);   // which way is "down" in the torso frame
        obs[k++] = v.x / 20f; obs[k++] = v.y / 20f; obs[k++] = v.z / 20f;
        obs[k++] = w.x / 5f;  obs[k++] = w.y / 5f;  obs[k++] = w.z / 5f;
        obs[k++] = g.x; obs[k++] = g.y; obs[k++] = g.z;
        obs[k++] = transform.position.x / trackHalfWidth;

        // joint angles (10), then joint angular velocities (10)
        for (int l = 0; l < 2; l++)
        {
            k = JointObs(obs, k, thigh[l], 3, false);
            k = JointObs(obs, k, shin[l], 1, false);
            k = JointObs(obs, k, board[l], 1, false);
        }
        for (int l = 0; l < 2; l++)
        {
            k = JointObs(obs, k, thigh[l], 3, true);
            k = JointObs(obs, k, shin[l], 1, true);
            k = JointObs(obs, k, board[l], 1, true);
        }
        for (int i = 0; i < ActionSize && k < ObservationSize; i++) obs[k++] = prevAction[i];   // previous action (10)
        return obs;
    }

    // joint angle (normalised to [-1,1] over its limit range) or angular velocity (rad/s / 10);
    // 3 dof for hips, 1 otherwise
    int JointObs(float[] obs, int k, ArticulationBody ab, int dof, bool velocity)
    {
        var jp = velocity ? ab.jointVelocity : ab.jointPosition;
        for (int d = 0; d < dof && k < obs.Length; d++)
        {
            if (velocity)
            {
                obs[k++] = Mathf.Clamp(jp[d] / 10f, -3f, 3f);
                continue;
            }
            var drive = d == 0 ? ab.xDrive : d == 1 ? ab.yDrive : ab.zDrive;
            float deg = jp[d] * Mathf.Rad2Deg;
            float mid = (drive.lowerLimit + drive.upperLimit) * 0.5f;
            float half = Mathf.Max(1f, (drive.upperLimit - drive.lowerLimit) * 0.5f);
            obs[k++] = Mathf.Clamp((deg - mid) / half, -1.5f, 1.5f);
        }
        return k;
    }

    // ------------------------------------------------------------------ reward
    public float ComputeReward()
    {
        float z = transform.position.z;
        float progress = (z - lastZ) / 10f;
        lastZ = z;

        float effort = 0f;
        for (int i = 0; i < ActionSize; i++) effort += Action[i] * Action[i];

        float r = progress
                + 0.002f * Mathf.Clamp01(transform.up.y)   // staying upright
                - 0.0005f * effort                         // energy penalty
                - 0.001f;                                  // time penalty
        if (Finished) r += 10f;
        if (Failed) r -= 5f;
        return r;
    }

    // ------------------------------------------------------------------ reset
    public void ResetEpisode()
    {
        Finished = Failed = TimedOut = false;
        doneTimer = 0f;
        FailReason = "";
        episodeTime = 0f;
        System.Array.Clear(Action, 0, ActionSize);
        System.Array.Clear(prevAction, 0, ActionSize);

        Vector3 pos = startPoint != null ? startPoint.position : new Vector3(0f, 2f, 0f);
        pos.x += Random.Range(-startOffsetRange, startOffsetRange);
        pos.y += 0.45f;   // torso centre ~1.4 m above the snow (crouched stance is ~1.27 m)

        torso.TeleportRoot(pos, Quaternion.Euler(0f, StartYaw, 0f));
        torso.linearVelocity = Vector3.zero;
        torso.angularVelocity = Vector3.zero;
        for (int i = 0; i < 2; i++)
        {
            ZeroJoint(thigh[i], 3);
            ZeroJoint(shin[i], 1);
            ZeroJoint(board[i], 1);
        }
        ApplyAction(Action);
        lastZ = pos.z;

        // drop in already moving downhill
        if (startSpeed > 0f)
        {
            Vector3 dir = startPoint != null ? startPoint.forward : Vector3.forward;
            foreach (var b in allBodies) b.linearVelocity = dir * startSpeed;
        }

        smBrake = smKeySteer = 0f;
        haveHeading = false;
        headingErrSum = 0f;
        jumpBufferTimer = jumpCooldownTimer = airTimer = landTimer = smCrouch = 0f;
        Jumps = 0;
        justReset = true;
        if (drawn != null) drawn.SnapNextStep();   // don't draw a swoop from the old position
    }

    bool justReset;

    // put a joint at the middle of its range (the neutral crouch) with no velocity
    void ZeroJoint(ArticulationBody ab, int dof)
    {
        ab.linearVelocity = Vector3.zero;
        ab.angularVelocity = Vector3.zero;
        float[] p = new float[3];
        for (int d = 0; d < dof; d++)
        {
            var drive = d == 0 ? ab.xDrive : d == 1 ? ab.yDrive : ab.zDrive;
            p[d] = (drive.lowerLimit + drive.upperLimit) * 0.5f * Mathf.Deg2Rad;
        }
        switch (dof)
        {
            case 1:
                ab.jointPosition = new ArticulationReducedSpace(p[0]);
                ab.jointVelocity = new ArticulationReducedSpace(0f);
                break;
            case 3:
                ab.jointPosition = new ArticulationReducedSpace(p[0], p[1], p[2]);
                ab.jointVelocity = new ArticulationReducedSpace(0f, 0f, 0f);
                break;
        }
    }

    // distance to the ground below the torso, ignoring the rider's own colliders
    float HeightAboveGround()
    {
        float best = 50f;
        foreach (var hit in Physics.RaycastAll(transform.position, Vector3.down, 50f))
        {
            if (hit.collider.transform.IsChildOf(transform)) continue;
            if (hit.collider.isTrigger) continue;
            best = Mathf.Min(best, hit.distance);
        }
        return best;
    }

    // ------------------------------------------------------------------ helpers
    void Tune(ArticulationBody ab, bool x, bool y, bool z)
    {
        if (x) ab.xDrive = TuneDrive(ab.xDrive);
        if (y) ab.yDrive = TuneDrive(ab.yDrive);
        if (z) ab.zDrive = TuneDrive(ab.zDrive);
    }

    ArticulationDrive TuneDrive(ArticulationDrive d)
    {
        d.stiffness = stiffness; d.damping = damping; d.forceLimit = forceLimit;
        return d;
    }

    // map action [-1,1] onto this joint axis's limit range
    static void SetTarget(ArticulationBody ab, int axis, float a)
    {
        var d = axis == 0 ? ab.xDrive : axis == 1 ? ab.yDrive : ab.zDrive;
        d.target = Mathf.Lerp(d.lowerLimit, d.upperLimit, (Mathf.Clamp(a, -1f, 1f) + 1f) * 0.5f);
        if (axis == 0) ab.xDrive = d; else if (axis == 1) ab.yDrive = d; else ab.zDrive = d;
    }

    // ------------------------------------------------------------------ manual play
    // A/D: steer by moving the front and back feet to opposite sides (hip roll), which turns the board.
    // S: brake (with A/D: drift). W: tuck (crouch, less drag, faster). R: reset.
    // Everything here is for playing by hand; the learning agent sets the 11 actions itself.
    [Header("Manual play")]
    // Steering (manual / high-level play): A/D ask for a TURN RATE and a yaw torque on the body tracks it, like
    // arcade snowboard games. It is symmetric left/right by design (the earlier foot-shifting steering was lopsided
    // because of the sideways stance), and the board's grip on the snow still turns the momentum, so carving and
    // drifting stay physical. In the air, A/D spin the rider. The joint-level agent steers with its own legs instead.
    public float turnRate = 45f;             // deg/s at full A/D
    public float steerRefSpeed = 12f;        // above this speed the turn rate is scaled down (refSpeed / speed, min 0.5)
    public float inputSmoothing = 0.12f;     // seconds for keys to ramp in/out (a sudden snap can tip the rider)
    public float yawTorqueGain = 160f;       // N*m per rad/s of turn-rate error (40 was too weak to overcome the
                                             // rider's natural pull left, so right turns were much slower than left)
    public float maxYawTorque = 200f;        // N*m cap

    // Balance assist (manual play ONLY - the agent must learn to balance itself): a gentle torque on the torso
    // that keeps the body upright and damps tumbling, on the snow and in the air (spin about the vertical is
    // left alone). This replaced an earlier version that balanced by rocking the ankles: that version made the
    // board buzz (the screen-shake) and tripped on steep ground. Measured: ~70% less vibration, and the rider now
    // rides up and over steep hills instead of falling.
    public bool balanceAssist = true;
    public float uprightStrength = 300f;     // N*m per radian of tilt
    public float uprightDamping = 60f;       // N*m per rad/s of tumbling

    // Carve limit (manual play ONLY): the keys can't steer the board past this angle from straight downhill, so it
    // can't turn uphill or spin round. Steering fades out over the last carveFadeAngle degrees (so momentum doesn't
    // carry it past), and if you keep pushing at the limit the heading hold keeps it there. The agent is not limited.
    // (Past ~90 deg the board is riding backwards and steering works in reverse, so it must never get that far.)
    public float maxCarveAngle = 70f;
    public float carveFadeAngle = 25f;

    // Heading hold (manual play ONLY): the sideways stance isn't symmetric, so on its own the rider slowly turns
    // left. When no steer key is held, gently counter-steer to keep the direction the player last chose, so the
    // board goes where you point it (straight lines, clean exits from turns, straight hockey stops).
    public bool headingHold = true;
    public float headingHoldRate = 3f;       // deg/s of turn asked for per degree of heading error
    public float headingTrim = 0.5f;         // extra deg/s per degree-second of accumulated error: learns the
                                             // constant correction that cancels the rider's natural pull left

    // Jump (Space): an ollie. While the board is on the snow, pop the whole rider upward, angled halfway between
    // straight up and the slope's surface normal. jumpSpeed 4 m/s gives roughly 0.8 m of height and ~0.8 s of air.
    public float jumpSpeed = 4f;             // m/s added at take-off
    public float jumpCooldown = 0.6f;        // s between jumps
    public float jumpBuffer = 0.15f;         // s: a press this long before landing still counts
    // Landing (manual play ONLY): on touchdown the legs auto-crouch to soak up the impact.
    public float landingAbsorbTime = 0.35f;  // s of automatic crouch after landing (fades out, so the legs don't spring)
    public float crouchSmoothing = 0.15f;    // s for the crouch (W or landing) to blend in/out; snapping the legs straight acts like a jump

    // joint targets for the W crouch, as actions in [-1,1] (hip -35 deg, knee +70 deg, ankle -32 deg)
    public float crouchHip = -0.375f, crouchKnee = 0.556f, crouchAnkle = -0.25f;

    // raw key state, set every frame by ReadKeyboard (or SetKeys) and turned into actions every physics step
    float keySteer, keyBrake;
    bool keyCrouch;
    float smBrake, smKeySteer;
    float holdHeading, headingErrSum;
    float jumpBufferTimer, jumpCooldownTimer, airTimer, landTimer, smCrouch;
    public int Jumps { get; private set; }   // jumps taken this episode (handy for tests/rewards)
    bool haveHeading;

    void ReadKeyboard()
    {
        var kb = Keyboard.current;
        if (kb == null) return;
        float steering = ((kb.dKey.isPressed || kb.rightArrowKey.isPressed) ? 1f : 0f)
                       - ((kb.aKey.isPressed || kb.leftArrowKey.isPressed) ? 1f : 0f);
        ApplyActions(steering, kb.wKey.isPressed, kb.sKey.isPressed);
        if (kb.spaceKey.wasPressedThisFrame) RequestJump();
        if (kb.rKey.wasPressedThisFrame) ResetEpisode();
    }

    // Ask for a jump; it happens on the next physics step where the board is on the snow (within jumpBuffer s).
    public void RequestJump() => jumpBufferTimer = jumpBuffer;

    // Is either half of the board touching (or within a few cm of) the snow? Also returns the surface normal.
    public bool IsGrounded(out Vector3 normal)
    {
        normal = Vector3.up;
        foreach (var b in board)
        {
            foreach (var hit in Physics.RaycastAll(b.transform.position + Vector3.up * 0.1f, Vector3.down, 0.35f))
            {
                if (hit.collider.isTrigger || hit.collider.transform.IsChildOf(transform)) continue;
                normal = hit.normal;
                return true;
            }
        }
        return false;
    }

    // Ground following (manual / high-level play ONLY, like the other assists). A real snow ramp has a rounded
    // transition; the sculpted terrain has a sharp kink. Hitting a kink head-on, the physics deletes the speed that
    // points into the slope: measured 12.7 -> 9.7 m/s at the foot of a 39 degree ramp, so the rider ran out of energy
    // and rolled back. Just before the board reaches a steeper surface, turn the velocity along it, keeping the speed.
    // Crests are left alone (no sticking to the ground), so riding off a lip still launches you.
    [Header("Ground following (manual play)")]
    public bool groundFollow = true;
    public float groundFollowMaxAngle = 55f;   // steeper than this is a wall, not a slope: let it crash
    public float groundLookAheadSteps = 2f;    // how many physics steps ahead of the nose to look
    public float groundFollowMinInto = 0.35f;  // m/s: only act on a real change of slope. Gravity alone pushes ~0.1 m/s into
                                               // the snow every step; cancelling THAT removes the contact force that levels
                                               // the board, and a crouched board then stays rolled onto its edge.

    void FollowGround(float dt)
    {
        Vector3 v = torso.linearVelocity;
        float speed = v.magnitude;
        Vector3 flat = new Vector3(v.x, 0f, v.z);
        if (speed < 1f || flat.magnitude < 0.5f) return;
        Vector3 dir = v / speed;

        Vector3 centre = (board[0].transform.position + board[1].transform.position) * 0.5f;
        Vector3 nose = board[0].transform.position - board[0].transform.right * 0.7f;           // tip of the nose
        Vector3 ahead = nose + flat.normalized * (flat.magnitude * dt * groundLookAheadSteps);

        Vector3 n = Vector3.up;
        bool haveNow = GroundBelow(centre + Vector3.up * 0.3f, 1f, out RaycastHit hitNow);
        bool haveAhead = GroundBelow(ahead + Vector3.up * 1f, 3f, out RaycastHit hitAhead);
        if (!haveNow && !haveAhead) return;
        if (haveNow && haveAhead) n = Vector3.Dot(hitNow.normal, dir) <= Vector3.Dot(hitAhead.normal, dir) ? hitNow.normal : hitAhead.normal;
        else n = haveNow ? hitNow.normal : hitAhead.normal;

        float into = Vector3.Dot(v, n);
        if (into >= -groundFollowMinInto) return;                                                // not heading into the surface (or only gravity's tiny push)
        float steepness = Mathf.Acos(Mathf.Clamp(n.y, -1f, 1f)) * Mathf.Rad2Deg;                 // slope angle of that surface
        float hitAngle = Mathf.Asin(Mathf.Clamp(-into / speed, 0f, 1f)) * Mathf.Rad2Deg;          // how hard we're heading into it
        if (steepness > groundFollowMaxAngle || hitAngle > groundFollowMaxAngle) return;

        Vector3 along = v - n * into;
        if (along.sqrMagnitude < 1e-4f) return;
        Vector3 delta = along.normalized * speed - v;
        foreach (var b in allBodies) b.linearVelocity += delta;
    }

    // nearest ground surface below a point (ignores the rider's own colliders and triggers)
    bool GroundBelow(Vector3 from, float maxDist, out RaycastHit best)
    {
        best = default;
        bool found = false;
        float bestDist = float.MaxValue;
        foreach (var hit in Physics.RaycastAll(from, Vector3.down, maxDist))
        {
            if (hit.collider.isTrigger || hit.collider.transform.IsChildOf(transform)) continue;
            if (hit.distance < bestDist) { bestDist = hit.distance; best = hit; found = true; }
        }
        return found;
    }

    // High-level action interface (used by the keyboard, scripted demos, and an RL agent in mode 2).
    //   steering  : -1 = full left, 0 = straight, +1 = full right (any value in between is allowed)
    //   isTucking : crouch into a tuck - less air drag, higher top speed
    //   isBraking : brake; combined with steering it throws the tail out into a drift
    // Call it every step before Tick(). It only stores the command; ManualStep() turns it into joint targets,
    // with input smoothing, speed-sensitive steering and the balance assist.
    //   isJumping : ollie (only takes effect while the board is on the snow; see jumpSpeed/jumpCooldown)
    public void ApplyActions(float steering, bool isTucking, bool isBraking, bool isJumping = false)
    {
        keySteer = Mathf.Clamp(steering, -1f, 1f);
        keyCrouch = isTucking;
        keyBrake = isBraking ? 1f : 0f;
        if (isJumping) RequestJump();
    }

    // Turns the current keys into the 11 actions. Runs once per physics step.
    void ManualStep()
    {
        float dt = Time.fixedDeltaTime;
        // jump: pop upward if the board is on the snow and the cooldown has passed
        jumpBufferTimer -= dt;
        jumpCooldownTimer -= dt;
        bool grounded = IsGrounded(out Vector3 groundNormal);
        if (jumpBufferTimer > 0f && jumpCooldownTimer <= 0f && grounded)
        {
            Vector3 pop = Vector3.Lerp(Vector3.up, groundNormal, 0.5f).normalized * jumpSpeed;
            foreach (var b in allBodies) b.linearVelocity += pop;
            jumpBufferTimer = 0f;
            jumpCooldownTimer = jumpCooldown;
            Jumps++;
            grounded = false;
        }

        if (groundFollow && grounded) FollowGround(dt);

        // landing absorb. Only count as a real jump after 0.3 s off the snow: at speed the board
        // loses contact for a frame or two over bumps, and treating that as a jump destabilises normal riding.
        if (!grounded) airTimer += dt;
        // balance assist: keep the body upright (on the snow and in the air)
        if (balanceAssist)
        {
            Vector3 tilt = Vector3.Cross(transform.up, Vector3.up);            // axis * sin(angle) back to upright
            Vector3 w = torso.angularVelocity;
            Vector3 tumble = w - Vector3.up * Vector3.Dot(w, Vector3.up);     // ignore spin about the vertical
            torso.AddTorque(tilt * uprightStrength - tumble * uprightDamping);
        }
        if (grounded)
        {
            if (airTimer > 0.3f) landTimer = landingAbsorbTime;   // landed from a real jump/drop
            airTimer = 0f;
        }
        landTimer -= dt;

        float blend = Mathf.Min(1f, dt / Mathf.Max(inputSmoothing, 1e-3f));
        smBrake += (keyBrake - smBrake) * blend;
        smKeySteer += (keySteer - smKeySteer) * blend;

        Vector3 v = torso.linearVelocity; v.y = 0f;
        float speedScale = Mathf.Clamp(steerRefSpeed / Mathf.Max(v.magnitude, 0.1f), 0.5f, 1f);
        float maxRate = turnRate * speedScale;
        // desired turn rate (deg/s, + = right); braking while steering swings the board round faster (drift)
        float target = smKeySteer * maxRate * (1f + brakeSteerBoost * smBrake);

        // heading hold: while steering, remember where the board points; when the keys are released, hold it
        Vector3 hd = Heading;
        float heading = Mathf.Atan2(hd.x, hd.z) * Mathf.Rad2Deg;
        if (!haveHeading || episodeTime < 0.1f)
        {
            // start by holding the start direction (straight downhill), not whatever the board points at in the
            // first wobbly instant after reset
            Vector3 sd = startPoint != null ? startPoint.forward : Vector3.forward;
            holdHeading = Mathf.Atan2(sd.x, sd.z) * Mathf.Rad2Deg;
            haveHeading = true;
        }
        // turn rate (deg/s, + = right): the physics engine's angular velocity of the board halves about the vertical.
        // (It used to be the heading change per step, which amplified tiny wobbles 1/dt times: measured 2x the
        // steering-torque chatter and 4x the heading wobble.)
        float yawRate = 0.5f * (board[0].angularVelocity.y + board[1].angularVelocity.y) * Mathf.Rad2Deg;
        // carve limit: measured from straight downhill (the start direction)
        Vector3 down = startPoint != null ? startPoint.forward : Vector3.forward;
        float downHeading = Mathf.Atan2(down.x, down.z) * Mathf.Rad2Deg;
        float fromDown = Mathf.DeltaAngle(downHeading, heading);
        bool pressing = Mathf.Abs(keySteer) > 0.01f;
        bool pushingOut = pressing && Mathf.Sign(keySteer) == Mathf.Sign(fromDown);   // steering further from downhill
        if (pushingOut)
            target *= Mathf.Clamp01((maxCarveAngle - Mathf.Abs(fromDown)) / Mathf.Max(carveFadeAngle, 1f));
        bool steering = pressing && !(pushingOut && Mathf.Abs(fromDown) >= maxCarveAngle - 1f);

        if (steering)
            holdHeading = downHeading + Mathf.Clamp(fromDown, -maxCarveAngle, maxCarveAngle);
        else if (headingHold)
        {
            if (pushingOut) holdHeading = downHeading + Mathf.Sign(fromDown) * maxCarveAngle;   // pinned at the limit
            float err = Mathf.DeltaAngle(heading, holdHeading);
            float trimLimit = maxRate / Mathf.Max(headingTrim, 1e-4f);
            headingErrSum = Mathf.Clamp(headingErrSum + err * dt, -trimLimit, trimLimit);   // anti-windup
            target = Mathf.Clamp(headingHoldRate * err + headingTrim * headingErrSum, -maxRate, maxRate);
        }

        // turn-rate controller: yaw torque on the body to make the board turn at the target rate
        float yawTorque = Mathf.Clamp(yawTorqueGain * (target - yawRate) * Mathf.Deg2Rad, -maxYawTorque, maxYawTorque);
        torso.AddTorque(Vector3.up * yawTorque);
        DebugYawTorque = yawTorque;
        DebugYawRate = yawRate;

        System.Array.Clear(Action, 0, ActionSize);   // hips/knees/ankles: crouch only (set below); steering is the torque above
        // S alone = full brake (hard stop); S while steering = part brake, so the board drifts round the turn
        Action[BrakeIndex] = smBrake * Mathf.Lerp(1f, driftBrakeFraction, Mathf.Abs(smKeySteer));

        // W: a coordinated crouch. Hips, knees and ankles move together so the hips stay over the feet and the
        // board stays flat (bending only the knees throws the weight off the board and it falls).
        // crouch amount: W, or the automatic knee-bend just after landing (fading out), blended smoothly
        float crouchTarget = Mathf.Max(keyCrouch ? 1f : 0f, Mathf.Clamp01(landTimer / Mathf.Max(landingAbsorbTime, 1e-3f)));
        smCrouch += (crouchTarget - smCrouch) * Mathf.Min(1f, dt / Mathf.Max(crouchSmoothing, 1e-3f));
        Action[0] = Action[5] = crouchHip * smCrouch;
        Action[3] = Action[8] = crouchKnee * smCrouch;
        // ankles: only the crouch's flat-board angle (balance is done by the torso assist, not by rocking the ankles)
        Action[4] = Action[9] = Mathf.Clamp(crouchAnkle * smCrouch, -1f, 1f);
    }
}
