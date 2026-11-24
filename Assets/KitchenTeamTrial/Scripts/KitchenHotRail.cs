using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// CH3의 고온수 세척 레일. 레일 표면 가까이에 실제로 내려온 발만 판정한다.
/// 움직이는 접시 위와 실드 무게판 위는 안전하다.
/// </summary>
public sealed class KitchenHotRail : MonoBehaviour
{
    public const float RailStartX = -112f;
    public const float RailEndX = -51f;
    public const float RailCenterZ = 100f;
    public const float RailHalfWidthZ = 3.7f;
    public const float ContactFootY = 8.4f;

    [SerializeField] private SectionSafePoint recoveryPoint;
    [SerializeField] private SectionSafePoint[] multiplayerRecoveryPoints;
    [SerializeField] private BoxCollider hotVolume;
    [SerializeField] private Collider[] railSurfaces;

    private RespawnController respawn;
    private readonly Dictionary<PlayerMover, float> nextHitTime = new Dictionary<PlayerMover, float>();
    private readonly Dictionary<PlayerMover, Collider[]> playerColliders = new Dictionary<PlayerMover, Collider[]>();
    private PlayerMover[] soloBodies;
    private float nextRefresh;
    private readonly RaycastHit[] groundHits = new RaycastHit[16];

    public int HitCount { get; private set; }
    public SectionSafePoint RecoveryPoint => recoveryPoint;

    public void Configure(BoxCollider volume, SectionSafePoint point, SectionSafePoint[] mpPoints,
        Collider[] surfaces)
    {
        hotVolume = volume;
        recoveryPoint = point;
        multiplayerRecoveryPoints = mpPoints;
        railSurfaces = surfaces;
    }

    private void Awake()
    {
        respawn = FindFirstObjectByType<RespawnController>();
        if (hotVolume == null) hotVolume = GetComponent<BoxCollider>();
        if (railSurfaces == null || railSurfaces.Length == 0)
        {
            GameObject belt = GameObject.Find("Map2 V3 Belt Segment 19 - CH3 top white-line plate route");
            if (belt != null)
            {
                Transform deck = belt.transform.Find("Belt continuous collision surface");
                Transform bed = belt.transform.Find("Restaurant brown conveyor walkable bed");
                if (deck != null && bed != null)
                    railSurfaces = new[] { deck.GetComponent<Collider>(), bed.GetComponent<Collider>() };
            }
        }
    }

    private void FixedUpdate()
    {
        if (respawn == null) respawn = FindFirstObjectByType<RespawnController>();
        if (respawn == null || recoveryPoint == null) return;

        // Trigger callbacks can stop for a sleeping Rigidbody. Three bodies are cheap to scan.
        if (MpSync.Active)
        {
            foreach (PlayerMover mover in MpSync.Bodies) CheckPlayer(mover);
        }
        else
        {
            if (soloBodies == null || Time.time >= nextRefresh)
            {
                soloBodies = FindObjectsByType<PlayerMover>(FindObjectsSortMode.None);
                nextRefresh = Time.time + 1f;
            }
            foreach (PlayerMover mover in soloBodies) CheckPlayer(mover);
        }
    }

    private void OnTriggerEnter(Collider other) => CheckPlayer(other.GetComponentInParent<PlayerMover>());
    private void OnTriggerStay(Collider other) => CheckPlayer(other.GetComponentInParent<PlayerMover>());

    public bool IsHotSurfaceContact(PlayerMover mover)
    {
        if (mover == null || !mover.gameObject.activeInHierarchy) return false;
        if (MpSync.Active && !MpSync.IsOwnBody(mover)) return false;
        Rigidbody body = mover.GetComponent<Rigidbody>();
        if (body == null) return false;
        // Do the cheap center test before the plate's support query and collider bounds walk.
        if (body.position.x < RailStartX - 2f || body.position.x > RailEndX + 2f ||
            body.position.z < RailCenterZ - RailHalfWidthZ - 2f ||
            body.position.z > RailCenterZ + RailHalfWidthZ + 2f) return false;
        if (KitchenHotPlateLine.IsPlayerOnPlate(body)) return false;

        if (!playerColliders.TryGetValue(mover, out Collider[] colliders))
        {
            colliders = mover.GetComponentsInChildren<Collider>();
            playerColliders[mover] = colliders;
        }
        bool overlapsRail = false;
        float lowestFoot = float.PositiveInfinity;
        foreach (Collider collider in colliders)
        {
            if (collider == null || !collider.enabled || collider.isTrigger) continue;
            Bounds b = collider.bounds;
            if (b.max.x < RailStartX || b.min.x > RailEndX ||
                b.max.z < RailCenterZ - RailHalfWidthZ ||
                b.min.z > RailCenterZ + RailHalfWidthZ) continue;
            overlapsRail = true;
            lowestFoot = Mathf.Min(lowestFoot, b.min.y);
        }
        if (!overlapsRail || lowestFoot > ContactFootY || lowestFoot < 7.4f) return false;

        // A foot passing above the rail in midair is not a contact. Require the
        // collider immediately under the foot to be the actual CH3 belt support.
        int count = Physics.RaycastNonAlloc(
            new Vector3(body.position.x, lowestFoot + 0.12f, body.position.z),
            Vector3.down, groundHits, 0.32f, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
            if (railSurfaces != null &&
                groundHits[i].point.x >= RailStartX && groundHits[i].point.x <= RailEndX &&
                groundHits[i].point.z >= RailCenterZ - RailHalfWidthZ &&
                groundHits[i].point.z <= RailCenterZ + RailHalfWidthZ)
                foreach (Collider surface in railSurfaces)
                    if (surface != null && groundHits[i].collider == surface) return true;
        return false;
    }

    private void CheckPlayer(PlayerMover mover)
    {
        if (respawn == null || recoveryPoint == null || !IsHotSurfaceContact(mover)) return;
        if (nextHitTime.TryGetValue(mover, out float until) && Time.time < until) return;

        nextHitTime[mover] = Time.time + 0.7f;
        HitCount++;
        SectionSafePoint destination = recoveryPoint;
        if (MpSync.Active && multiplayerRecoveryPoints != null)
        {
            int slot = MpSync.SlotOf(mover);
            if (slot >= 0 && slot < multiplayerRecoveryPoints.Length && multiplayerRecoveryPoints[slot] != null)
                destination = multiplayerRecoveryPoints[slot];
        }
        respawn.RespawnPlayer(mover.gameObject, destination);
    }
}
