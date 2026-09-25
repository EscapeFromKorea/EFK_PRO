using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 서버실 전력 유지 장치(실험실 CH7) 오케스트레이터 — docs/PRD/ServerRoomPower.md.
/// 전력 게이지(감소/회복), 컴퓨터 제출 게이트, 회로 순환 배정, 자동 유지(시험용) 옵션만 맡는다.
/// 역할 점유·문답 판정(RoleClueTerminalSystem)과 배선 판정(WiringPanelSystem)은 다시 구현하지 않고
/// 그쪽이 내보내는 신호를 구독해 연결만 한다. 다른 시스템 폴더의 파일은 수정하지 않는다.
///
/// [연결점 — 이 컨트롤러가 붙는 곳]
///  - QuizTerminal.SetSubmitGate(bool)      : 전력이 모자라면 제출만 잠근다(책 읽기·배선은 계속 가능).
///  - WiringPanel.OnCircuitCompleted        : {장치, 시도, 배정번호, 회로ID} 완료 신호 → 전력 회복 + 다음 회로.
///  - WiringPanel.AssignCircuit             : 회로를 POWER_1 → 2 → 3 → 1 … 순서로 배정한다.
///  - RoleAssignmentManager.Subscribe       : 참가자 이탈 시 전력 감소·휴식 타이머를 함께 멈춘다.
///  - RoleAssignmentManager.ChapterReset    : 챕터 전체 재시작 시 전력·배정을 초기화한다.
///
/// [수치는 전부 인스펙터 값 — 미확정 임시값]
/// M/E0/K/U/d/G/H는 PRD가 "수치 미정"으로 남겼다(팀 회신 대기). 기본값은 시험이 가능하도록 넣은 임시값이며
/// 확정되면 값만 바꾸면 된다. 어떤 값도 코드에 박아 두지 않았다.
///
/// [상태 규칙 요약 — PRD §4]
///  - 준비: 시간이 흘러도 전력이 변하지 않는다. 역할 3개(자동 유지면 2개) 점유 + StartExperiment() 후 시작.
///  - 유지: E = max(0, E - d·Δt). 회로 완료 → E = min(M, E + G), 배정당 1회만 회복, H초 휴식 후 다음 회로.
///          휴식 중에도 전력은 계속 준다. 오배선은 추가 차감이 없다(이 컨트롤러는 오답을 보지 않는다).
///  - 잠금: E &lt; K이면 제출 잠금, 잠긴 뒤에는 E ≥ U가 되어야 풀린다(히스테리시스). E == K로는 새로 잠그지 않는다.
///  - 동시 처리: 회복은 완료 신호를 받은 즉시 게이트 판정까지 끝내므로 "회복 → 제출 허용 검사" 순서가 된다.
///  - 마지막 문항 성공: 감소·새 회로 배정이 함께 멈춘다. 이미 인정된 정답은 취소되지 않는다.
///  - 자동 유지: 시작 전에만 선택. E = M 고정, 회로·정전 로직을 끈다. 전력 역할만 시작 요건에서 뺀다.
///
/// [이 파일이 정하지 않은 것 — 연결점만 열어 둠]
///  - "실험 시작" 입력: PRD에 입력 방식이 없다. 공개 메서드 StartExperiment()만 두고 어떤 입력이 부를지는
///    UI/상호작용 담당이 정한다.
///  - 이탈 시 전력을 "문항 시작 시점 값"으로 되돌릴지: 재접속으로 전력을 회복하는 악용 여부가 팀 확인 대기라
///    구현하지 않았다. 지금은 정지만 하고 값은 그대로 둔다.
///  - 챕터 재시작 시 배선 패널 화면 초기화: WiringPanel에 리셋 API가 없고(#103 소유) 다음 시작 때
///    AssignCircuit이 리셋하므로 건드리지 않는다.
/// </summary>
public class PowerMaintenanceController : MonoBehaviour, IParticipantPauseReceiver
{
    public enum State
    {
        Ready,      // 시작 전. 전력 변화 없음.
        Running,    // 전력 감소·회로 순환 중.
        Completed   // 마지막 문항 성공 후. 감소·새 배정 정지.
    }

    [Header("연결")]
    public RoleAssignmentManager manager;
    public QuizTerminal quiz;
    public WiringPanel wiringPanel;

