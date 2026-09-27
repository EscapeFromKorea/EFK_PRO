using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// CH8 관리자(C8_MANAGER) — 순찰/의심/추격/수면 상태 머신과 시야(C8_FOV) 판정. docs/PRD/ManagerSurveillance.md §3·§4.
/// 이동은 PathChaserAgent(CH1과 공유하는 순수 이동체)를 <b>수정 없이</b> 바깥에서 조종한다 — 에이전트에 없는
/// 것(순찰 반복·바라보는 방향·정지·속도 전환)은 전부 여기서 채운다(PRD §3 표):
///  - 준비·수면·이탈 정지: agent.enabled = false (FixedUpdate 중단 = 제자리)
///  - 순찰 반복: 닫힌 경로(마지막 지점 ≈ 첫 지점)로 배치하고 ReachedLimit이면 RejoinPath(0)
///  - 의심: MoveToOverride(현재 위치) — 제자리에서 대상 쪽으로 돈다
///  - 추격: MoveToOverride(대상 / 마지막 확인 위치), speed를 chaseSpeed로 교체
///  - 순찰 복귀: NearestPathPoint까지 간 뒤 RejoinPath(CH1과 같은 방식)
///  - 재시작: ResetToStart(에이전트는 꺼 둔 채 — Awake는 이미 돌았다)
///
/// [ManagerFieldOfView를 따로 두지 않았다]
/// 시야 판정은 "바라보는 방향"을 읽어야 하는데 그 방향의 소유자가 이 컴포넌트다. 분리하면 방향을 넘겨주는
/// 배선만 늘어 한 파일에 둔다.
///
/// [바로 빨강] 순찰 중 처음 발견한 대상이 이미 D/2 안이면 같은 판정에서 곧바로 추격으로 간다(PRD §4 확정).
/// [표적 고정] 처음 획득 시 가장 가까운 대상(동거리면 PlayerShapeIdentity.Kind 순). 의심·추격 중에는 그 대상만
/// 보고, 순찰로 돌아온 뒤에만 다시 획득한다.
/// </summary>
public class ManagerAgent : MonoBehaviour
{
    public enum State { Inactive, Patrol, Suspect, Chase, Asleep }

    [Tooltip("이동체(수정 금지 — 바깥에서 조종만 한다). 닫힌 순찰 경로로 waypoints를 채워라.")]
    public PathChaserAgent agent;

    [Tooltip("바라보는 방향으로 돌릴 모델 시각물(선택). 콜라이더 없는 자식이어야 한다.")]
    public Transform visual;

    [Header("감지 (튜닝용 기본값)")]
    [Tooltip("D: 의심 감지거리(U). 추격은 D/2.")]
    public float detectDistance = 10f;
    [Tooltip("F: 시야각(°). 바라보는 방향과 대상 방향의 사이각이 F/2 이하면 시야 안. 360이면 전방위.")]
    [Range(1f, 360f)] public float fieldOfView = 90f;
    [Tooltip("S: 대상이 안 보인 채 이 시간(초)이 지나면 순찰로 돌아간다.")]
    public float loseSeconds = 3f;
    [Tooltip("시야를 가리는 레이어. 이 레이어의 콜라이더가 사이를 막으면 안 보인다.")]
    public LayerMask sightMask = ~0;
    [Tooltip("시야 기준점 높이(관리자 피벗 기준 U). 피벗이 바닥에 있으면 올려 바닥이 시선을 막지 않게 한다.")]
    public float eyeHeight = 0.5f;

    [Header("이동")]
    [Tooltip("순찰 속도(U/s).")]
    public float patrolSpeed = 2f;
    [Tooltip("추격 속도(U/s).")]
    public float chaseSpeed = 4f;
    [Tooltip("바라보는 방향 회전 속도(°/s).")]
    public float turnSpeed = 360f;

    [Header("텔레메트리 (Grafana/Loki)")]
    [Tooltip("깨어 있는 동안 이 간격(초)마다 시야 스냅샷(참가자별 거리·각도·차폐 사유)을 보낸다. 0이면 끈다. " +
             "상태 전이 이벤트는 이 값과 무관하게 항상 보낸다.")]
    public float snapshotInterval = 1f;

    public State Current { get; private set; } = State.Inactive;
    /// <summary>false = 시험 감시 생략 모드(위치 고정·감지/잡힘 OFF). 깨어 있는 상태는 유지된다.</summary>
    public bool Surveillance { get; private set; }
    public bool Paused { get; private set; }
    public PlayerShapeIdentity Target { get; private set; }
    public Vector3 Facing => facing;

    public bool IsAwake => Current == State.Patrol || Current == State.Suspect || Current == State.Chase;
    /// <summary>잡힘 접촉이 유효한가 — 깨어 있고, 감시 모드이고, 이탈 정지가 아닐 때만.</summary>
    public bool CatchActive => IsAwake && Surveillance && !Paused;

