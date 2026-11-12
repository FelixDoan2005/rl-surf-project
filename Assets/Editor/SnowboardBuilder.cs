#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// Menu: Surf > Build Snowboarder. Builds the jointed snowboarder from primitives (ArticulationBody joints).
// The rider faces sideways: the whole body is yawed 90 degrees so torso local -X is the board's nose.
// Every body GameObject sits at its joint pivot with unit scale; shapes live on a scaled "Visual" child.
public static class SnowboardBuilder
{
    const float TorsoW = 0.5f, TorsoH = 0.7f, TorsoD = 0.3f;
    const float HipX = 0.25f;                    // feet are 0.5 m apart along the board
    const float ThighLen = 0.45f, ShinLen = 0.45f;
    const float LimbRadius = 0.08f;

    [MenuItem("Surf/Build Snowboarder")]
    public static void Build()
    {
        foreach (var n in new[] { "SnowboarderAgent", "SkierAgent" })
        {
            var old = GameObject.Find(n);
            if (old != null) Undo.DestroyObjectImmediate(old);
        }

        var start = GameObject.Find("StartPoint");
        Vector3 pos = start != null ? start.transform.position + new Vector3(0f, 0.45f, 0f) : new Vector3(0f, 2f, 0f);
        Quaternion rot = Quaternion.Euler(0f, 90f, 0f);

        var limbMat = GetMaterial("Limb", new Color(0.2f, 0.45f, 0.85f));
        var bodyMat = GetMaterial("Agent", new Color(0.9f, 0.3f, 0.1f));
        var boardMat = GetMaterial("Board", new Color(0.1f, 0.5f, 0.45f));
        var boardPhys = GetPhysicsMaterial("Board", 0.02f);   // well-waxed board on snow

        // ---- torso (articulation root) ----
        var root = new GameObject("SnowboarderAgent");
        root.transform.SetPositionAndRotation(pos, rot);
        Undo.RegisterCreatedObjectUndo(root, "Build Snowboarder");
        var rootAb = root.AddComponent<ArticulationBody>();
        rootAb.immovable = false;
        rootAb.mass = 6f;   // light torso + heavier board keeps the centre of mass low
        AddShape(root.transform, PrimitiveType.Cube, Vector3.zero, new Vector3(TorsoW, TorsoH, TorsoD), bodyMat, null);

        var allBodies = new System.Collections.Generic.List<ArticulationBody> { rootAb };

        // sideSign -1 = front foot (torso local -X = downhill), +1 = back foot
        foreach (var sideSign in new[] { -1f, 1f })
        {
            string s = sideSign < 0 ? "F" : "B";
            Vector3 hipWorld = pos + rot * new Vector3(sideSign * HipX, -TorsoH / 2f, 0f);

            // thigh: hip joint (pitch x, yaw y, roll z)
            var thigh = NewBody("Thigh" + s, root.transform, hipWorld, rot, 3f, allBodies);
            Spherical(thigh, root.transform.InverseTransformPoint(hipWorld),
                      new Vector2(-60f, 20f), new Vector2(-30f, 30f), new Vector2(-25f, 25f));
            AddShape(thigh.transform, PrimitiveType.Capsule, new Vector3(0f, -ThighLen / 2f, 0f),
                     new Vector3(LimbRadius * 2f, ThighLen / 2f, LimbRadius * 2f), limbMat, null);

            // shin: knee (revolute, bends toward the rider's back)
            Vector3 kneeWorld = hipWorld + new Vector3(0f, -ThighLen, 0f);
            var shin = NewBody("Shin" + s, thigh.transform, kneeWorld, rot, 2f, allBodies);
            Revolute(shin, thigh.transform.InverseTransformPoint(kneeWorld), 0f, 90f);
            AddShape(shin.transform, PrimitiveType.Capsule, new Vector3(0f, -ShinLen / 2f, 0f),
                     new Vector3(LimbRadius * 2f, ShinLen / 2f, LimbRadius * 2f), limbMat, null);

            // board half: ankle (revolute about the board's long axis = rolls onto an edge)
            Vector3 ankleWorld = kneeWorld + new Vector3(0f, -ShinLen, 0f);
            var board = NewBody("Board" + s, shin.transform, ankleWorld, rot, 2f, allBodies);
            // range centred on -22 deg: the most stable neutral found by experiment (was -25)
            Revolute(board, shin.transform.InverseTransformPoint(ankleWorld), -62f, 18f);
            // each half spans from just past the other foot's midpoint out to the nose/tail
            AddShape(board.transform, PrimitiveType.Cube, new Vector3(sideSign * 0.13f, -0.065f, 0f),
                     new Vector3(0.74f, 0.05f, 0.45f), boardMat, boardPhys);   // wide for stability
            AddRocker(board.transform, sideSign, boardMat, boardPhys);
        }

        foreach (var ab in allBodies)
        {
            ab.solverIterations = 16;
            ab.solverVelocityIterations = 4;
            ab.linearDamping = 0.05f;
            ab.angularDamping = 0.05f;
            ab.jointFriction = 0.05f;
        }

        var type = System.Type.GetType("SnowboarderAgent, Assembly-CSharp");
        if (type != null)
        {
            var comp = root.AddComponent(type);
            if (Camera.main != null)   // fill the inspector's Follow Camera slot
            {
                var so = new SerializedObject(comp);
                so.FindProperty("followCamera").objectReferenceValue = Camera.main;
                so.ApplyModifiedProperties();
            }
        }
        else Debug.LogWarning("SnowboarderAgent script not compiled yet; add it to the object manually.");

        Selection.activeGameObject = root;
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log("Snowboarder built. Save the scene (Ctrl+S).");
    }

