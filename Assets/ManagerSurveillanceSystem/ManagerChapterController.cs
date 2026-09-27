using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// CH8 오케스트레이터 — 시작·사용 검증·수면·팀 실패(전원 CH8 복귀)·이탈 정지. docs/PRD/ManagerSurveillance.md §3.
/// PowerMaintenanceController(CH7)와 같은 위치다: 역할 점유·책·혼합 판정은 다시 구현하지 않고 신호만 연결한다.
///
/// [연결점]
///  - startZone(구역)                  : 준비 상태에서 살아 있는 참가자가 한 명이라도 들어오면 자동 시작.
///  - ManagerUsePoint.onUseRequested    : 사용 요청 → 검증 → 수락 시 ConsumeToken + 수면 확정 + 열쇠 노출.
///  - ManagerCatchZone → NotifyCaught   : 팀 실패 → 전원 CH8 복귀 → ResetChapter.
///  - RoleAssignmentManager.Subscribe   : 참가자 이탈 시 관리자 이동·감지·잡힘 정지.
///  - RoleAssignmentManager.ChapterReset: 관리자·상태 초기화(혼합대·열쇠·책은 각자 같은 신호로 스스로 초기화).
///
/// [자동 시작 — "[Space] 실험 시작"을 대체(2026-09-26 사용자 결정)]
/// 역할 점유 요건 없이 구역 진입만으로 감시가 켜진다. "살아 있음"(복귀 연출 중 아님)을 요구하므로, 잡혀서 전원이
/// 구역 안의 시작 지점으로 복귀한 경우 연출이 끝난 뒤에야 다시 시작된다(PRD "안전 복귀 완료 후 준비" 5단계).
/// 구역은 콜라이더가 아니라 Transform + 크기다 — 방 전체를 덮는 트리거를 두면 트리거를 무시하지 않는 다른 레이캐스트
/// (카메라 등)에 걸릴 수 있다.
///
/// [잡힘 우선(A-07) — 큐를 두지 않은 근거]
/// 잡힘은 트리거 콜백(물리 단계)에서, 사용 요청은 키 입력(Update)에서 온다. Unity는 한 프레임에 물리 단계를 Update보다
/// 먼저 돌리므로 같은 프레임에 둘이 겹치면 잡힘이 항상 먼저 처리된다. NotifyCaught는 실패 플래그를 가장 먼저 세우고
/// 같은 프레임 안에 ResetChapter까지 끝내므로, 뒤이어 오는 사용 요청은 준비 상태·권한 0으로 거부된다. 반대로
/// 이미 잠든 관리자는 CatchActive가 false라 접촉 사건 자체가 생기지 않는다.
///
/// [사용 검증에서 "현재 시도" 항목을 뺐다]
/// 잡힘이 같은 프레임에 완성물을 0으로 만들어 옛 시도의 완성물이 살아남을 경로가 없다(MixingStation 주석 참고).
/// "정식 관리자"는 이 컨트롤러가 자기 managerAgent의 사용 지점만 구독하는 것으로 성립한다.
///
/// [전원 복귀 — 참가자마다 다른 지점]
/// RespawnController는 이동 전에 같은 프레임에서 점유 검사를 해서, 셋을 한 SectionSafePoint로 동시에 보내면 전원이
/// "비어 있다"고 판정받아 같은 점에 겹친다. 그래서 startPoints를 참가자 순서(Kind 순)대로 하나씩 준다.
/// </summary>
public class ManagerChapterController : MonoBehaviour, IParticipantPauseReceiver
{
    public enum State
    {
        Ready,      // 시작 전. 관리자 정지.
        Running,    // 관리자 활동(순찰/의심/추격).
        Asleep      // 유효 사용으로 수면 확정. 챕터 재시작 전까지 유지.
    }

    [Header("연결")]
    public RoleAssignmentManager manager;
    public MixingStation station;
    public ManagerUsePoint usePoint;
    public ManagerAgent managerAgent;
    public ManagerKeyPoint keyPoint;
    [Tooltip("비우면 씬에서 찾는다(씬에 하나뿐인 컴포넌트).")]
    public RespawnController respawnController;

