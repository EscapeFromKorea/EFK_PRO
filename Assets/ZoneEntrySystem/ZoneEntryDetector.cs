using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 구역 진입 감지기 — 보이지 않는 Box 트리거에 붙어, 도형이 구역에 "새로 들어온" 사실을 다른 기믹에
/// 이벤트로 알린다(도끼·망치·가시·발사구·레이저의 Activate() 등). 무엇을 켤지는 이 컴포넌트가 모른다.
/// 상세: docs/PRD/ZoneEntry.md.
///
/// [진입의 정의] 도형(PlayerMover) 하나의 <b>첫</b> 콜라이더가 밖에서 안으로 바뀌는 순간만 진입이다.
/// 플레이어는 콜라이더가 둘(Player_Mesh/Player_Collider)이라 콜백이 두 번 오지만 도형별 집합으로 합쳐
/// 한 번만 센다. 모든 콜라이더가 나가야 점유가 끝난다. PlayerMover가 없는 물체(상자·낙석·함정 몸체,
/// 태그만 Player인 비도형)는 대상이 아니다.
///
/// [점유 기록이 틀어지지 않게 하는 세 장치]
/// 1. 시작/재활성화/ResetState 때 <c>OverlapBox</c>로 이미 안에 있는 도형을 점유로 먼저 채운다 — 씬
///    시작 때 이미 겹쳐 있는 콜라이더에도 물리가 Enter를 한 번 보내는데, 점유가 이미 있으면 그건 진입이
///    아니라 점유 추가로만 처리된다(PRD §4-5 "시작 위치가 이미 구역 안이면 새 진입으로 세지 않는다").
/// 2. 매 FixedUpdate에 파괴·비활성·구역 밖으로 나간 콜라이더를 걷어낸다 — 순간이동이나 콜라이더 비활성은
///    Exit 콜백이 오지 않을 수 있어, 낡은 점유가 재진입을 막으면 안 된다. 구역 AABB(회전된 박스를
///    감싸는 상자)가 콜라이더와 안 겹치면 실제로도 밖이므로 걷어내는 쪽은 틀리지 않는다.
/// 3. 복귀 중인 도형(RespawnController.PlayerRespawned ~ ExternallyDriven 해제)이 구역에 들어오면 점유만
///    기록하고 발동하지 않는다 — 복귀 목적지가 구역 안이어도 복귀 자체는 진입이 아니다(PRD §5). 그
///    배치는 오류라 경고를 남긴다.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class ZoneEntryDetector : MonoBehaviour
{
    public enum EntryMode
    {
        [Tooltip("구역 전체에서 최초 한 번만 발동한다.")] OncePerZone,
        [Tooltip("모든 콜라이더가 나갔다 다시 들어오는 유효한 새 진입마다 발동한다.")] EveryNewEntry
    }

    [System.Serializable]
    public class PlayerEntryEvent : UnityEvent<GameObject> { }

    [Header("판정")]
    [Tooltip("최초 한 번(기본) / 새 진입마다.")]
    public EntryMode mode = EntryMode.OncePerZone;

    [Tooltip("재발동 대기 시간(초). 이벤트를 실제로 낸 순간부터 이 감지기 전체에 적용한다. 대기 중 들어온 " +
             "후보는 버리고, 대기가 끝나도 안에 계속 있는 도형은 자동으로 다시 발동시키지 않는다. 0이면 " +
             "대기 없음. 실제 배치값은 맵 담당이 조정한다(PRD §7 미정).")]
    [Min(0f)] public float retriggerCooldownSeconds = 0f;

    [Header("진단")]
    [Tooltip("켜면 도형이 구역에 닿을 때마다 진입 판정 결과(발동/무시 사유)를 콘솔에 남긴다. 연결했는데 " +
             "안 켜질 때 감지기 문제인지 연결 쪽 문제인지 가르는 용도다 — 평소에는 끈다.")]
    public bool debugLog = false;

    [Header("출력 — 같은 유효 진입에서 각각 한 번씩 발생")]
    [Tooltip("인자 없음. 함정/발사구/레이저의 Activate() 같은 함수에 연결한다.")]
    public UnityEvent OnValidEntry;

    [Tooltip("진입한 도형의 대표 Root(PlayerMover가 붙은 GameObject)를 넘긴다.")]
    public PlayerEntryEvent OnValidEntryPlayer;

    private readonly Dictionary<PlayerMover, HashSet<Collider>> occupancy =
        new Dictionary<PlayerMover, HashSet<Collider>>();
    private readonly HashSet<PlayerMover> respawning = new HashSet<PlayerMover>();
    private readonly List<Collider> scratchColliders = new List<Collider>();
    private readonly List<PlayerMover> scratchMovers = new List<PlayerMover>();

    private BoxCollider zone;
    private RespawnController subscribedRespawn;
    private bool hasFired;
    private float nextAllowedTime;

    private void Reset()
    {
        BoxCollider box = GetComponent<BoxCollider>();
        if (box != null) box.isTrigger = true;
    }

    private void Awake()
    {
        zone = GetComponent<BoxCollider>();
        if (!zone.isTrigger)
            Debug.LogError("[ZoneEntryDetector] BoxCollider가 Trigger가 아니다 — isTrigger를 켜라.", this);

        // 구역은 눈에 보이면 안 된다(PRD §3). 렌더러가 붙어 있으면 끈다.
        foreach (Renderer r in GetComponents<Renderer>()) r.enabled = false;
    }

    private void OnEnable()
    {
        SubscribeRespawn();
        SeedOccupancy();
    }

    private void Start()
    {
        // 모든 오브젝트의 Awake가 끝난 뒤 한 번 더 — OnEnable 시점에는 다른 오브젝트의 콜라이더가 아직
        // 없을 수 있다. 컨트롤러가 늦게 생긴 경우의 구독도 여기서 다시 시도한다.
        SubscribeRespawn();
        SeedOccupancy();
    }

    private void OnDisable()
    {
        if (subscribedRespawn != null) subscribedRespawn.PlayerRespawned -= OnPlayerRespawned;
        subscribedRespawn = null;
        occupancy.Clear();
        respawning.Clear();
    }

    /// <summary>챕터 전체 재시작용(확정 — 호출은 챕터 컨트롤러나 씬 인스펙터 배선이 맡는다). 최초 발동 여부,
    /// 도형별 점유, 대기 시간을 모두 초기화한다. 그 시점에 이미 안에 있는 도형은 점유로 채워지므로 나갔다
    /// 다시 들어와야 발동한다.</summary>
    public void ResetState()
    {
        hasFired = false;
        nextAllowedTime = 0f;
        respawning.Clear();
        SeedOccupancy();
    }

    private void SubscribeRespawn()
    {
        if (subscribedRespawn != null) return;
        subscribedRespawn = FindObjectOfType<RespawnController>();
        if (subscribedRespawn != null) subscribedRespawn.PlayerRespawned += OnPlayerRespawned;
    }

    private void OnPlayerRespawned(GameObject playerRoot)
    {
        if (playerRoot == null) return;
        PlayerMover mover = playerRoot.GetComponentInParent<PlayerMover>();
        if (mover != null) respawning.Add(mover);
    }

    private void SeedOccupancy()
    {
        occupancy.Clear();
        if (zone == null) zone = GetComponent<BoxCollider>();
        if (zone == null) return;

        Physics.SyncTransforms();
        Transform t = zone.transform;
        Vector3 center = t.TransformPoint(zone.center);
        Vector3 scale = t.lossyScale;
        Vector3 half = Vector3.Scale(zone.size * 0.5f,
            new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));

        // 플레이어의 Player_Mesh는 트리거라 트리거도 포함해서 본다.
        Collider[] hits = Physics.OverlapBox(center, half, t.rotation, ~0, QueryTriggerInteraction.Collide);
        foreach (Collider c in hits)
        {
            PlayerMover mover = c.GetComponentInParent<PlayerMover>();
            if (mover == null) continue;
            if (!occupancy.TryGetValue(mover, out HashSet<Collider> set))
            {
                set = new HashSet<Collider>();
                occupancy[mover] = set;
            }
            set.Add(c);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        PlayerMover mover = other.GetComponentInParent<PlayerMover>();
        if (mover == null) return;

        if (occupancy.TryGetValue(mover, out HashSet<Collider> set))
        {
            set.Add(other); // 같은 도형의 두 번째 콜라이더 — 진입이 아니다.
            if (debugLog) Debug.Log($"[ZoneEntryDetector] '{name}': '{mover.name}' 콜라이더 추가 — 이미 점유 중이라 진입 아님 " +
                                    $"(콜라이더 {set.Count}개, 트리거={other.name}).", this);
            return;
        }

        occupancy[mover] = new HashSet<Collider> { other };
        HandleNewEntry(mover);
    }

    private void OnTriggerExit(Collider other)
    {
        PlayerMover mover = other.GetComponentInParent<PlayerMover>();
        if (mover == null || !occupancy.TryGetValue(mover, out HashSet<Collider> set)) return;

        set.Remove(other);
        if (set.Count == 0) occupancy.Remove(mover);
    }

    private void FixedUpdate()
    {
        // 복귀가 끝난 도형은 표시를 푼다(복귀 중에는 ExternallyDriven이 켜져 있다).
        if (respawning.Count > 0)
        {
            scratchMovers.Clear();
            foreach (PlayerMover m in respawning)
                if (m == null || !m.ExternallyDriven) scratchMovers.Add(m);
            foreach (PlayerMover m in scratchMovers) respawning.Remove(m);
        }

        if (occupancy.Count == 0 || zone == null) return;

        Bounds zoneBounds = zone.bounds;
        scratchMovers.Clear();
        foreach (KeyValuePair<PlayerMover, HashSet<Collider>> kv in occupancy)
        {
            scratchColliders.Clear();
            foreach (Collider c in kv.Value)
            {
                if (c == null || !c.enabled || !c.gameObject.activeInHierarchy ||
                    !zoneBounds.Intersects(c.bounds))
                    scratchColliders.Add(c);
            }
            foreach (Collider c in scratchColliders) kv.Value.Remove(c);
            if (kv.Key == null || kv.Value.Count == 0) scratchMovers.Add(kv.Key);
        }
        foreach (PlayerMover m in scratchMovers) occupancy.Remove(m);
    }

    private void HandleNewEntry(PlayerMover mover)
    {
        if (debugLog) Debug.Log($"[ZoneEntryDetector] '{name}': '{mover.name}' 새 진입 후보 (mode={mode}, hasFired={hasFired}, " +
                                $"대기 남음={Mathf.Max(0f, nextAllowedTime - Time.time):F2}s, 복귀중={respawning.Contains(mover)}).", this);

        if (respawning.Contains(mover))
        {
            Debug.LogWarning($"[ZoneEntryDetector] '{name}': '{mover.name}'의 복귀 목적지가 감지 구역 안에 있다 — " +
                             "배치 오류다. 복귀는 진입으로 세지 않는다(구역 밖으로 한 번 나온 뒤 들어와야 한다).", this);
            return;
        }

        if (mode == EntryMode.OncePerZone && hasFired) return;
        if (Time.time < nextAllowedTime) return;

        hasFired = true;
        nextAllowedTime = Time.time + Mathf.Max(0f, retriggerCooldownSeconds);
        if (debugLog) Debug.Log($"[ZoneEntryDetector] '{name}': 발동 — OnValidEntry 연결 {OnValidEntry?.GetPersistentEventCount() ?? 0}개.", this);
        OnValidEntry?.Invoke();
        OnValidEntryPlayer?.Invoke(mover.gameObject);
    }

    private void OnDrawGizmosSelected()
    {
        BoxCollider box = GetComponent<BoxCollider>();
        if (box == null) return;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.9f);
        Gizmos.DrawWireCube(box.center, box.size);
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.12f);
        Gizmos.DrawCube(box.center, box.size);
    }
}