    /// <summary>참가자 목록(고정 ID = Kind 순). 표적 동거리 판정과 전원 복귀 순서가 같은 순서를 쓴다.</summary>
    public IReadOnlyList<PlayerShapeIdentity> Players
    {
        get
        {
            if (players == null)
            {
                players = FindObjectsOfType<PlayerShapeIdentity>();
                System.Array.Sort(players, (a, b) => a.Kind.CompareTo(b.Kind));
            }
            return players;
        }
    }

    private PlayerShapeIdentity[] players;
    private Vector3 facing = Vector3.forward;
    private Vector3 startFacing = Vector3.forward;
    private Vector3 lastPos;
    private Vector3 lastSeen;
    private float lostTimer;
    private bool returning;
    private Vector3 returnPoint;
    private int returnNextIndex;

    private const float ArriveThreshold = 0.05f;
    private float nextSnapshot;

    private void Reset()
    {
        agent = GetComponent<PathChaserAgent>();
    }

    private void Awake()
    {
        // 준비 상태에선 움직이면 안 된다 — 에이전트는 상태를 모르는 이동체라 켜 두면 첫 프레임부터 순회한다.
        if (agent != null) agent.enabled = false;
        Vector3 f = Flat(visual != null ? visual.forward : transform.forward);
        startFacing = f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
        facing = startFacing;
    }

    private void FixedUpdate() => Tick(Time.fixedDeltaTime);

    // ── 명령 (컨트롤러가 부른다) ─────────────────────────────────────

    /// <summary>실험 시작: 순찰로 깨운다. surveillance=false면 시험 감시 생략 — 제자리 고정, 감지·잡힘 OFF.</summary>
    public void Activate(bool surveillance)
    {
        if (agent == null) return;
        Surveillance = surveillance;
        Target = null;
        returning = false;
        lostTimer = 0f;
        Current = State.Patrol;
        agent.ClearOverride();
        agent.speed = patrolSpeed;
        lastPos = agent.transform.position;
        ApplyAgentEnabled();
        Log("activate", $"surveillance={surveillance}");
    }

    /// <summary>유효 사용으로 수면 확정. 이동·감지·잡힘이 모두 꺼지고 새 CH8 재시작 전까지 깨지 않는다.</summary>
    public void Sleep()
    {
        Current = State.Asleep;
        Target = null;
        ApplyAgentEnabled();
        Log("sleep");
    }

    /// <summary>팀 실패 등으로 즉시 모든 동작을 끈다.</summary>
    public void Deactivate()
    {
        Current = State.Inactive;
        Target = null;
        ApplyAgentEnabled();
        Log("deactivate");
    }

    /// <summary>경로 첫 지점으로 되돌리고 준비(Inactive) 상태로. 에이전트는 꺼 둔 채 옮긴다 — 물리 콜백 안에서
    /// 불려도 이번 스텝의 MovePosition은 이미 적용된 뒤고, 다음 스텝부터는 에이전트가 꺼져 있어 덮어쓰지 않는다.</summary>
    public void ResetToStart()
    {
        Current = State.Inactive;
        Target = null;
        returning = false;
        lostTimer = 0f;
        if (agent != null)
        {
            agent.enabled = false;
            agent.speed = patrolSpeed;
            agent.ResetToStart();
            lastPos = agent.transform.position;
        }
        facing = startFacing;
        ApplyVisual();
        Log("reset");
    }

    /// <summary>참가자 이탈 정지/재개. 상태는 그대로 두고 이동·감지·잡힘만 멈춘다 — 재개 시 직전 상태로 이어간다.</summary>
    public void SetPaused(bool paused)
    {
        Paused = paused;
        if (agent != null) lastPos = agent.transform.position;
        ApplyAgentEnabled();
        Log("pause", $"paused={paused}");
    }

    private void ApplyAgentEnabled()
    {
        if (agent != null) agent.enabled = IsAwake && Surveillance && !Paused;
    }

    // ── 상태 머신 ─────────────────────────────────────────────────────

