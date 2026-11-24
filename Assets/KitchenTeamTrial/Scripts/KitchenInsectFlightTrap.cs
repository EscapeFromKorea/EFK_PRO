using System.Collections.Generic;
using UnityEngine;

/// <summary>A single reusable fly. Every peer sees the same approach and checks its own bodies.</summary>
public sealed class KitchenInsectFlightTrap : MonoBehaviour
{
    public const float WarningSeconds = 1.1f;
    public const float FlightSeconds = 1.5f;
    public const float CycleSeconds = 4f;
    public const float HitRadius = .34f;

    [SerializeField] private Transform attractor;
    [SerializeField] private Transform[] origins;
    [SerializeField] private Transform insect;
    [SerializeField] private Bounds dangerBounds;
    [SerializeField] private SectionSafePoint recovery;
    [SerializeField] private RespawnController respawn;

    private readonly Collider[] contacts = new Collider[64];
    private readonly Dictionary<PlayerMover, float> protectedUntil = new Dictionary<PlayerMover, float>();
    private LineRenderer guide;
    private Material guideMaterial;
    private Transform leftWing;
    private Transform rightWing;
    private Quaternion leftRest;
    private Quaternion rightRest;
    private uint soloSeed;
    private int lastCycle = -1;
    private float lastClock;
    public int CurrentOriginIndex { get; private set; }
    public Vector3 OriginPosition => origins != null && CurrentOriginIndex < origins.Length &&
        origins[CurrentOriginIndex] ? origins[CurrentOriginIndex].position : transform.position;
    public Vector3 TargetPosition => attractor ? attractor.position : transform.position;
    public Vector3 CurrentPosition => insect ? insect.position : transform.position;
    public Bounds DangerBounds => dangerBounds;
    public bool IsWarning { get; private set; }
    public bool IsFlying { get; private set; }
    public int HitCount { get; private set; }

    public void Configure(Transform attractor, Transform[] origins, Transform insect,
        Bounds dangerBounds, SectionSafePoint recovery = null)
    {
        this.attractor = attractor;
        this.origins = origins;
        this.insect = insect;
        this.dangerBounds = dangerBounds;
        this.recovery = recovery;
        respawn = FindFirstObjectByType<RespawnController>();
    }

    private void Start()
    {
        if (!attractor || !insect || origins == null || origins.Length != 4 ||
            System.Array.Exists(origins, origin => !origin))
        {
            Debug.LogError("CH6 insect trap requires one intake, four origins and one fly.", this);
            enabled = false;
            return;
        }
        leftWing = insect.Find("Left wing hinge");
        rightWing = insect.Find("Right wing hinge");
        if (leftWing) leftRest = leftWing.localRotation;
        if (rightWing) rightRest = rightWing.localRotation;
        soloSeed = unchecked((uint)System.Guid.NewGuid().GetHashCode());
        if (!respawn) respawn = FindFirstObjectByType<RespawnController>();
        guide = new GameObject("CH6 fly approach warning").AddComponent<LineRenderer>();
        guide.transform.SetParent(transform, false);
        guide.useWorldSpace = true;
        guide.positionCount = 2;
        guide.enabled = false;
        Shader shader = Shader.Find("Sprites/Default");
        if (shader)
        {
            guideMaterial = new Material(shader);
            guide.sharedMaterial = guideMaterial;
        }
        guide.startColor = new Color(1f, .75f, .09f, .9f);
        guide.endColor = new Color(1f, .35f, .05f, .45f);
    }

    // Shuffle each pair of straight and diagonal entrances independently every four flights.
    private static int SelectOrigin(int cycle, uint sessionSeed)
    {
        uint seed = unchecked((uint)(cycle / 4) + 0x9e3779b9u + sessionSeed);
        seed ^= seed >> 16;
        seed = unchecked(seed * 0x85ebca6bu);
        seed ^= seed >> 13;
        int kind = (cycle & 1) ^ (int)(seed & 1u);
        int variant = ((cycle >> 1) & 1) ^ (int)((seed >> (kind + 1)) & 1u);
        return variant * 2 + kind;
    }

    private void FixedUpdate()
    {
        if (!guide) return;
        float clock = MpSync.SharedFixedTime;
        int cycle = Mathf.FloorToInt(clock / CycleSeconds);
        float phase = clock - cycle * CycleSeconds;
        bool continuous = lastCycle == cycle && clock >= lastClock &&
            clock - lastClock <= Time.fixedDeltaTime * 3f;
        bool wasFlying = IsFlying;
        Vector3 previous = insect.position;
        CurrentOriginIndex = SelectOrigin(cycle, MpSync.Active ? 0u : soloSeed);
        IsWarning = phase < WarningSeconds;
        IsFlying = !IsWarning && phase < WarningSeconds + FlightSeconds;
        Vector3 start = OriginPosition;
        Vector3 end = TargetPosition;
        guide.enabled = IsWarning;
        if (IsWarning)
        {
            guide.SetPosition(0, start);
            guide.SetPosition(1, end);
            guide.widthMultiplier = .05f + .055f * Mathf.PingPong(phase * 4f, 1f);
        }
        insect.gameObject.SetActive(IsFlying || (IsWarning && Mathf.Repeat(phase, .28f) < .19f));
        if (insect.gameObject.activeSelf)
        {
            float flap = 37f * Mathf.Sin(phase * 2f * Mathf.PI * 9f);
            if (leftWing) leftWing.localRotation = leftRest * Quaternion.AngleAxis(-flap, Vector3.forward);
            if (rightWing) rightWing.localRotation = rightRest * Quaternion.AngleAxis(flap, Vector3.forward);
        }
        insect.position = Vector3.Lerp(start, end, (phase - WarningSeconds) / FlightSeconds);
        if ((end - start).sqrMagnitude > .001f) insect.rotation = Quaternion.LookRotation(end - start);

        if (IsFlying || (continuous && wasFlying))
        {
            Vector3 from = continuous && wasFlying ? previous : insect.position;
            CheckContact(from, insect.position);
        }
        lastCycle = cycle;
        lastClock = clock;
    }

    private void CheckContact(Vector3 from, Vector3 to)
    {
        if (!respawn) return;
        int count = Physics.OverlapCapsuleNonAlloc(from, to, HitRadius, contacts, ~0,
            QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider contact = contacts[i];
            Rigidbody body = contact.attachedRigidbody;
            if (!body || body.gameObject.scene != gameObject.scene ||
                !dangerBounds.Intersects(contact.bounds) || !MpSync.IsOwnBody(body)) continue;
            PlayerMover mover = body.GetComponent<PlayerMover>();
            if (!mover || (protectedUntil.TryGetValue(mover, out float until) && Time.time < until)) continue;
            protectedUntil[mover] = Time.time + CycleSeconds + .5f;
            HitCount++;
            respawn.RespawnPlayer(mover.gameObject, recovery);
        }
    }

    private void OnDisable()
    {
        if (guide) guide.enabled = false;
        if (insect) insect.gameObject.SetActive(false);
        IsFlying = IsWarning = false;
        lastCycle = -1;
    }

    private void OnDestroy()
    {
        if (guideMaterial) Destroy(guideMaterial);
        if (guide) Destroy(guide.gameObject);
    }
}