    [Header("자동 시작 구역")]
    [Tooltip("구역 중심(회전 반영). 비우면 자동 시작하지 않는다.")]
    public Transform startZone;
    [Tooltip("구역 크기(startZone 로컬 축 기준, U).")]
    public Vector3 startZoneSize = new Vector3(40f, 6f, 40f);

    [Header("전원 복귀")]
    [Tooltip("CH8 시작 지점들. 참가자(Kind 순)마다 하나씩 배정한다 — 셋을 한 지점으로 보내면 겹친다. " +
             "참가자보다 적으면 순환한다(그 경우 SectionSafePoint.backupPoints가 겹침을 피한다).")]
    public SectionSafePoint[] startPoints = new SectionSafePoint[0];

    [Header("수치 (튜닝용 기본값)")]
    [Tooltip("U: 완성물 사용 가능 거리 — 요청자와 관리자 사이(U).")]
    public float useDistance = 3f;

    [Header("시험 옵션")]
    [Tooltip("시험 감시 생략: 시작 전에만 바꿀 수 있다. 관리자 위치 고정, 감지/잡힘 OFF. " +
             "혼합·사용·수면·열쇠 흐름은 그대로(A-09).")]
    [SerializeField] private bool skipSurveillance;

    [Header("이벤트")]
    public UnityEvent onStarted = new UnityEvent();
    public UnityEvent onSlept = new UnityEvent();
    [Tooltip("팀 실패(잡힘) 확정 시 — 전원 복귀를 시작하기 직전.")]
    public UnityEvent onTeamFailed = new UnityEvent();

    public State Current { get; private set; } = State.Ready;
    public bool Paused { get; private set; }
    public bool SkipSurveillance => skipSurveillance;
    /// <summary>팀 실패 처리 중. NotifyCaught 안에서만 true이고 ResetChapter가 끝나면 풀린다.</summary>
    public bool TeamFailed { get; private set; }
    /// <summary>누적 팀 실패 횟수(검증용 — A-07 "정확히 1회").</summary>
    public int FailureCount { get; private set; }
    public string LastRefusal { get; private set; }

    private bool bound;
    private string useRequester;   // 텔레메트리용 — 이번 사용 요청의 요청자·측정 거리(거부 사유와 함께 보낸다).
    private float useDist;

    private void OnEnable() => Bind();
    private void OnDisable() => Unbind();

    private void Start()
    {
        if (respawnController == null) respawnController = FindObjectOfType<RespawnController>();
        if (managerAgent == null || managerAgent.agent == null)
        {
            Debug.LogError("[Manager] managerAgent(또는 그 PathChaserAgent)가 비어 있어 CH8을 시작할 수 없다.", this);
            enabled = false; // Update(자동 시작)가 매 프레임 null을 밟지 않게.
        }
    }

    public void Bind()
    {
        if (bound) return;
        bound = true;
        if (usePoint != null) usePoint.onUseRequested.AddListener(HandleUseRequest);
        if (manager != null)
        {
            manager.ChapterReset += ResetAll;
            manager.Subscribe(this);
        }
    }

    public void Unbind()
    {
        if (!bound) return;
        bound = false;
        if (usePoint != null) usePoint.onUseRequested.RemoveListener(HandleUseRequest);
        if (manager != null)
        {
            manager.ChapterReset -= ResetAll;
            manager.Unsubscribe(this);
        }
    }

    /// <summary>시험 감시 생략 옵션. 준비 상태에서만 바꿀 수 있다.</summary>
    public bool SetSkipSurveillance(bool value)
    {
        if (Current != State.Ready) return false;
        skipSurveillance = value;
        return true;
    }

    // ── 시작 ─────────────────────────────────────────────────────────

    private void Update()
    {
        if (Current != State.Ready || Paused || startZone == null) return;
        foreach (PlayerShapeIdentity p in managerAgent.Players)
        {
            PlayerMover m = p != null ? p.GetComponent<PlayerMover>() : null;
            if (!ManagerAgent.IsAlive(m) || !InStartZone(m.transform.position)) continue;
            RequestStart(m);
            return;
        }
    }

    /// <summary>startZone(회전·위치 반영) 상자 안인가. 스케일은 무시하고 startZoneSize만 쓴다.</summary>
    public bool InStartZone(Vector3 worldPos)
    {
        if (startZone == null) return false;
        Vector3 local = Quaternion.Inverse(startZone.rotation) * (worldPos - startZone.position);
        Vector3 half = startZoneSize * 0.5f;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
    }

