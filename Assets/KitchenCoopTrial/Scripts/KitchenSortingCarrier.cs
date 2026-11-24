using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One cafeteria plate carried along the outfeed belt.
/// The support follows the occupied plate, lowers at the far end, and returns below the belt.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public sealed class KitchenSortingCarrier : MonoBehaviour, IMpSynced
{
    public enum TravelState
    {
        Boarding,
        Carrying,
        Unloading,
        Returning,
        Raising
    }

    [SerializeField] private Vector3 start = new Vector3(78f, 8.2f, 100f);
    [SerializeField] private Vector3 end = new Vector3(94f, 8.2f, 100f);
    [SerializeField] private float travelSpeed = 2f;
    [SerializeField] private float returnSpeed = 3f;
    [SerializeField] private float lowerDistance = .8f;
    [SerializeField] private float lowerSpeed = 1.6f;
    [SerializeField] private float unloadPause = .22f;
    [SerializeField] private BoxCollider support;

    private readonly HashSet<Rigidbody> riders = new HashSet<Rigidbody>();
    private readonly HashSet<Rigidbody> standing = new HashSet<Rigidbody>();
    private readonly Dictionary<Rigidbody, Collider[]> playerColliders =
        new Dictionary<Rigidbody, Collider[]>();
    private readonly Collider[] nearby = new Collider[24];
    private Rigidbody body;
    private float pauseRemaining;
    private PhysicsMaterial slideMaterial;
    private uint mpId;
    private TravelState publishedState = (TravelState)(-1);
    private bool publishedSupport;

    public TravelState State { get; private set; } = TravelState.Boarding;
    public Vector3 CarrierPosition => body != null ? body.position : transform.position;
    public int RiderCount => riders.Count;
    public int BoardCount { get; private set; }
    public int DisembarkCount { get; private set; }
    public int AutoUnloadCount { get; private set; }

    public void Configure(Vector3 from, Vector3 to, BoxCollider top)
    {
        start = from;
        end = to;
        support = top;
        transform.position = from;
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        if (support == null) support = GetComponent<BoxCollider>();
        slideMaterial = new PhysicsMaterial("Sorting carrier low-friction support")
        {
            dynamicFriction = 0f,
            staticFriction = 0f,
            frictionCombine = PhysicsMaterialCombine.Minimum
        };
        support.material = slideMaterial;
        mpId = MpSync.Register(this);
        MpBodySync.Ensure(this);
    }

    private void OnDestroy()
    {
        MpSync.Unregister(mpId, this);
        if (slideMaterial != null) Destroy(slideMaterial);
    }

    public void OnMpValue(int channel, float value)
    {
        if (channel == 0) State = (TravelState)Mathf.RoundToInt(value);
        if (channel == 1 && support != null) support.enabled = value > 0.5f;
    }

    private void FixedUpdate()
    {
        // Scene objects may awake before the multiplayer round starts.
        if (MpSync.Active) MpBodySync.Ensure(this);
        if (MpSync.IsClient) return;
        RefreshRiders();

        switch (State)
        {
            case TravelState.Boarding:
                if (riders.Count > 0)
                    State = TravelState.Carrying;
                break;

            case TravelState.Carrying:
                Vector3 previous = body.position;
                Vector3 next = Vector3.MoveTowards(previous, end, travelSpeed * Time.fixedDeltaTime);
                // A kinematic MovePosition reports a velocity to PlayerMover and can
                // also impart contact friction. Set the pose directly and translate
                // only riders confirmed as standing on the support this step.
                body.position = next;
                Vector3 carriage = next - previous;
                foreach (Rigidbody rider in riders)
                    if (rider != null && !rider.isKinematic && MpSync.IsOwnBody(rider))
                        rider.position += carriage;
                if (Vector3.Distance(body.position, end) <= travelSpeed * Time.fixedDeltaTime + .001f)
                {
                    pauseRemaining = unloadPause;
                    State = TravelState.Unloading;
                }
                break;

            case TravelState.Unloading:
                pauseRemaining -= Time.fixedDeltaTime;
                if (pauseRemaining > 0f) break;

                // The continuous belt is directly below the plate. The player lands on
                // that belt while the empty plate retracts through it.
                if (riders.Count > 0) AutoUnloadCount += riders.Count;
                DisembarkCount += riders.Count;
                riders.Clear();
                support.enabled = false;
                MoveToward(end + Vector3.down * lowerDistance, lowerSpeed);
                if (body.position.y <= end.y - lowerDistance + .001f)
                    State = TravelState.Returning;
                break;

            case TravelState.Returning:
                MoveToward(start + Vector3.down * lowerDistance, returnSpeed);
                if (Vector3.Distance(body.position, start + Vector3.down * lowerDistance)
                    <= returnSpeed * Time.fixedDeltaTime + .001f)
                    State = TravelState.Raising;
                break;

            case TravelState.Raising:
                if (BoardingAreaOccupied()) break;
                MoveToward(start, lowerSpeed);
                if (body.position.y >= start.y - .001f)
                {
                    support.enabled = true;
                    State = TravelState.Boarding;
                }
                break;
        }

        if (publishedState != State)
        {
            publishedState = State;
            MpSync.Publish(mpId, 0, (float)State);
        }
        if (publishedSupport != support.enabled)
        {
            publishedSupport = support.enabled;
            MpSync.Publish(mpId, 1, support.enabled ? 1f : 0f);
        }
    }

    private void MoveToward(Vector3 destination, float speed)
    {
        body.MovePosition(Vector3.MoveTowards(body.position, destination, speed * Time.fixedDeltaTime));
    }

    private void RefreshRiders()
    {
        if (!support.enabled || State == TravelState.Returning || State == TravelState.Raising)
        {
            if (riders.Count > 0) DisembarkCount += riders.Count;
            riders.Clear();
            return;
        }

        // A vertical ray confirms the support below the player's centre. A nearby
        // jumper, side contact or player on the belt below cannot become a rider.
        int count = Physics.OverlapBoxNonAlloc(
            body.position + Vector3.up * .7f,
            new Vector3(1.9f, 1.1f, 1.7f), nearby, Quaternion.identity,
            ~0, QueryTriggerInteraction.Ignore);
        standing.Clear();
        for (int i = 0; i < count; i++)
        {
            Rigidbody candidate = nearby[i].attachedRigidbody;
            if (candidate == null || candidate == body || candidate.GetComponent<PlayerMover>() == null)
                continue;
            if (standing.Contains(candidate) || !IsStandingOnSupport(candidate)) continue;
            standing.Add(candidate);
        }
        foreach (Rigidbody rider in riders)
            if (!standing.Contains(rider)) DisembarkCount++;
        foreach (Rigidbody rider in standing)
            if (!riders.Contains(rider)) BoardCount++;
        riders.Clear();
        foreach (Rigidbody rider in standing) riders.Add(rider);
    }

    private bool IsStandingOnSupport(Rigidbody candidate)
    {
        Vector3 origin = candidate.worldCenterOfMass;
        float top = body.position.y + support.center.y + support.size.y * .5f;
        PlayerGroundContact contact = candidate.GetComponentInChildren<PlayerGroundContact>();
        if (contact == null || (MpSync.IsOwnBody(candidate) && !contact.IsGrounded) ||
            candidate.linearVelocity.y > .05f)
            return false;
        if (!playerColliders.TryGetValue(candidate, out Collider[] colliders))
        {
            colliders = candidate.GetComponentsInChildren<Collider>();
            playerColliders.Add(candidate, colliders);
        }
        float lowestPoint = float.PositiveInfinity;
        foreach (Collider collider in colliders)
            if (!collider.isTrigger) lowestPoint = Mathf.Min(lowestPoint, collider.bounds.min.y);
        if (lowestPoint > top + .1f || origin.y < top + .12f || origin.y > top + 1.85f)
            return false;
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 2.05f,
                ~0, QueryTriggerInteraction.Ignore))
            return false;
        return hit.collider == support;
    }

    private bool BoardingAreaOccupied()
    {
        int count = Physics.OverlapBoxNonAlloc(
            start + Vector3.up * .55f,
            new Vector3(1.95f, .75f, 1.75f), nearby,
            Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Rigidbody candidate = nearby[i].attachedRigidbody;
            if (candidate != null && candidate.GetComponent<PlayerMover>() != null)
                return true;
        }
        return false;
    }
}
