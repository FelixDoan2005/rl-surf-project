using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Smooth drawing for an ArticulationBody rig (the snowboarder). Purely visual: physics, colliders and the agent are
// untouched, and nothing changes for training without rendering.
//
// Unity interpolates Rigidbodies between physics steps but NOT ArticulationBodies, so without this the rider is drawn
// exactly where the last physics step left it: at 12 m/s and 100 steps/s it jumps 12 cm at a time while the camera
// glides smoothly, and the rider and board appear to judder/shake. Here every mesh is drawn as a copy blended between
// the last two physics steps (what Rigidbody interpolation does), and the original renderers are hidden.
// SnowboarderAgent adds this automatically when the game starts (its "Smooth Rendering" option).
[DefaultExecutionOrder(-100)]   // place the copies before the camera (order 0) looks at them in LateUpdate
public class ArticulationInterpolation : MonoBehaviour
{
    public bool interpolate = true;   // false = draw the raw physics poses (for comparison)

    // Bodies drawn as ONE rigid piece at their average pose. The snowboard is two physics halves held together by a
    // spring; over bumps they flex against each other by a few mm, which looks like the board glitching. Drawn as a
    // group, the board stays visibly solid. (SnowboarderAgent fills this in with its two board halves.)
    public Transform[] rigidGroup;

    class Body { public Transform t; public Vector3 p0, p1, p; public Quaternion r0, r1, r; }
    class Piece { public Body body; public Transform copy; public Vector3 localPos; public Quaternion localRot; }

    readonly List<Body> bodies = new List<Body>();
    readonly List<Piece> pieces = new List<Piece>();
    readonly List<Body> groupMembers = new List<Body>();
    Body group;   // the rigid group's average pose (not a physics body itself)
    Body root;
    [System.NonSerialized] bool built;
    [System.NonSerialized] bool snapNext = true;
    // kept through a script reload during play, so the component can tidy up and rebuild
    [SerializeField, HideInInspector] Transform drawnRoot;
    [SerializeField, HideInInspector] List<Renderer> hidden = new List<Renderer>();
    const float TeleportDistance = 3f;   // a bigger jump in one step is a reset: don't blend across it

    public bool Ready => built;
    public Vector3 Position => built ? root.p : transform.position;          // where the rig's root is drawn this frame
    public Quaternion Rotation => built ? root.r : transform.rotation;

    // where a body of the rig is drawn this frame (falls back to its physics pose if it has no meshes)
    public Vector3 DrawnPosition(Transform body)
    {
        if (built) foreach (var b in bodies) if (b.t == body) return b.p;
        return body.position;
    }

    // Call after teleporting the rig (e.g. an episode reset) so the next step isn't blended with the old pose.
    public void SnapNextStep() => snapNext = true;

    void Start()
    {
        if (!built) Build();
    }

    void OnEnable()
    {
        if (built)
        {
            SetHidden(true);
            drawnRoot.gameObject.SetActive(true);
            snapNext = true;
            StartCoroutine(AfterPhysics());
        }
        else if (Application.isPlaying && (drawnRoot != null || hidden.Count > 0))
        {
            Build();   // scripts were reloaded during play: the old copies lost their links, so start again
        }
    }

    void OnDisable()
    {
        StopAllCoroutines();
        if (!built) return;
        SetHidden(false);
        if (drawnRoot != null) drawnRoot.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        SetHidden(false);
        if (drawnRoot != null) Destroy(drawnRoot.gameObject);
    }

    void Build()
    {
        // clear anything left over from before a script reload
        SetHidden(false);
        hidden.Clear();
        if (drawnRoot != null) Destroy(drawnRoot.gameObject);
        bodies.Clear(); pieces.Clear(); groupMembers.Clear(); group = null;

        drawnRoot = new GameObject(name + " (drawn)").transform;
        root = BodyFor(transform);
        foreach (var r in GetComponentsInChildren<MeshRenderer>())
        {
            var ab = r.GetComponentInParent<ArticulationBody>();
            var mf = r.GetComponent<MeshFilter>();
            if (ab == null || mf == null || mf.sharedMesh == null || !r.enabled) continue;
            Body b = BodyFor(ab.transform);

            var copy = new GameObject(r.name);
            copy.layer = r.gameObject.layer;
            copy.transform.SetParent(drawnRoot, false);
            copy.transform.localScale = r.transform.lossyScale;
            copy.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            var mr = copy.AddComponent<MeshRenderer>();
            mr.sharedMaterials = r.sharedMaterials;
            mr.shadowCastingMode = r.shadowCastingMode;
            mr.receiveShadows = r.receiveShadows;
            mr.lightProbeUsage = r.lightProbeUsage;
            mr.reflectionProbeUsage = r.reflectionProbeUsage;
            mr.renderingLayerMask = r.renderingLayerMask;

            Quaternion inv = Quaternion.Inverse(b.t.rotation);
            pieces.Add(new Piece
            {
                body = b, copy = copy.transform,
                localPos = inv * (r.transform.position - b.t.position),
                localRot = inv * r.transform.rotation
            });
            hidden.Add(r);
        }
        BuildRigidGroup();
        SetHidden(true);
        built = true;
        snapNext = true;
        Record();
        Apply(-1f);
        StopAllCoroutines();
        StartCoroutine(AfterPhysics());
    }