    /// <summary>감시 시작. 준비 상태이고 정지가 아닐 때만. 자동 시작 구역에 처음 들어온(또는 복귀 후 남아 있는) 참가자가
    /// 요청자가 되고, "이어서 시작" 권한자로 지정된다.</summary>
    public bool RequestStart(PlayerMover requester)
    {
        if (Current != State.Ready) return Refuse("이미 시작했다.");
        if (Paused) return Refuse("참가자 재접속 대기 중이라 시작할 수 없다.");
        if (requester == null) return Refuse("요청자가 없다.");

        LastRefusal = null;
        Current = State.Running;
        if (manager != null) manager.SetStartOwner(requester);
        managerAgent.Activate(!skipSurveillance);
        Debug.Log($"[Manager] 실험 시작 — 감시 {(skipSurveillance ? "생략(시험)" : "켜짐")}.", this);
        LokiTelemetry.Event("ch8_start", $"by={requester.name} skipSurveillance={skipSurveillance}");
        onStarted.Invoke();
        return true;
    }

    // ── 사용 검증 ────────────────────────────────────────────────────

    /// <summary>사용 요청 판정. 실패 미확정 · 활동 중 · 정지 아님 · 관리자 깨어 있음 · 요청자 살아 있음 · 거리 ≤ U ·
    /// 완성물 보유를 모두 통과해야 수락한다. 수락 시 완성물 소비와 수면 확정을 한 자리에서 처리하고 열쇠를 1회 노출한다.
    /// 거부는 아무 상태도 바꾸지 않는다(권한 보존, M-06/A-05).</summary>
    public bool HandleUse(PlayerMover requester)
    {
        useRequester = requester != null ? requester.name : "null";
        useDist = -1f;
        if (TeamFailed) return RefuseUse("팀 실패가 확정되어 사용할 수 없습니다.");
        if (Current == State.Ready) return RefuseUse("아직 실험이 시작되지 않았습니다.");
        if (Current == State.Asleep) return RefuseUse("관리자는 이미 잠들어 있습니다.");
        if (Paused) return RefuseUse("참가자 재접속 대기 중입니다.");
        if (managerAgent == null || managerAgent.agent == null || !managerAgent.IsAwake) return RefuseUse("관리자가 깨어 있지 않습니다.");
        if (!ManagerAgent.IsAlive(requester)) return RefuseUse("지금은 사용할 수 없는 상태입니다.");
        // 사용 지점(관리자 자식) 기준으로 잰다 — 안내 표시(ManagerUsePoint.promptRadius)와 같은 기준점이어야 "안내는
        // 뜨는데 거리 거부"가 안 생긴다. 사용 지점이 없으면 관리자 위치.
        Vector3 usePos = usePoint != null ? usePoint.transform.position : managerAgent.agent.transform.position;
        useDist = Vector3.Distance(requester.transform.position, usePos);
        if (useDist > useDistance)
            return RefuseUse("관리자와 너무 멉니다. 더 가까이 가세요.");
        if (station == null || !station.ConsumeToken()) return RefuseUse("사용할 완성물이 없습니다.");

        // 소비 + 수면 확정 → 이동/잡힘 OFF → 열쇠 1회. 연출이 없어 취소될 경로가 없다.
        Current = State.Asleep;
        managerAgent.Sleep();
        if (usePoint != null)
        {
            usePoint.SetFeedback("관리자가 잠들었습니다.");
            usePoint.available = false; // 같은 키(LeftControl)를 쓰는 열쇠와 겹치지 않게.
        }
        if (keyPoint != null) keyPoint.Reveal();
        Debug.Log("[Manager] 완성물 사용 수락 — 관리자 수면 확정.", this);
        LokiTelemetry.Event("ch8_use_accept", $"by={useRequester} dist={useDist:F2} U={useDistance:F2}");
        onSlept.Invoke();
        return true;
    }

    private void HandleUseRequest(PlayerMover requester) => HandleUse(requester);