    static ArticulationBody NewBody(string name, Transform parent, Vector3 worldPos, Quaternion worldRot, float mass,
                                    System.Collections.Generic.List<ArticulationBody> list)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, true);
        go.transform.SetPositionAndRotation(worldPos, worldRot);
        var ab = go.AddComponent<ArticulationBody>();
        ab.mass = mass;
        list.Add(ab);
        return ab;
    }

    // Unity's default joint frame is rotated 90 degrees about Z, which puts the hinge axis on the body's
    // vertical axis. Set the frames explicitly so joint X/Y/Z = body X/Y/Z (all bodies share one rotation).
    static void SetFrames(ArticulationBody ab, Vector3 parentPivotLocal)
    {
        ab.matchAnchors = false;
        ab.anchorPosition = Vector3.zero;
        ab.anchorRotation = Quaternion.identity;
        ab.parentAnchorPosition = parentPivotLocal;
        ab.parentAnchorRotation = Quaternion.identity;
    }

    static void Revolute(ArticulationBody ab, Vector3 parentPivotLocal, float lo, float hi)
    {
        ab.jointType = ArticulationJointType.RevoluteJoint;
        SetFrames(ab, parentPivotLocal);
        ab.twistLock = ArticulationDofLock.LimitedMotion;
        ab.xDrive = Drive(lo, hi);
    }

    static void Spherical(ArticulationBody ab, Vector3 parentPivotLocal, Vector2 x, Vector2 y, Vector2 z)
    {
        ab.jointType = ArticulationJointType.SphericalJoint;
        SetFrames(ab, parentPivotLocal);
        ab.twistLock = ArticulationDofLock.LimitedMotion;
        ab.swingYLock = ArticulationDofLock.LimitedMotion;
        ab.swingZLock = ArticulationDofLock.LimitedMotion;
        ab.xDrive = Drive(x.x, x.y);
        ab.yDrive = Drive(y.x, y.y);
        ab.zDrive = Drive(z.x, z.y);
    }

    static ArticulationDrive Drive(float lo, float hi)
    {
        return new ArticulationDrive
        {
            lowerLimit = lo, upperLimit = hi,
            stiffness = 2000f, damping = 300f, forceLimit = 1000f,
            target = (lo + hi) * 0.5f
        };
    }

    // Upturned nose/tail at the outer tip of a board half (sideSign -1 = nose of the front half, +1 = tail of the
    // back half), angled up 25 degrees. Like a real board's rocker, it lets the board ride up onto steep ground
    // instead of the flat box corner digging in and tripping the rider.
    public static void AddRocker(Transform boardHalf, float sideSign, Material mat, PhysicsMaterial pm)
    {
        var old = boardHalf.Find("Rocker");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var r = GameObject.CreatePrimitive(PrimitiveType.Cube);
        r.name = "Rocker";
        r.transform.SetParent(boardHalf, false);
        r.transform.localPosition = new Vector3(sideSign * (0.13f + 0.37f + 0.08f), -0.04f, 0f);
        r.transform.localRotation = Quaternion.Euler(0f, 0f, sideSign * 25f);
        r.transform.localScale = new Vector3(0.2f, 0.03f, 0.45f);
        r.GetComponent<Renderer>().sharedMaterial = mat;
        if (pm != null) r.GetComponent<Collider>().sharedMaterial = pm;
    }

    static void AddShape(Transform parent, PrimitiveType type, Vector3 localPos, Vector3 scale, Material mat, PhysicsMaterial pm)
    {
        var v = GameObject.CreatePrimitive(type);
        v.name = "Visual";
        v.transform.SetParent(parent, false);
        v.transform.localPosition = localPos;
        v.transform.localScale = scale;
        v.GetComponent<Renderer>().sharedMaterial = mat;
        if (pm != null) v.GetComponent<Collider>().sharedMaterial = pm;
    }

    static Material GetMaterial(string name, Color c)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
        string path = $"Assets/Materials/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        m.SetColor("_BaseColor", c);
        m.color = c;
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static PhysicsMaterial GetPhysicsMaterial(string name, float friction)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
        string path = $"Assets/Materials/{name}.physicMaterial";
        var pm = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
        if (pm != null) return pm;
        pm = new PhysicsMaterial(name)
        {
            dynamicFriction = friction, staticFriction = friction,
            frictionCombine = PhysicsMaterialCombine.Minimum
        };
        AssetDatabase.CreateAsset(pm, path);
        return pm;
    }
}
#endif