    [Tooltip("순환 배정할 회로(POWER_1 → 2 → 3). 콘텐츠는 미확정이라 에셋으로 채운다.")]
    public WiringCircuitData[] circuits = new WiringCircuitData[0];

    [Header("시작 요건 — 점유돼 있어야 하는 사물")]
    public RoleSlot bookSlot;
    public RoleSlot computerSlot;
    [Tooltip("전력 담당(배선). 자동 유지 모드에서는 시작 요건에서 빠진다.")]
    public RoleSlot powerSlot;

    [Header("전력 수치 (미확정 임시값 — 확정되면 값만 교체)")]
    [Tooltip("M: 최대 전력.")]
    public float maxPower = 100f;
    [Tooltip("E0: 시작 전력. U 이상이어야 한다.")]
    public float startPower = 100f;
    [Tooltip("K: 이 값 미만이 되면 제출이 잠긴다(양수).")]
    public float lockBelow = 20f;
    [Tooltip("U: 잠긴 뒤 이 값 이상이 되어야 풀린다(K < U ≤ M).")]
    public float unlockAt = 50f;
    [Tooltip("d: 초당 전력 감소.")]
    public float drainPerSecond = 1f;
    [Tooltip("G: 회로 하나를 완성할 때 회복량.")]
    public float gainPerCircuit = 30f;
    [Tooltip("H: 회로 완료 뒤 다음 회로 배정까지 휴식(초).")]
    public float restSeconds = 3f;

    [Header("시험 옵션")]
    [Tooltip("자동 유지: 시작 전에만 바꿀 수 있다. E = M 고정, 회로·정전 로직 꺼짐.")]
    [SerializeField] private bool autoMaintain;

    [Header("이벤트")]
    [Tooltip("제출 잠금 상태가 바뀔 때(true = 잠김).")]
    public UnityEvent<bool> onSubmitLockChanged = new UnityEvent<bool>();
    public UnityEvent onStarted = new UnityEvent();
    [Tooltip("마지막 문항 성공으로 종료될 때.")]
    public UnityEvent onCompleted = new UnityEvent();

    public State Current { get; private set; } = State.Ready;
    public float Power { get; private set; }
    public bool SubmitLocked { get; private set; }
    public bool AutoMaintain => autoMaintain;
    public bool Paused { get; private set; }
    public int AssignmentNumber { get; private set; }
    public bool IsResting => restRemaining > 0f;
    public float RestRemaining => restRemaining;

    private int circuitIndex = -1;
    private int recoveredAssignment;   // 이미 회복을 준 배정번호(같은 배정 중복 회복 방지).
    private float restRemaining;
    private bool bound;

    private void Awake()
    {
        Power = startPower;
    }

    private void OnEnable()
    {
        Bind();
    }

    private void OnDisable()
    {
        Unbind();
    }

    private void Update()
    {
        Tick(Time.deltaTime);
    }

    /// <summary>구독을 연결한다. Editor 자가검증(EditMode)은 OnEnable이 불리지 않아 직접 부른다.</summary>
    public void Bind()
    {
        if (bound) return;
        bound = true;
        if (wiringPanel != null)
        {
            // 씬에 배치된 패널은 직렬화로 채워지지만 코드로 만든 패널은 null이다(WiringPanel도 ?.Invoke로 허용).
            if (wiringPanel.OnCircuitCompleted == null) wiringPanel.OnCircuitCompleted = new WiringPanel.CompletionEvent();
            wiringPanel.OnCircuitCompleted.AddListener(HandleCircuitCompleted);
        }
        if (quiz != null) quiz.onAllCleared.AddListener(HandleAllCleared);
        if (manager != null)
        {
            manager.ChapterReset += ResetAll;
            manager.Subscribe(this); // 구독 즉시 현재 정지 상태로 한 번 호출된다.
        }
    }

    public void Unbind()
    {
        if (!bound) return;
        bound = false;
        if (wiringPanel != null && wiringPanel.OnCircuitCompleted != null)
            wiringPanel.OnCircuitCompleted.RemoveListener(HandleCircuitCompleted);
        if (quiz != null) quiz.onAllCleared.RemoveListener(HandleAllCleared);
        if (manager != null)
        {
            manager.ChapterReset -= ResetAll;
            manager.Unsubscribe(this);
        }
    }

    // ── 설정 ─────────────────────────────────────────────────────────