    // re-anchor the group members' meshes to the group's average pose, keeping today's layout as the rigid shape
    void BuildRigidGroup()
    {
        if (rigidGroup == null) return;
        foreach (var t in rigidGroup)
        {
            if (t == null || !t.IsChildOf(transform)) continue;
            var b = BodyFor(t);
            if (!groupMembers.Contains(b)) groupMembers.Add(b);
        }
        if (groupMembers.Count < 2) { groupMembers.Clear(); return; }
        group = new Body();
        AverageInto(group, true);
        Quaternion inv = Quaternion.Inverse(group.r);
        foreach (var pc in pieces)
        {
            if (!groupMembers.Contains(pc.body)) continue;
            Vector3 worldPos = pc.body.t.position + pc.body.t.rotation * pc.localPos;
            Quaternion worldRot = pc.body.t.rotation * pc.localRot;
            pc.localPos = inv * (worldPos - group.p);
            pc.localRot = inv * worldRot;
            pc.body = group;
        }
    }

    // group pose = mean position and (incrementally slerped) mean rotation of its members
    void AverageInto(Body g, bool fromTransforms)
    {
        Vector3 p = Vector3.zero;
        Quaternion r = Quaternion.identity;
        for (int i = 0; i < groupMembers.Count; i++)
        {
            var m = groupMembers[i];
            Vector3 mp = fromTransforms ? m.t.position : m.p;
            Quaternion mr = fromTransforms ? m.t.rotation : m.r;
            p += mp;
            r = i == 0 ? mr : Quaternion.Slerp(r, mr, 1f / (i + 1));
        }
        g.p = p / groupMembers.Count;
        g.r = r;
    }

    Body BodyFor(Transform t)
    {
        foreach (var b in bodies) if (b.t == t) return b;
        var nb = new Body { t = t, p0 = t.position, p1 = t.position, p = t.position, r0 = t.rotation, r1 = t.rotation, r = t.rotation };
        bodies.Add(nb);
        return nb;
    }

    // hide the original renderers while the copies are drawn (or show them again)
    void SetHidden(bool hide)
    {
        foreach (var r in hidden) if (r != null) r.enabled = !hide;
        if (!hide) hidden.RemoveAll(r => r == null);
    }

    // record every physics step's result (WaitForFixedUpdate resumes right after the physics step)
    IEnumerator AfterPhysics()
    {
        var wait = new WaitForFixedUpdate();
        while (true)
        {
            yield return wait;
            Record();
        }
    }

    void Record()
    {
        if (!built) return;
        bool snap = snapNext || (root.t.position - root.p1).sqrMagnitude > TeleportDistance * TeleportDistance;
        snapNext = false;
        foreach (var b in bodies)
        {
            Vector3 p = b.t.position;
            Quaternion r = b.t.rotation;
            b.p0 = snap ? p : b.p1;
            b.r0 = snap ? r : b.r1;
            b.p1 = p;
            b.r1 = r;
        }
    }

    void LateUpdate()
    {
        if (!built) return;
        // Blend only when Unity itself steps the physics; under a script-driven loop (training, tests) draw it raw.
        bool blend = interpolate && Physics.simulationMode == SimulationMode.FixedUpdate;
        Apply(blend ? Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime) : -1f);
    }

    void Apply(float a)   // a in [0,1] = blend from the previous to the latest physics step; a < 0 = raw physics pose
    {
        foreach (var b in bodies)
        {
            if (a < 0f) { b.p = b.t.position; b.r = b.t.rotation; }
            else { b.p = Vector3.Lerp(b.p0, b.p1, a); b.r = Quaternion.Slerp(b.r0, b.r1, a); }
        }
        if (group != null) AverageInto(group, false);
        foreach (var pc in pieces) pc.copy.SetPositionAndRotation(pc.body.p + pc.body.r * pc.localPos, pc.body.r * pc.localRot);
    }
}
