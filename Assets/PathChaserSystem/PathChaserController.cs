using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 경로 추격자 상태 머신 — 실험실 CH1 전용. <see cref="PathChaserAgent"/>(순수 이동체, CH8과 공유하는
/// 클래스)를 통제해 대기 → 추격 → 벽 대기 → 추격(재개) → 종점 대기 → 종료 순서를 진행한다
/// (docs/PRD/PathChaser.md §4).
///
/// [벽 파괴 판정 — BreakableObject를 읽기만 한다] BreakableObject에는 파괴 이벤트가 없고 broken
/// 필드도 private이다. Break()가 부서지는 순간 gameObject.SetActive(false)를 호출하는 걸 코드로
/// 확인했으므로(DestructionSystem/BreakableObject.cs 144~180행), <see cref="IsWallBroken"/>은
/// !wall.gameObject.activeInHierarchy로 판정한다 — DestructionSystem 파일은 한 줄도 고치지 않는다.
///
/// [카트 도착 판정 — RailCartSystem에는 "도착" 개념이 없다] RailCart는 자유 물리 객체라 진행값을
/// 들지 않는다(Assets/CLAUDE.md RailCartSystem 절). 그래서 매 프레임 카트들의 transform.position과
/// arrivalPoint 사이 거리를 직접 재서 판정한다 — RailCartSystem 파일도 손대지 않는다.
///
/// [잡힘 → 구간 복귀 목적지 결정] 벽이 부서졌는지 여부로 SAFE_PRE/SAFE_POST를 가른다.
/// PathChaserCatchZone이 인스펙터 동적 배선이 아니라 NotifyCaught를 코드로 직접 호출한다 —
/// 목적지가 그 순간의 벽 상태에 달려 있어 고정 배선으로는 표현할 수 없다.
///
/// [근접 추격 — 경로를 기준으로 한 목줄] 추격자 주변 detectRadius 안에 보이는 플레이어가 있으면
/// 경로를 벗어난 지점(anchor)을 기억하고 그 플레이어에게 수평으로 붙는다. 플레이어가 anchor에서
/// leashRadius 밖으로 달아나거나 시야에서 사라지면 anchor로 되돌아간 뒤에야 경로를 재개한다 —
/// 돌아가는 도중 재추격해도 anchor는 그대로라, 추격을 반복해도 경로에서 목줄 이상 멀어지지 않는다.
/// 시야(Linecast)가 벽에 막히면 쫓지 않는다 — 추격자는 솔리드 콜라이더가 없어 벽을 통과하므로, 이
/// 검사가 "벽 파괴 전 벽 너머로 못 간다"를 지키는 장치다. 판단은 여기(CH1)서만 하고 에이전트에는
/// 목표 지점만 넘긴다 — 에이전트를 재사용하는 CH8은 이 동작을 갖지 않는다.
/// </summary>
public class PathChaserController : MonoBehaviour
{
    private enum State { Waiting, Chasing, WallWait, EndpointWait, Finished }

    [Header("연결 — 참조 없으면 시작 거부(PRD 확정)")]
    public PathChaserAgent agent;
    public BreakableObject wall;
    public RailCart[] carts;
    public Transform arrivalPoint;
    public SectionHitCounter safePreCounter;
    public SectionHitCounter safePostCounter;

    [Header("선택 — 비워두면 종점 종료 조건이 영원히 미충족으로 남는다")]
    public TeamExitZone teamExitZone;

    [Header("경로 — agent.waypoints 안에서의 인덱스")]
    [Tooltip("추격자가 벽 파괴 전까지 멈춰 기다릴 웨이포인트 인덱스.")]
    public int wallStopWaypointIndex;

    [Header("근접 추격")]
    [Tooltip("추격자 주변 이 반경(수평) 안에 보이는 플레이어가 있으면 경로를 벗어나 쫓는다.")]
    public float detectRadius = 6f;

    [Tooltip("경로를 벗어난 지점에서 이 반경(수평) 밖으로 달아난 플레이어는 포기하고 경로로 돌아간다.")]
    public float leashRadius = 8f;

    [Tooltip("시야 검사에 쓰는 레이어 — 이 레이어의 콜라이더가 사이를 가리면 쫓지 않는다.")]
    public LayerMask sightMask = ~0;

    [Header("타이밍")]
    public float startDelay = 2f;

    [Header("종점 종료 판정")]
    [Tooltip("카트가 이 반경 안에 있어야 '도착'으로 본다.")]
    public float arrivalRadius = 2f;

    [Tooltip("종료(카트 도착 + 팀 전체 조건 충족) 시 1회 발화 — 지금 구독자는 없지만 PRD가 확정한 " +
             "'종료 신호' 요구사항을 위한 자리다.")]
    public UnityEvent OnChapterCleared;

    private State state = State.Waiting;
    private float waitTimer;
    private PlayerShapeIdentity[] players;
    private bool hasAnchor;
    private Vector3 anchor;

    private int FinalWaypointIndex => agent.waypoints.Length - 1;

    private void Start()
    {
        // 시작 지연이 끝나기 전(그리고 시작을 거부한 경우)에는 추격자가 움직이면 안 된다 — 에이전트는
        // 상태를 모르는 이동체라 끄지 않으면 첫 프레임부터 상한(기본 int.MaxValue)까지 달려간다.
        if (agent != null) agent.enabled = false;

        if (agent == null || wall == null || carts == null || carts.Length == 0 || arrivalPoint == null
            || safePreCounter == null || safePostCounter == null
            || agent.waypoints == null || agent.waypoints.Length == 0)
        {
            Debug.LogError("[PathChaserController] 필수 참조(agent/wall/carts/arrivalPoint/" +
                           "safePreCounter/safePostCounter/agent.waypoints)가 비어 있어 시작을 " +
                           "거부한다(PRD 확정 — 참조 없으면 시작 거부).", this);
            enabled = false;
            return;
        }

        agent.maxWaypointIndex = Mathf.Clamp(wallStopWaypointIndex, 0, FinalWaypointIndex);
        players = FindObjectsOfType<PlayerShapeIdentity>();
    }

    private void FixedUpdate()
    {
        if (state == State.Chasing || state == State.WallWait || state == State.EndpointWait)
            UpdateProximityChase();

        switch (state)
        {
            case State.Waiting:
                waitTimer += Time.fixedDeltaTime;
                if (waitTimer >= startDelay)
                {
                    agent.enabled = true;
                    state = State.Chasing;
                }
                break;

            case State.Chasing:
                // 벽 파괴 신호는 위치와 무관하게 즉시 반영한다 — 아직 벽 앞에 도달하지 않았다면
                // 벽 대기 상태를 아예 거치지 않고 그대로 지나간다.
                if (agent.maxWaypointIndex < FinalWaypointIndex && IsWallBroken())
                    agent.maxWaypointIndex = FinalWaypointIndex;

                if (!agent.ReachedLimit) break;

                if (agent.maxWaypointIndex < FinalWaypointIndex)
                    state = State.WallWait; // 벽 앞에 도달했는데 아직 안 부서졌다
                else if (EndConditionMet())
                    FinishChapter();
                else
                    state = State.EndpointWait;
                break;

            case State.WallWait:
                if (IsWallBroken())
                {
                    agent.maxWaypointIndex = FinalWaypointIndex;
                    state = State.Chasing; // 다음 틱부터 이동 재개
                }
                break;

            case State.EndpointWait:
                if (EndConditionMet()) FinishChapter();
                break;

            case State.Finished:
                break;
        }
    }

    private void UpdateProximityChase()
    {
        Vector3 pos = agent.transform.position;
        PlayerShapeIdentity target = null;
        float best = float.MaxValue;
        foreach (PlayerShapeIdentity p in players)
        {
            if (p == null || !p.gameObject.activeInHierarchy) continue;
            Vector3 c = CenterOf(p);
            float d = FlatDistance(c, pos);
            if (d > detectRadius || d >= best) continue;
            if (hasAnchor && FlatDistance(c, anchor) > leashRadius) continue;
            if (!CanSee(pos, p, c)) continue;
            target = p;
            best = d;
        }

        if (target != null)
        {
            if (!hasAnchor)
            {
                hasAnchor = true;
                anchor = pos;
            }
            Vector3 c = CenterOf(target);
            agent.MoveToOverride(new Vector3(c.x, anchor.y, c.z)); // 수평 추격 — 경로 높이 유지
        }
        else if (hasAnchor)
        {
            if (Vector3.Distance(pos, anchor) > 0.05f)
            {
                agent.MoveToOverride(anchor);
            }
            else
            {
                hasAnchor = false;
                agent.ClearOverride();
            }
        }
    }

    private bool CanSee(Vector3 from, PlayerShapeIdentity player, Vector3 to)
    {
        if (!Physics.Linecast(from, to, out RaycastHit hit, sightMask, QueryTriggerInteraction.Ignore)) return true;
        return hit.rigidbody != null && hit.rigidbody.transform == player.transform;
    }

    private static Vector3 CenterOf(PlayerShapeIdentity p) =>
        p.solidCollider != null ? p.solidCollider.bounds.center : p.transform.position;

    private static float FlatDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

    private bool IsWallBroken() => !wall.gameObject.activeInHierarchy;

    private bool EndConditionMet()
    {
        foreach (RailCart cart in carts)
        {
            if (cart == null) return false;
            if (Vector3.Distance(cart.transform.position, arrivalPoint.position) > arrivalRadius) return false;
        }
        return teamExitZone != null && teamExitZone.AllPlayersInside();
    }

    private void FinishChapter()
    {
        state = State.Finished;
        // 접촉(잡힘 트리거) 끄기 — PathChaserCatchZone이 agent와 같은 GameObject에 붙어 있어,
        // 에이전트를 끄는 것만으로 트리거도 함께 꺼진다(별도 "잡힘 무시" 플래그 불필요).
        // 추락 연출은 일부러 SetActive(false) 하나로 끝낸다(YAGNI — 파티클/애니메이션 없음).
        agent.gameObject.SetActive(false);
        OnChapterCleared?.Invoke();
    }

    /// <summary>PathChaserCatchZone이 잡힘을 보고하는 창구. 목적지는 그 순간의 벽 상태로 정한다 —
    /// 인스펙터 고정 배선으로는 표현할 수 없어 코드 직접 호출로만 연결한다.</summary>
    public void NotifyCaught(GameObject playerRoot)
    {
        // 시작 지연 중·시작 거부(enabled=false)·종료 후에는 잡힘이 없다(PRD: 접촉은 시작 지연 후 활성).
        if (!enabled || state == State.Waiting || state == State.Finished) return;

        SectionHitCounter target = IsWallBroken() ? safePostCounter : safePreCounter;
        target.RegisterHit(playerRoot);
    }

    /// <summary>전체 재시작 처리 — 지금 저장소에 이 메서드를 호출할 챕터 재시작 시스템 자체가 없다
    /// (PRD "전체 재시작 처리" 요구사항 충족용 스캐폴딩, 아무도 안 부르는 채로 둬도 된다). PRD
    /// 5단계 중 이 챕터가 스스로 아는 것만 최소로 되돌린다 — 벽/카트 복구는 각자 시스템의 몫이라
    /// 여기서 다루지 않는다.</summary>
    public void ResetChapter()
    {
        state = State.Waiting;
        waitTimer = 0f;
        agent.gameObject.SetActive(true);
        agent.enabled = false;
        agent.ClearOverride();
        hasAnchor = false;
        agent.maxWaypointIndex = Mathf.Clamp(wallStopWaypointIndex, 0, FinalWaypointIndex);
    }
}