    /// <summary>Δt만큼 진행한다. 깨어 있고 감시 모드이고 정지가 아닐 때만 돈다.</summary>
    public void Tick(float dt)
    {
        if (agent == null || !IsAwake || !Surveillance || Paused || dt <= 0f) return;

        Vector3 pos = agent.transform.position;
        Vector3 moved = Flat(pos - lastPos);
        lastPos = pos;

        bool targetVisible = false;
        Vector3 targetCenter = default;
        float targetDist = 0f;
        if (Target != null) targetVisible = CanSee(Target, pos, out targetCenter, out targetDist);

        switch (Current)
        {
            case State.Patrol:
                TickPatrol(pos);
                break;

            case State.Suspect:
                if (targetVisible)
                {
                    lostTimer = 0f;
                    if (targetDist <= detectDistance * 0.5f) EnterChase(Target, targetCenter);
                }
                else if ((lostTimer += dt) >= loseSeconds) ReturnToPatrol(pos);
                break;

            case State.Chase:
                if (targetVisible)
                {
                    lostTimer = 0f;
                    lastSeen = targetCenter;
                }
                else lostTimer += dt;

                if (lostTimer >= loseSeconds) ReturnToPatrol(pos);
                else agent.MoveToOverride(new Vector3(lastSeen.x, pos.y, lastSeen.z)); // 수평 추격 — 경로 높이 유지
                break;
        }

        // 바라보는 방향: 의심·추격 중 대상이 보이면 대상 쪽, 아니면 이동 방향.
        Vector3 desired = facing;
        if (Current != State.Patrol && targetVisible) desired = Flat(targetCenter - pos);
        else if (moved.sqrMagnitude > 1e-8f) desired = moved;
        if (desired.sqrMagnitude > 1e-8f)
            facing = Vector3.RotateTowards(facing, desired.normalized, turnSpeed * Mathf.Deg2Rad * dt, 0f);
        ApplyVisual();

        if (snapshotInterval > 0f && Time.time >= nextSnapshot)
        {
            nextSnapshot = Time.time + snapshotInterval;
            LogSnapshot(pos);
        }
    }

    private void TickPatrol(Vector3 pos)
    {
        if (returning)
        {
            if (Vector3.Distance(pos, returnPoint) <= ArriveThreshold)
            {
                returning = false;
                agent.RejoinPath(returnNextIndex);
                Log("rejoin", $"nextWp={returnNextIndex}");
            }
        }
        else if (agent.ReachedLimit)
        {
            agent.RejoinPath(0); // 에이전트는 종점에서 멈춘다 — 닫힌 경로의 처음으로 되돌려 반복시킨다.
        }

        // 표적 획득: 가장 가까운 보이는 대상. Players가 Kind 순이고 비교가 엄격(<)이라 동거리면 앞 ID가 이긴다.
        PlayerShapeIdentity best = null;
        Vector3 bestCenter = default;
        float bestDist = float.MaxValue;
        foreach (PlayerShapeIdentity p in Players)
        {
            if (!CanSee(p, pos, out Vector3 c, out float d) || d >= bestDist) continue;
            best = p;
            bestCenter = c;
            bestDist = d;
        }
        if (best == null) return;

        // 의심·추격을 같은 판정에서 함께 평가한다 — D/2 안에 갑자기 나타나면 주황을 거치지 않는다.
        if (bestDist <= detectDistance * 0.5f) EnterChase(best, bestCenter);
        else EnterSuspect(best, pos);
    }

    private void EnterSuspect(PlayerShapeIdentity target, Vector3 pos)
    {
        Current = State.Suspect;
        Target = target;
        lostTimer = 0f;
        returning = false;
        Log("suspect", TargetInfo(target, pos));
        agent.MoveToOverride(pos); // 순찰 이동을 멈추고 제자리에서 바라본다.
    }

    private void EnterChase(PlayerShapeIdentity target, Vector3 center)
    {
        State from = Current;
        Current = State.Chase;
        Target = target;
        lostTimer = 0f;
        returning = false;
        lastSeen = center;
        Log("chase", $"from={from} {TargetInfo(target, agent.transform.position)}"); // from=Patrol이면 주황 생략(D/2 즉시 진입)
        agent.speed = chaseSpeed;
    }

    private void ReturnToPatrol(Vector3 pos)
    {
        Log("lost", $"from={Current} target={(Target != null ? Target.name : "null")}" +
            (Current == State.Chase ? $" lastSeen={lastSeen}" : "")); // lastSeen은 추격에서만 갱신된다
        Current = State.Patrol;
        Target = null;
        lostTimer = 0f;
        agent.speed = patrolSpeed;
        // 복귀점은 시작하는 순간 한 번만 정한다 — 매 틱 다시 구하면 가는 도중 목표가 미끄러진다(CH1과 같은 이유).
        returnPoint = agent.NearestPathPoint(pos, out returnNextIndex);
        returning = true;
        agent.MoveToOverride(returnPoint);
    }

    // ── 시야 ─────────────────────────────────────────────────────────

