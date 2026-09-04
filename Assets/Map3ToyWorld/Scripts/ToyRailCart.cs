using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(BoxCollider))]
public sealed class ToyRailCart : MonoBehaviour, IWindUpReceiver
{
    public Transform railReference;
    public ToyRailSwitch railSwitch;
    public float driveAcceleration = 3f;
    public float maxRailSpeed = 12f;
    public float lateralCentering = 20f;
    public float lateralDamping = 7f;
    public float derailDistance = 2.2f;
    public float unalignedSwitchBrake = 24f;

    private Rigidbody body;
    private float pendingPower;
    private bool derailed;
    private RigidbodyConstraints railConstraints;

    public bool IsDerailed => derailed;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        railConstraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        body.constraints = railConstraints;
    }

    public void ApplyWindUpPower(float signedPower, float fixedDeltaTime)
    {
        pendingPower += signedPower;
    }

    private void FixedUpdate()
    {
        if (derailed || railReference == null)
        {
            pendingPower = 0f;
            return;
        }

        Vector3 forward = railReference.forward.normalized;
        Vector3 fromOrigin = body.position - railReference.position;
        Vector3 lateral = fromOrigin - Vector3.Project(fromOrigin, forward);
        lateral.y = 0f;

        if (lateral.magnitude > derailDistance)
        {
            Derail();
            pendingPower = 0f;
            return;
        }

        Vector3 lateralVelocity = body.velocity - Vector3.Project(body.velocity, forward);
        lateralVelocity.y = 0f;
        body.AddForce(-lateral * lateralCentering - lateralVelocity * lateralDamping, ForceMode.Acceleration);

        if (railSwitch != null && !railSwitch.IsAligned &&
            Vector3.Distance(new Vector3(body.position.x, 0f, body.position.z),
                             new Vector3(railSwitch.transform.position.x, 0f, railSwitch.transform.position.z)) < 4f)
        {
            float speedTowardSwitch = Vector3.Dot(body.velocity, forward);
            if (speedTowardSwitch > 0f)
                body.AddForce(-forward * unalignedSwitchBrake, ForceMode.Acceleration);
            pendingPower = 0f;
        }

        if (Mathf.Abs(pendingPower) > 0.0001f)
            body.AddForce(forward * (pendingPower * driveAcceleration), ForceMode.Acceleration);

        float forwardSpeed = Vector3.Dot(body.velocity, forward);
        if (Mathf.Abs(forwardSpeed) > maxRailSpeed)
        {
            Vector3 nonForward = body.velocity - forward * forwardSpeed;
            body.velocity = nonForward + forward * (Mathf.Sign(forwardSpeed) * maxRailSpeed);
        }

        pendingPower = 0f;
    }

    public void Derail()
    {
        if (derailed) return;
        derailed = true;
        body.constraints = RigidbodyConstraints.None;
        Debug.Log("[ToyWorld] Rail cart derailed and remains available as a free physics object.", this);
    }

    private void OnPuzzleReset()
    {
        derailed = false;
        pendingPower = 0f;
        body.constraints = railConstraints;
    }
}
