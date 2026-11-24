using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One continuously supplied dish on the CH3 hot wash conveyor. Every peer derives
/// its pose from the multiplayer server clock; only the owner moves a riding shape.
/// The return run is below the belt and has no support collider.
/// </summary>
[DefaultExecutionOrder(-75)]
[RequireComponent(typeof(Rigidbody))]
public sealed class KitchenHotPlateLine : MonoBehaviour
{
    [SerializeField] private int segmentIndex;
    [SerializeField] private float startX;
    [SerializeField] private float endX;
    [SerializeField] private float z = 100f;
    [SerializeField] private float topY = 8.55f;
    [SerializeField] private float speed = 3.5f;
    [SerializeField] private float loopSeconds = 11.5f;
    [SerializeField] private float returnDrop = 1.4f;
    [SerializeField] private float transitionSeconds = .38f;
    [SerializeField] private float phaseFraction;
    [SerializeField] private BoxCollider support;
    [SerializeField] private Renderer warningRenderer;

    private readonly Collider[] nearby = new Collider[32];
    private readonly HashSet<Rigidbody> riders = new HashSet<Rigidbody>();
    private readonly HashSet<Rigidbody> standing = new HashSet<Rigidbody>();
    private readonly Dictionary<Rigidbody, Collider[]> bodyColliders = new Dictionary<Rigidbody, Collider[]>();
    private Rigidbody body;
    private MaterialPropertyBlock warningBlock;
    private PhysicsMaterial slideMaterial;
    private bool initialized;
    private float lastCycleTime;
    private Vector3 lastPosition;

    public int SegmentIndex => segmentIndex;
    public Vector3 CurrentPosition => body != null ? body.position : transform.position;
    public bool IsSupportActive => support != null && support.enabled;
    public int RiderCount => riders.Count;
    public int BoardCount { get; private set; }
    public int DisembarkCount { get; private set; }
    public int RecycleCount { get; private set; }
    public float SecondsUntilRecycle { get; private set; }
    public float TopY => topY;
    public float Speed => speed;

    public void Configure(int segment, float fromX, float toX, float laneZ, float surfaceY,
        float forwardSpeed, float loopDuration, float phase, BoxCollider top, Renderer warning)
    {
        segmentIndex = segment;
        startX = fromX;
        endX = toX;
        z = laneZ;
        topY = surfaceY;
        speed = forwardSpeed;
        loopSeconds = loopDuration;
        phaseFraction = Mathf.Repeat(phase, 1f);
        support = top;
        warningRenderer = warning;
        transform.position = EvaluatePose(0f, out bool active, out _, out _);
        support.enabled = active;
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.None;
        if (support == null) support = GetComponent<BoxCollider>();
        slideMaterial = new PhysicsMaterial("CH3 moving dish low-friction support")
        {
            dynamicFriction = 0f,
            staticFriction = 0f,
            frictionCombine = PhysicsMaterialCombine.Minimum
        };
        support.material = slideMaterial;
        warningBlock = new MaterialPropertyBlock();
    }

    private void OnDestroy()
    {
        if (slideMaterial != null) Destroy(slideMaterial);
    }

    private void FixedUpdate()
    {
        Vector3 next = EvaluatePose(MpSync.SharedFixedTime, out bool forward,
            out float cycleTime, out float cycleSeconds);

        if (initialized && cycleTime < lastCycleTime - cycleSeconds * .5f) RecycleCount++;
        Vector3 delta = initialized ? next - lastPosition : Vector3.zero;
        // Server clock correction or a respawn must never drag a rider backwards.
        bool continuousForward = initialized && forward && support.enabled &&
            cycleTime >= lastCycleTime && delta.x >= 0f && delta.x <= speed * Time.fixedDeltaTime * 2.5f;
        if (continuousForward) RefreshRiders();
        else ClearRiders();

        support.enabled = forward;
        body.position = next;
        transform.position = next;
        if (continuousForward)
            foreach (Rigidbody rider in riders)
                if (rider != null && !rider.isKinematic && MpSync.IsOwnBody(rider))
                    rider.position += delta;

        if (warningRenderer != null)
        {
            warningBlock.SetColor("_BaseColor", forward && SecondsUntilRecycle < 1.2f
                ? new Color(1f, .18f, .03f) : new Color(1f, .78f, .08f));
            warningRenderer.SetPropertyBlock(warningBlock);
        }
        lastPosition = next;
        lastCycleTime = cycleTime;
        initialized = true;
    }

