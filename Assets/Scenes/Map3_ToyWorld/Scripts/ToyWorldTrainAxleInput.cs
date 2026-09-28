using System.Collections.Generic;
using UnityEngine;

/// <summary>Map3 Train Yard normal-movement input for the crank's pass-through trigger.</summary>
[RequireComponent(typeof(Collider))]
public sealed class ToyWorldTrainAxleInput : MonoBehaviour
{
    public WindupAxle axle;
    public float deltaPerHit = 1f;

    private readonly Dictionary<Rigidbody, float> lastHitTime = new Dictionary<Rigidbody, float>();

    private void Reset()
    {
        Collider trigger = GetComponent<Collider>();
        if (trigger != null) trigger.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (axle == null || axle.crank == null) return;

        PlayerShapeIdentity identity = other.GetComponentInParent<PlayerShapeIdentity>();
        if (identity == null) return;
        bool isSolid = identity.solidCollider != null ? identity.solidCollider == other : !other.isTrigger;
        if (!isSolid) return;
        Rigidbody body = other.attachedRigidbody;
        if (body == null) return;
        if (lastHitTime.TryGetValue(body, out float lastHit) && Time.time - lastHit < 0.25f) return;

        Vector3 push = body.velocity;
        PlayerRollModeReceiver rollMode = identity.GetComponent<PlayerRollModeReceiver>();
        if (rollMode != null && rollMode.RollModeActive && rollMode.CurrentTumbleDirection.sqrMagnitude > 0.0001f)
            push = rollMode.CurrentTumbleDirection;
        push.y = 0f;
        if (push.sqrMagnitude < 0.04f) return;

        Vector3 radial = other.bounds.center - axle.crank.position;
        radial.y = 0f;
        float cross = Vector3.Cross(radial, push.normalized).y;
        float turnSign = Mathf.Abs(cross) > 0.01f ? Mathf.Sign(cross) : 1f;
        lastHitTime[body] = Time.time;
        axle.ApplyRotation(turnSign * Mathf.Abs(deltaPerHit));
    }
}