    private bool RefuseUse(string reason)
    {
        LastRefusal = reason;
        if (usePoint != null) usePoint.SetFeedback(reason);
        Debug.Log($"[Manager] 사용 거부 — {reason}", this);
        LokiTelemetry.Event("ch8_use_refuse",
            $"by={useRequester} reason=\"{reason}\" dist={useDist:F2} U={useDistance:F2} state={Current} " +
            $"agentState={(managerAgent != null ? managerAgent.Current.ToString() : "null")} failed={TeamFailed} paused={Paused}");
        return false;
    }

    // ── 팀 실패 ──────────────────────────────────────────────────────

    /// <summary>유효 잡힘(ManagerCatchZone이 CatchActive 확인 후 호출). 실패 플래그를 가장 먼저 세우고, 관리자를 끄고,
    /// 참가자 전원을 각자의 CH8 시작 지점으로 보낸 뒤, 역할·혼합·열쇠·책을 ResetChapter로 초기화하고 준비로 돌아간다.
    /// CH1~7 상태는 건드리지 않는다.</summary>
    public void NotifyCaught(PlayerMover caught)
    {
        if (TeamFailed || Current != State.Running || Paused) return;
        if (managerAgent == null || !managerAgent.CatchActive) return;

        TeamFailed = true;
        FailureCount++;
        managerAgent.Deactivate();
        Debug.Log($"[Manager] 잡힘({(caught != null ? caught.name : "?")}) — 팀 실패, 전원 CH8 시작으로 복귀.", this);
        LokiTelemetry.Event("ch8_caught",
            $"who={(caught != null ? caught.name : "?")} failCount={FailureCount} " +
            $"managerPos={managerAgent.transform.position} whoPos={(caught != null ? caught.transform.position.ToString() : "?")}");
        onTeamFailed.Invoke();

        ReturnAllToStart();

        if (manager != null) manager.ResetChapter(); // → ChapterReset → ResetAll(여기) + 혼합대·열쇠·책
        else ResetAll();
    }

    private void ReturnAllToStart()
    {
        if (respawnController == null) respawnController = FindObjectOfType<RespawnController>();
        if (respawnController == null)
        {
            Debug.LogWarning("[Manager] RespawnController가 없어 전원 복귀를 건너뛴다.", this);
            return;
        }

        var players = managerAgent.Players;
        for (int i = 0; i < players.Count; i++)
        {
            if (players[i] == null) continue;
            SectionSafePoint point = startPoints != null && startPoints.Length > 0 ? startPoints[i % startPoints.Length] : null;
            // ponytail: 복귀 도중(busy)이거나 다른 기믹이 붙잡은(IsHeld) 참가자는 RespawnController가 거절하고 경고만
            // 남긴다 — 그 사람은 제자리에 남는다. CH8엔 붙잡는 기믹이 없어 두지만, 생기면 거절 결과를 받아 재시도하라.
            respawnController.RespawnPlayer(players[i].gameObject, point);
            LokiTelemetry.Event("ch8_respawn",
                $"player={players[i].name} kind={players[i].Kind} point={(point != null ? point.sectionId : "null(공용 체크포인트)")}");
        }
    }

    /// <summary>챕터 재시작(ChapterReset 수신): 관리자를 경로 처음으로 돌리고 준비 상태로. 이탈 정지 상태는 유지한다.</summary>
    public void ResetAll()
    {
        Current = State.Ready;
        TeamFailed = false;
        LastRefusal = null;
        if (managerAgent != null) managerAgent.ResetToStart();
        if (usePoint != null) usePoint.available = true;
        LokiTelemetry.Event("ch8_reset");
    }

    // ── 이탈 정지 ────────────────────────────────────────────────────

    public void SetParticipantPaused(bool paused)
    {
        Paused = paused;
        if (managerAgent != null) managerAgent.SetPaused(paused);
        LokiTelemetry.Event("ch8_pause", $"paused={paused} state={Current}");
    }

    // ── 내부 ─────────────────────────────────────────────────────────

    private bool Refuse(string reason)
    {
        LastRefusal = reason;
        Debug.Log($"[Manager] 시작 거부 — {reason}", this);
        LokiTelemetry.Event("ch8_start_refuse", $"reason=\"{reason}\" state={Current} paused={Paused}");
        return false;
    }

    private void OnDrawGizmos()
    {
        if (startZone == null) return;
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.5f);
        Gizmos.matrix = Matrix4x4.TRS(startZone.position, startZone.rotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, startZoneSize);
    }
}