    private Vector3 EvaluatePose(float sharedTime, out bool forward,
        out float cycleTime, out float cycleSeconds)
    {
        float forwardSeconds = (endX - startX) / speed;
        cycleSeconds = loopSeconds;
        float backSeconds = cycleSeconds - forwardSeconds - transitionSeconds * 2f;
        if (backSeconds <= 0f) backSeconds = .1f;
        float hiddenReturnSpeed = (endX - startX) / backSeconds;
        cycleTime = Mathf.Repeat(sharedTime + phaseFraction * cycleSeconds, cycleSeconds);
        forward = cycleTime < forwardSeconds;
        if (forward)
        {
            SecondsUntilRecycle = forwardSeconds - cycleTime;
            return new Vector3(startX + cycleTime * speed, topY - .17f, z);
        }
        SecondsUntilRecycle = 0f;
        if (cycleTime < forwardSeconds + transitionSeconds)
        {
            float t = (cycleTime - forwardSeconds) / transitionSeconds;
            return new Vector3(endX, topY - .17f - t * returnDrop, z);
        }
        if (cycleTime < forwardSeconds + transitionSeconds + backSeconds)
        {
            float t = cycleTime - forwardSeconds - transitionSeconds;
            return new Vector3(endX - t * hiddenReturnSpeed, topY - .17f - returnDrop, z);
        }
        float rise = (cycleTime - forwardSeconds - transitionSeconds - backSeconds) / transitionSeconds;
        return new Vector3(startX, topY - .17f - (1f - rise) * returnDrop, z);
    }

    private void RefreshRiders()
    {
        int count = Physics.OverlapBoxNonAlloc(body.position + Vector3.up * .75f,
            new Vector3(1.65f, 1.2f, 2.1f), nearby, Quaternion.identity,
            ~0, QueryTriggerInteraction.Ignore);
        standing.Clear();
        for (int i = 0; i < count; i++)
        {
            Rigidbody candidate = nearby[i] == null ? null : nearby[i].attachedRigidbody;
            if (candidate == null || candidate == body || !MpSync.IsOwnBody(candidate) ||
                candidate.GetComponent<PlayerMover>() == null || standing.Contains(candidate)) continue;
            if (StandingOnSupport(candidate)) standing.Add(candidate);
        }
        foreach (Rigidbody rider in riders)
            if (!standing.Contains(rider)) DisembarkCount++;
        foreach (Rigidbody rider in standing)
            if (!riders.Contains(rider)) BoardCount++;
        riders.Clear();
        foreach (Rigidbody rider in standing) riders.Add(rider);
    }

    private bool StandingOnSupport(Rigidbody candidate)
    {
        if (!bodyColliders.TryGetValue(candidate, out Collider[] colliders))
        {
            colliders = candidate.GetComponentsInChildren<Collider>();
            bodyColliders.Add(candidate, colliders);
        }
        float foot = float.PositiveInfinity;
        foreach (Collider collider in colliders)
            if (collider != null && collider.enabled && !collider.isTrigger)
                foot = Mathf.Min(foot, collider.bounds.min.y);
        if (foot < topY - .18f || foot > topY + .18f ||
            candidate.linearVelocity.y > .3f) return false;
        Vector3 origin = candidate.worldCenterOfMass;
        return Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 2.5f,
            ~0, QueryTriggerInteraction.Ignore) && hit.collider == support;
    }

    private void ClearRiders()
    {
        DisembarkCount += riders.Count;
        riders.Clear();
    }

    /// <summary>Used by the hot rail hazard to exempt a shape actually supported by a dish.</summary>
    public static bool IsPlayerOnPlate(Rigidbody player)
    {
        if (player == null) return false;
        Vector3 origin = player.worldCenterOfMass;
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 2.5f,
            ~0, QueryTriggerInteraction.Ignore)) return false;
        KitchenHotPlateLine plate = hit.collider.GetComponentInParent<KitchenHotPlateLine>();
        return plate != null && plate.IsSupportActive && hit.collider == plate.support &&
            Mathf.Abs(player.position.y - plate.TopY) < 2.2f;
    }
}