    /// <summary>수치 관계를 검사한다(PRD §4 "파라미터 관계"): 0 ≤ E0 ≤ M, d ≥ 0, G &gt; 0, 0 &lt; K &lt; U ≤ M,
    /// E0 ≥ U, H ≥ 0.</summary>
    public bool ValidateSettings(out string error)
    {
        if (maxPower <= 0f) { error = "M(최대 전력)은 양수여야 한다."; return false; }
        if (startPower < 0f || startPower > maxPower) { error = "E0는 0 이상 M 이하여야 한다."; return false; }
        if (drainPerSecond < 0f) { error = "d(초당 감소)는 0 이상이어야 한다."; return false; }
        if (gainPerCircuit <= 0f) { error = "G(회로당 회복)는 양수여야 한다."; return false; }
        if (restSeconds < 0f) { error = "H(휴식)는 0 이상이어야 한다."; return false; }
        if (lockBelow <= 0f) { error = "K(잠금 임계)는 양수여야 한다(전력 0에서 항상 잠김 보장)."; return false; }
        if (lockBelow >= unlockAt) { error = "K는 U보다 작아야 한다(K < U)."; return false; }
        if (unlockAt > maxPower) { error = "U는 M 이하여야 한다."; return false; }
        if (startPower < unlockAt) { error = "E0는 U 이상이어야 한다(시작 시 제출 가능)."; return false; }
        error = null;
        return true;
    }

    /// <summary>자동 유지 옵션을 바꾼다. 시작 전(Ready)에만 허용하고 시작 후에는 거부한다.</summary>
    public bool SetAutoMaintain(bool value)
    {
        if (Current != State.Ready)
        {
            Debug.LogWarning("[Power] 시작 후에는 자동 유지 옵션을 바꿀 수 없다. 챕터를 재시작한 뒤 바꿔 주세요.", this);
            return false;
        }

        autoMaintain = value;
        return true;
    }

    // ── 시작 / 종료 / 초기화 ───────────────────────────────────────────

    /// <summary>"실험 시작". 준비 상태이고 설정이 유효하며 필요한 사물이 모두 점유돼 있을 때만 시작한다.
    /// 성공하면 자동 유지가 아닐 때 첫 회로(POWER_1)를 배정한다.</summary>
    public bool StartExperiment()
    {
        if (Current != State.Ready) return false;
        if (Paused)
        {
            Debug.Log("[Power] 참가자 이탈로 정지 중이라 시작할 수 없다.", this);
            return false;
        }

        if (!ValidateSettings(out string error))
        {
            Debug.LogError($"[Power] 수치 설정 오류 — {error} 시작을 거부한다.", this);
            return false;
        }

        if (!RequiredSlotsOccupied(out string missing))
        {
            Debug.Log($"[Power] 시작 요건 미충족 — {missing}", this);
            return false;
        }

        if (!autoMaintain && (circuits == null || circuits.Length == 0))
        {
            Debug.LogError("[Power] 배정할 회로(circuits)가 비어 있다. 시작을 거부한다.", this);
            return false;
        }

        Current = State.Running;
        Power = autoMaintain ? maxPower : startPower;
        restRemaining = 0f;
        recoveredAssignment = 0;
        AssignmentNumber = 0;
        circuitIndex = -1;
        UpdateGate();

        if (!autoMaintain && !AssignNextCircuit())
        {
            // 콘텐츠 오류로 첫 회로를 못 주면 시작 상태를 되돌린다(잘못된 콘텐츠로 시작하지 않는다).
            ResetAll();
            return false;
        }

        Debug.Log($"[Power] 실험 시작 — E={Power:0.##}/{maxPower:0.##}, 자동 유지={autoMaintain}", this);
        onStarted.Invoke();
        return true;
    }

    /// <summary>챕터 전체 재시작(PRD "전체 재시작 처리"): 감소·예약을 취소하고 E0로 복원한 뒤 준비 상태로
    /// 돌아간다. 이전 시도의 지연 회복 신호는 준비 상태에서 무시되므로 무효가 된다.</summary>
    public void ResetAll()
    {
        Current = State.Ready;
        Power = startPower;
        SubmitLocked = false;
        AssignmentNumber = 0;
        recoveredAssignment = 0;
        circuitIndex = -1;
        restRemaining = 0f;
        if (quiz != null) quiz.SetSubmitGate(true);
    }

