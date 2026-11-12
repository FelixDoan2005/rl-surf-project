using UnityEngine;

// Third-person chase camera: sits behind and above the target and looks at it.
//
// Smoothness: the snowboarder is drawn blended between physics steps (ArticulationInterpolation) and this camera
// follows exactly that drawn pose every frame, so the rider can't judder against the camera or the background.
// The aim point and position are smoothed on top, so bumps and landings don't shake the whole screen.
public class FollowCamera : MonoBehaviour
{
    public Transform target;                 // auto-found as "SnowboarderAgent" if empty
    public float distance = 10f;             // behind the target
    public float height = 5f;                // above the target
    public float lookHeight = 1f;            // aim point above the target's centre
    public float positionSmoothing = 0.15f;  // seconds, larger = lazier
    public float rotationSpeed = 5f;         // how fast the camera swings behind the heading
    public float lookSmoothing = 0.05f;      // seconds of smoothing on the aim point (more absorbs bumps but lags; 0 = rigid)

    Vector3 velocity, lookVelocity, lookPoint;
    Vector3 flatForward = Vector3.forward;
    bool initialised;
    SnowboarderAgent rider;
    ArticulationInterpolation drawn;

    void FindTarget()
    {
        if (target == null)
        {
            var go = GameObject.Find("SnowboarderAgent");
            if (go != null) target = go.transform;
        }
        if (target == null) return;
        if (rider == null || rider.transform != target) rider = target.GetComponent<SnowboarderAgent>();
        if (drawn == null || drawn.transform != target) drawn = target.GetComponent<ArticulationInterpolation>();
    }

    // where the target is drawn this frame
    Vector3 TargetPosition() => drawn != null && drawn.isActiveAndEnabled && drawn.Ready ? drawn.Position : target.position;

    Vector3 TargetForward()
    {
        // The snowboarder faces sideways, so follow the board's direction, not the torso's.
        Vector3 f = rider != null ? rider.Heading : Vector3.ProjectOnPlane(target.forward, Vector3.up);
        return f.sqrMagnitude > 1e-4f ? f.normalized : flatForward;
    }

    void LateUpdate()
    {
        FindTarget();
        if (target == null) return;
        Vector3 pos = TargetPosition();
        Vector3 fwd = TargetForward();

        // first frame, or the rider teleported (reset): snap instead of swooping across the map
        if (!initialised || (pos - lookPoint).sqrMagnitude > 25f * 25f)
        {
            initialised = true;
            lookPoint = pos;
            flatForward = fwd;
            velocity = lookVelocity = Vector3.zero;
            transform.position = pos - fwd * distance + Vector3.up * height;
        }

        flatForward = Vector3.Slerp(flatForward, fwd, 1f - Mathf.Exp(-rotationSpeed * Time.deltaTime));
        lookPoint = lookSmoothing > 0f ? Vector3.SmoothDamp(lookPoint, pos, ref lookVelocity, lookSmoothing) : pos;
        Vector3 desired = lookPoint - flatForward * distance + Vector3.up * height;
        transform.position = positionSmoothing > 0f
            ? Vector3.SmoothDamp(transform.position, desired, ref velocity, positionSmoothing)
            : desired;
        transform.LookAt(lookPoint + Vector3.up * lookHeight);
    }
}