    /// <summary>거리 ≤ D, 사이각 ≤ F/2, 차폐 없음, 살아 있는 참가자. 벽 뒤 대상은 새로 감지되지 않는다(A-02).</summary>
    public bool CanSee(PlayerShapeIdentity p, Vector3 pos, out Vector3 center, out float distance)
    {
        center = default;
        distance = float.MaxValue;
        if (p == null || !IsAlive(p.GetComponent<PlayerMover>())) return false;

        center = p.solidCollider != null ? p.solidCollider.bounds.center : p.transform.position;
        Vector3 eye = pos + Vector3.up * eyeHeight;
        distance = Vector3.Distance(eye, center);
        if (distance > detectDistance) return false;

        if (fieldOfView < 360f)
        {
            Vector3 dir = Flat(center - eye);
            if (dir.sqrMagnitude > 1e-6f && Vector3.Angle(facing, dir) > fieldOfView * 0.5f) return false;
        }

        if (!Physics.Linecast(eye, center, out RaycastHit hit, sightMask, QueryTriggerInteraction.Ignore)) return true;
        return hit.rigidbody != null && hit.rigidbody.transform == p.transform;
    }

    /// <summary>"살아 있는" 참가자 — 활성이고 복귀 연출 중이 아님.
    /// ponytail: 복귀 중 신호로 ExternallyDriven 하나만 본다. CH8엔 매달림 같은 다른 ExternallyDriven 기믹이 없어
    /// 충분하다 — 생기면 RespawnController에 "복귀 중" 조회 창구를 추가해 그걸 읽어라.</summary>
    public static bool IsAlive(PlayerMover m)
    {
        return m != null && m.isActiveAndEnabled && !m.ExternallyDriven;
    }

    // ── 텔레메트리 ───────────────────────────────────────────────────

    private void Log(string evt, string extra = null)
    {
        Vector3 p = agent != null ? agent.transform.position : transform.position;
        LokiTelemetry.Event("ch8_manager_" + evt,
            $"state={Current} pos=({p.x:F1},{p.y:F1},{p.z:F1}) {extra}");
    }

    private string TargetInfo(PlayerShapeIdentity t, Vector3 pos)
    {
        if (t == null) return "target=null";
        Vector3 c = t.solidCollider != null ? t.solidCollider.bounds.center : t.transform.position;
        return $"target={t.name} dist={Vector3.Distance(pos + Vector3.up * eyeHeight, c):F2} D={detectDistance:F1}";
    }

    /// <summary>참가자별로 "왜 보이는지/안 보이는지"를 한 줄에 담는다 — 감지 튜닝(D/F/eyeHeight/sightMask)용.
    /// 사유: ok / dead(복귀 중·비활성) / far(거리 &gt; D) / angle(시야각 밖) / blocked:차폐물이름.</summary>
    private void LogSnapshot(Vector3 pos)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"state={Current} lost={lostTimer:F1}/{loseSeconds:F1} target={(Target != null ? Target.name : "null")} ");
        Vector3 eye = pos + Vector3.up * eyeHeight;
        foreach (PlayerShapeIdentity p in Players)
        {
            if (p == null) continue;
            Vector3 c = p.solidCollider != null ? p.solidCollider.bounds.center : p.transform.position;
            float d = Vector3.Distance(eye, c);
            Vector3 dir = Flat(c - eye);
            float ang = dir.sqrMagnitude > 1e-6f ? Vector3.Angle(facing, dir) : 0f;
            string why;
            if (!IsAlive(p.GetComponent<PlayerMover>())) why = "dead";
            else if (d > detectDistance) why = "far";
            else if (fieldOfView < 360f && ang > fieldOfView * 0.5f) why = "angle";
            else if (Physics.Linecast(eye, c, out RaycastHit hit, sightMask, QueryTriggerInteraction.Ignore)
                     && !(hit.rigidbody != null && hit.rigidbody.transform == p.transform)) why = "blocked:" + hit.collider.name;
            else why = "ok";
            sb.Append($"| {p.Kind} d={d:F1} ang={ang:F0} {why} ");
        }
        LokiTelemetry.Event("ch8_manager_snapshot", sb.ToString());
    }

    private void ApplyVisual()
    {
        if (visual != null && facing.sqrMagnitude > 1e-6f) visual.rotation = Quaternion.LookRotation(facing, Vector3.up);
    }

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    private void OnDrawGizmos()
    {
        Vector3 eye = transform.position + Vector3.up * eyeHeight;
        Vector3 f = Application.isPlaying ? facing : Flat(visual != null ? visual.forward : transform.forward).normalized;
        float half = fieldOfView * 0.5f;
        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.8f);
        Gizmos.DrawLine(eye, eye + Quaternion.Euler(0f, -half, 0f) * f * detectDistance);
        Gizmos.DrawLine(eye, eye + Quaternion.Euler(0f, half, 0f) * f * detectDistance);
        Gizmos.color = new Color(1f, 0.15f, 0.15f, 0.8f);
        Gizmos.DrawLine(eye, eye + f * detectDistance * 0.5f);
    }
}