    // ── 시간 진행 ────────────────────────────────────────────────────

    /// <summary>활성 시간 Δt만큼 진행한다. 정지 중이거나 진행 중이 아니면 아무것도 하지 않는다. 프레임에
    /// 묶이지 않는 순수 로직이라 Editor 자가검증이 그대로 부른다.</summary>
    public void Tick(float dt)
    {
        if (Current != State.Running || Paused || dt <= 0f) return;
        if (autoMaintain)
        {
            Power = maxPower; // 자동 유지: 감소·정전·회로가 모두 꺼진다.
            return;
        }

        Power = Mathf.Max(0f, Power - drainPerSecond * dt);

        if (restRemaining > 0f)
        {
            restRemaining -= dt;
            if (restRemaining <= 0f)
            {
                restRemaining = 0f;
                AssignNextCircuit();
            }
        }

        UpdateGate();
    }

    // ── 신호 수신 ────────────────────────────────────────────────────

    /// <summary>WiringPanel 완료 신호({장치, 시도, 배정번호, 회로ID}). 같은 배정 재전달, 옛 배정, 정지 중,
    /// 준비·종료·자동 유지 상태의 신호는 무시한다(멱등).</summary>
    public void HandleCircuitCompleted(WiringPanel panel, int attempt, int assignmentNumber, string circuitId)
    {
        if (panel != wiringPanel) return;
        if (Current != State.Running || autoMaintain || Paused) return;
        if (assignmentNumber != AssignmentNumber) return;            // 옛 배정의 지연 신호.
        if (recoveredAssignment == assignmentNumber) return;         // 같은 배정은 1번만 회복.

        recoveredAssignment = assignmentNumber;
        Power = Mathf.Min(maxPower, Power + gainPerCircuit);         // 최대치 초과분은 저장하지 않고 버린다.
        UpdateGate();                                                // 회복 처리 → 제출 허용 검사 순서.
        Debug.Log($"[Power] 회로 '{circuitId}'(배정 #{assignmentNumber}) 완료 — E={Power:0.##}/{maxPower:0.##}", this);

        if (restSeconds <= 0f) AssignNextCircuit();
        else restRemaining = restSeconds;
    }

    private void HandleAllCleared()
    {
        if (Current != State.Running) return;
        Current = State.Completed;
        restRemaining = 0f;
        Debug.Log("[Power] 마지막 문항 성공 — 전력 감소·회로 배정을 멈춘다.", this);
        onCompleted.Invoke();
    }

    public void SetParticipantPaused(bool paused)
    {
        Paused = paused;
    }

    // ── 내부 ─────────────────────────────────────────────────────────

    private bool AssignNextCircuit()
    {
        if (wiringPanel == null || circuits == null || circuits.Length == 0) return false;

        int next = (circuitIndex + 1) % circuits.Length;
        int number = AssignmentNumber + 1;
        if (!wiringPanel.AssignCircuit(circuits[next], number)) return false;

        circuitIndex = next;
        AssignmentNumber = number;
        return true;
    }

    /// <summary>히스테리시스: 열린 상태에서는 E &lt; K일 때만 잠그고, 잠긴 뒤에는 E ≥ U일 때만 푼다.</summary>
    private void UpdateGate()
    {
        bool locked = SubmitLocked;
        if (autoMaintain) locked = false;
        else if (!locked && Power < lockBelow) locked = true;
        else if (locked && Power >= unlockAt) locked = false;

        if (locked == SubmitLocked) return;

        SubmitLocked = locked;
        if (quiz != null) quiz.SetSubmitGate(!locked);
        onSubmitLockChanged.Invoke(locked);
        Debug.Log($"[Power] 컴퓨터 제출 {(locked ? "잠김" : "해제")} — E={Power:0.##}", this);
    }

    private bool RequiredSlotsOccupied(out string missing)
    {
        if (bookSlot != null && bookSlot.CurrentOwner == null) { missing = "가이드북 담당이 없다."; return false; }
        if (computerSlot != null && computerSlot.CurrentOwner == null) { missing = "컴퓨터 담당이 없다."; return false; }
        if (!autoMaintain && powerSlot != null && powerSlot.CurrentOwner == null)
        {
            missing = "전력(배선) 담당이 없다.";
            return false;
        }

        missing = null;
        return true;
    }
}
